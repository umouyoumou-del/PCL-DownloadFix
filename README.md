# Plain Craft Launcher (PCL) 下载功能恢复修改版

> 第三方修改版，非 PCL 官方发布。仓库名：`PCL-DownloadFix`。

本项目基于官方 **PCL 2.99.0.0 正式版** 的单文件程序，通过 IL 补丁恢复“下载游戏本体”入口，并附带可复现的补丁工具和 PCL 开源源码快照。

- 上游项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)
- 上游作者：龙腾猫跃 / LTCat
- 赞助链接：<https://meloong.com/afd/a/LTCat>
- 上游许可：[`LICENCE`](LICENCE)

## 功能说明

已恢复：

- “下载”页面使用正常的 Mojang 版本清单加载器。
- 可以查看正式版、预览版、远古版、愚人节版等 Minecraft 版本列表。
- 可以点击“开始下载”下载 Minecraft 游戏本体。
- 修改已安装版本时，Minecraft 版本卡片重新显示。
- 保留官方二进制自带的微软登录、Java 检测、启动器界面等完整资源。

未恢复：

- 上游出于合规原因同时关闭的整合包导入/安装入口。
- CurseForge 相关 API 功能：发布版二进制中的 CurseForge API 凭据已替换为 `REDACTED`，因此相关请求会失败；这不影响 Minecraft 原版游戏下载。

## 目录结构

```text
.
├─ dist/
│  ├─ PCL 2.99.0.0 DownloadFix.exe   # 可直接运行的修改版
│  └─ SHA256SUMS.txt
├─ patcher/                          # 用于复现二进制补丁的 dnlib 工具
│  ├─ PclPatcher.csproj
│  └─ Program.cs
├─ source/                           # PCL 2.99.0.0 开源代码快照
│  └─ ...                            # 已把 RestrictMcDownload 改为 False
├─ docs/
│  └─ IL-PATCH-INFO.md
├─ LICENCE                           # PCL 原项目许可
└─ README.md
```

## 使用方法

1. 从本仓库 `dist/` 下载 `PCL 2.99.0.0 DownloadFix.exe`。
2. 建议先备份你原来的官方 PCL2 程序。
3. 将修改版放在你希望使用的目录，直接双击运行。
4. 进入 **下载 → 原版游戏**，选择 Minecraft 版本，点击 **开始下载**。
5. 如果 PCL 提示启动器更新，请不要直接更新；官方更新可能会覆盖修改版。

本程序是未签名的第三方修改二进制，Windows SmartScreen/杀毒软件可能提示风险。请先核对下方 SHA256，再自行决定是否运行；不要从非本仓库来源下载。

## 文件校验

```text
官方 PCL 2.99.0.0 原文件：
6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386

本仓库修改版：
4c8cd4b37108dfc096c7624930138c0f9ec3e2c427f91e0a8c5d20f7e4546845
```

PowerShell 校验：

```powershell
Get-FileHash ".\PCL 2.99.0.0 DownloadFix.exe" -Algorithm SHA256
```

## 二进制补丁原理

补丁工具位于 `patcher/`，使用 dnlib 直接修改官方 PCL 2.99.0.0 的 IL：

1. `PCL.PageDownloadInstall.LoaderInit()`
   - 原版用固定抛出异常的“关闭下载”LoaderTask 替代下载加载器。
   - 修改为使用 `ModDownload.DlClientListLoader`。
2. `PCL.PageDownloadModify.LoadInstanceFinished(...)`
   - 将 `CardMinecraft.Visibility` 从 `Collapsed` 改为 `Visible`。
3. 安全清理
   - 将官方二进制内存中的 CurseForge API 凭据替换为 `REDACTED`，避免公开发布时泄露凭据。

更详细的方法/IL 说明见 [`docs/IL-PATCH-INFO.md`](docs/IL-PATCH-INFO.md)。

## 复现补丁

需要 .NET SDK 10 或更高版本，以及一份官方 PCL 2.99.0.0 单文件程序：

```powershell
dotnet restore .\patcher\PclPatcher.csproj
dotnet run --project .\patcher\PclPatcher.csproj -- patch `
  "C:\path\to\official\Plain Craft Launcher 2.exe" `
  ".\dist\PCL 2.99.0.0 DownloadFix.exe"
```

注意：

- 补丁工具只针对 PCL 2.99.0.0 的特定 IL 布局，不能保证适用于其他版本。
- 工具会拒绝在找不到预期 IL 或清理凭据失败时输出结果。
- 官方原程序不包含在本仓库中。你需要从 PCL 官方渠道自行获取。

## 源码构建

`source/` 是从上游 GitHub 源码同步的 PCL 2.99.0.0 快照，并且已经将：

```vb
Public Const RestrictMcDownload As Boolean = False
```

作为源码功能恢复版本。

源码构建建议使用 Visual Studio 2022 的 MSBuild、.NET Framework 4.8 目标包和 Roslyn 编译器。源码版和二进制修改版的区别：

- 源码版不会包含官方发布版中的 OAuth Client ID、CurseForge API Key 等发布配置。
- 如需微软正版登录，请配置自己的 `PCL_MS_CLIENT_ID`；否则可使用离线登录。
- 如需 CurseForge，请配置你自己的 `PCL_CURSEFORGE_API_KEY`。

## 数据与隐私

- 本仓库不包含任何用户的 PCL 配置、注册表、账号令牌、日志、游戏目录或本机路径。
- 修改版本身不包含个人账号数据；登录状态仍由你本机的 PCL 配置管理。
- 发布前已将二进制中的 CurseForge API 凭据清理掉。
- 测试过程中产生的临时配置、缓存、反编译输出和日志均已删除。

## 免责声明

- 本仓库是第三方基于 PCL 独立进行的二次修改，与龙腾猫跃/PCL 官方无隶属或授权关系，不是官方版本。
- 上游关闭 Minecraft 下载功能是出于合规原因。本项目仅用于个人学习、逆向分析和研究；请自行确认所在地区的法律法规、Minecraft 用户协议和 PCL 分发许可，不要商用、倒卖、公开传播或冒充官方版本。
- 修改版按“现状”提供，不提供任何明示或暗示担保。因使用本修改版造成的账号、数据、游戏或法律风险由使用者自行承担。
- 如果你喜欢 PCL，请支持上游作者：<https://meloong.com/afd/a/LTCat>。
