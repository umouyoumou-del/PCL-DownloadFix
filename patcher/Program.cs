using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "patch")
            return Patch(args[1], args[2]);

        if (args.Length < 1)
        {
            Console.WriteLine("usage:");
            Console.WriteLine("  PclPatcher <assembly> [type-filter] [method-filter]");
            Console.WriteLine("  PclPatcher patch <input.exe> <output.exe>");
            return 1;
        }

        Inspect(args[0],
            args.Length > 1 ? args[1] : "",
            args.Length > 2 ? args[2] : "");
        return 0;
    }

    static int Patch(string inputPath, string outputPath)
    {
        using var mod = ModuleDefMD.Load(inputPath);
        Console.WriteLine($"Loaded: {inputPath}");
        Console.WriteLine($"Assembly: {mod.Assembly?.FullName}");

        var type = mod.Find("PCL.PageDownloadInstall", false)
                   ?? throw new InvalidOperationException("PageDownloadInstall type not found");
        var method = type.FindMethod("LoaderInit")
                     ?? throw new InvalidOperationException("PageDownloadInstall.LoaderInit not found");
        if (!method.HasBody)
            throw new InvalidOperationException("LoaderInit has no body");

        var instructions = method.Body.Instructions;
        var closeText = instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "关闭下载")
                        ?? throw new InvalidOperationException("LoaderInit restriction marker not found");
        var startIndex = instructions.IndexOf(closeText);

        var loaderCtor = instructions.Skip(startIndex)
            .FirstOrDefault(i => i.OpCode == OpCodes.Newobj &&
                                 i.Operand is IMethod m &&
                                 m.DeclaringType?.Name?.Contains("LoaderTask") == true)
            ?? throw new InvalidOperationException("Restricted LoaderTask constructor not found");
        var endIndex = instructions.IndexOf(loaderCtor);
        if (endIndex < startIndex)
            throw new InvalidOperationException("Unexpected IL layout");

        var modDownload = mod.Find("PCL.ModDownload", false)
                          ?? throw new InvalidOperationException("ModDownload type not found");
        var loaderField = modDownload.FindField("DlClientListLoader")
                          ?? throw new InvalidOperationException("DlClientListLoader field not found");
        if (loaderField.FieldType.FullName.IndexOf("LoaderTask", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("Unexpected DlClientListLoader type: " + loaderField.FieldType.FullName);

        Console.WriteLine($"Replacing IL_{closeText.Offset:X4}..IL_{loaderCtor.Offset:X4} with ldsfld {loaderField.FullName}");
        for (var i = endIndex; i >= startIndex; i--)
            instructions.RemoveAt(i);
        instructions.Insert(startIndex, Instruction.Create(OpCodes.Ldsfld, loaderField));

        // Restore the Minecraft card in the "modify instance" page. In the
        // restricted build this was constant-folded to Visibility.Collapsed (2).
        var modifyType = mod.Find("PCL.PageDownloadModify", false)
                         ?? throw new InvalidOperationException("PageDownloadModify type not found");
        var cardPatches = 0;
        foreach (var candidate in modifyType.Methods.Where(x => x.HasBody))
        {
            var bodyInstructions = candidate.Body.Instructions;
            for (var i = 1; i < bodyInstructions.Count; i++)
            {
                if (bodyInstructions[i].OpCode != OpCodes.Ldc_I4_2)
                    continue;
                if (bodyInstructions[i - 1].OpCode != OpCodes.Callvirt)
                    continue;
                if (bodyInstructions[i - 1].Operand is IMethod called &&
                    called.Name == "get_CardMinecraft")
                {
                    bodyInstructions[i] = Instruction.Create(OpCodes.Ldc_I4_0);
                    cardPatches++;
                }
            }
        }
        if (cardPatches != 1)
            throw new InvalidOperationException($"Expected exactly one CardMinecraft visibility patch, found {cardPatches}");
        Console.WriteLine("Patched PageDownloadModify CardMinecraft visibility: Collapsed -> Visible");

        var secretsToScrub = RedactEmbeddedSecrets(mod);

        var opts = new ModuleWriterOptions(mod)
        {
            Logger = DummyLogger.NoThrowInstance,
            MetadataOptions = { Flags = MetadataFlags.PreserveAll }
        };
        mod.Write(outputPath, opts);
        ScrubRawCredentialBytes(outputPath, secretsToScrub);
        VerifySecretsScrubbed(outputPath, secretsToScrub);
        Console.WriteLine($"Written: {outputPath}");
        return 0;
    }

    static List<string> RedactEmbeddedSecrets(ModuleDef mod)
    {
        var secrets = new List<string>();
        var modSecret = mod.Find("PCL.ModSecret", false)
                        ?? throw new InvalidOperationException("ModSecret type not found");
        var signMethod = modSecret.FindMethod("SecretHeadersSign")
                         ?? throw new InvalidOperationException("SecretHeadersSign not found");
        if (!signMethod.HasBody)
            throw new InvalidOperationException("SecretHeadersSign has no body");

        var headerInstructions = signMethod.Body.Instructions;
        var fromBase64Index = -1;
        for (var i = 0; i < headerInstructions.Count; i++)
        {
            if (headerInstructions[i].OpCode == OpCodes.Call &&
                headerInstructions[i].Operand is IMethod called &&
                called.Name == "FromBase64String")
            {
                fromBase64Index = i;
                break;
            }
        }
        if (fromBase64Index < 1)
            throw new InvalidOperationException("Embedded API credential call not found");

        var secretInstruction = headerInstructions[fromBase64Index - 1];
        if (secretInstruction.OpCode != OpCodes.Ldstr || secretInstruction.Operand is not string encodedSecret)
            throw new InvalidOperationException("Unexpected API credential IL layout");

        secrets.Add(encodedSecret);
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedSecret));
            if (!string.IsNullOrWhiteSpace(decoded))
                secrets.Add(decoded);
        }
        catch
        {
            // Not fatal: the encoded literal is still scrubbed below.
        }

        var placeholder = Convert.ToBase64String(Encoding.UTF8.GetBytes("REDACTED"));
        var replacementCount = 0;
        foreach (var candidate in mod.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            foreach (var instr in candidate.Body.Instructions)
            {
                if (instr.OpCode == OpCodes.Ldstr &&
                    instr.Operand is string s &&
                    secrets.Contains(s, StringComparer.Ordinal))
                {
                    instr.Operand = placeholder;
                    replacementCount++;
                }
            }
        }
        foreach (var field in mod.GetTypes().SelectMany(t => t.Fields))
        {
            if (field.Constant?.Value is string s &&
                secrets.Contains(s, StringComparer.Ordinal))
            {
                field.Constant.Value = placeholder;
                replacementCount++;
            }
        }
        if (replacementCount == 0)
            throw new InvalidOperationException("No embedded API credential literals were replaced");

        Console.WriteLine($"Redacted embedded API credential: {replacementCount} reference(s), original encoded length {encodedSecret.Length}");
        return secrets.Distinct(StringComparer.Ordinal).ToList();
    }

    static void ScrubRawCredentialBytes(string path, IEnumerable<string> secrets)
    {
        var bytes = File.ReadAllBytes(path);
        var replacements = 0;
        foreach (var secret in secrets.Distinct(StringComparer.Ordinal))
        {
            var utf16Needle = Encoding.Unicode.GetBytes(secret);
            var utf16Replacement = Encoding.Unicode.GetBytes(new string('R', secret.Length));
            bytes = ReplaceAll(bytes, utf16Needle, utf16Replacement, out var utf16Count);
            replacements += utf16Count;

            var utf8Needle = Encoding.UTF8.GetBytes(secret);
            var utf8Replacement = Enumerable.Repeat((byte)'R', utf8Needle.Length).ToArray();
            bytes = ReplaceAll(bytes, utf8Needle, utf8Replacement, out var utf8Count);
            replacements += utf8Count;
        }
        if (replacements == 0)
            throw new InvalidOperationException("No raw credential bytes were found to scrub");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"Raw credential scrub: {replacements} byte-pattern occurrence(s) replaced");
    }

    static byte[] ReplaceAll(byte[] data, byte[] needle, byte[] replacement, out int count)
    {
        count = 0;
        if (needle.Length == 0 || needle.Length != replacement.Length || data.Length < needle.Length)
            return data;

        var output = (byte[])data.Clone();
        for (var i = 0; i <= output.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (output[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (!match)
                continue;
            for (var j = 0; j < replacement.Length; j++)
                output[i + j] = replacement[j];
            count++;
            i += needle.Length - 1;
        }
        return output;
    }

    static void VerifySecretsScrubbed(string path, IEnumerable<string> secrets)
    {
        var secretList = secrets.Distinct(StringComparer.Ordinal).ToList();
        using (var check = ModuleDefMD.Load(path))
        {
            foreach (var candidate in check.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
            {
                foreach (var instr in candidate.Body.Instructions)
                {
                    if (instr.OpCode == OpCodes.Ldstr &&
                        instr.Operand is string s &&
                        secretList.Contains(s, StringComparer.Ordinal))
                        throw new InvalidOperationException("Scrubbed credential is still present in IL");
                }
            }
            foreach (var field in check.GetTypes().SelectMany(t => t.Fields))
            {
                if (field.Constant?.Value is string s &&
                    secretList.Contains(s, StringComparer.Ordinal))
                    throw new InvalidOperationException("Scrubbed credential is still present in metadata");
            }
        }

        var bytes = File.ReadAllBytes(path);
        foreach (var secret in secretList)
        {
            if (Contains(bytes, Encoding.UTF8.GetBytes(secret)) ||
                Contains(bytes, Encoding.Unicode.GetBytes(secret)))
                throw new InvalidOperationException("Scrubbed credential is still present in the PE image");
        }
        Console.WriteLine("Embedded API credential scrub verification: passed");
    }

    static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return false;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return true;
        }
        return false;
    }

    static void Inspect(string path, string typeFilter, string methodFilter)
    {
        using var mod = ModuleDefMD.Load(path);
        Console.WriteLine($"Module: {mod.Name}, asm: {mod.Assembly?.FullName}");
        foreach (var type in mod.GetTypes().Where(t => t.FullName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"\nTYPE {type.MDToken.Raw:X8} {type.FullName} base={type.BaseType}");
            Console.WriteLine("  Fields:");
            foreach (var f in type.Fields)
                Console.WriteLine($"    {f.MDToken.Raw:X8} {f.FieldType} {f.Name}");
            Console.WriteLine("  Methods:");
            foreach (var m in type.Methods)
            {
                Console.WriteLine($"    {m.MDToken.Raw:X8} {m.FullName}");
                Console.WriteLine($"      sig={m.MethodSig}");
            }
            foreach (var m in type.Methods.Where(m => methodFilter == "" || m.Name.String.Contains(methodFilter, StringComparison.OrdinalIgnoreCase)))
            {
                if (!m.HasBody) continue;
                Console.WriteLine($"\n  IL {m.MDToken.Raw:X8} {m.FullName}");
                DumpIL(m);
            }
        }
    }

    static void DumpIL(MethodDef m)
    {
        foreach (var instr in m.Body.Instructions)
            Console.WriteLine($"    IL_{instr.Offset:X4}: {instr.OpCode,-12} {FormatOperand(instr.Operand)}");
        if (m.Body.HasExceptionHandlers)
        {
            Console.WriteLine("  ExceptionHandlers:");
            foreach (var eh in m.Body.ExceptionHandlers)
                Console.WriteLine($"    {eh.HandlerType} try {eh.TryStart?.Offset:X4}-{eh.TryEnd?.Offset:X4} handler {eh.HandlerStart?.Offset:X4}-{eh.HandlerEnd?.Offset:X4} catch={eh.CatchType}");
        }
    }

    static string FormatOperand(object? op)
    {
        if (op == null) return "";
        if (op is Instruction i) return $"IL_{i.Offset:X4}";
        if (op is Instruction[] arr) return string.Join(", ", arr.Select(x => $"IL_{x.Offset:X4}"));
        return op.ToString() ?? "";
    }
}
