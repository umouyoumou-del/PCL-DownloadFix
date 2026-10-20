# PclPatcher

用于给官方 PCL 2.99.0.0 单文件程序打 IL 补丁的 dnlib 工具。

## 用途

- 恢复 `PageDownloadInstall.LoaderInit` 的原版 Minecraft 下载加载器。
- 恢复 `PageDownloadModify` 中的 Minecraft 版本卡片。
- 清理官方二进制中内嵌的 CurseForge API 凭据，避免公开发布时泄露。

## 构建与运行

```powershell
dotnet restore .\PclPatcher.csproj
dotnet run --project .\PclPatcher.csproj -- patch `
  "C:\path\to\official\Plain Craft Launcher 2.exe" `
  "C:\path\to\PCL 2.99.0.0 DownloadFix.exe"
```

工具会校验目标 IL 布局；如果找不到预期结构，会报错并拒绝输出。

本工具只面向 PCL 2.99.0.0 的官方二进制，其他版本需要重新分析 IL。
