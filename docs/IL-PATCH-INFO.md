# PCL 2.99.0.0 IL 补丁说明

目标程序：`Plain Craft Launcher 2, Version=2.99.0.0`

原始官方文件 SHA256：

```text
6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386
```

修改后文件 SHA256：

```text
4c8cd4b37108dfc096c7624930138c0f9ec3e2c427f91e0a8c5d20f7e4546845
```

## Patch 1：恢复下载页加载器

方法：

```text
PCL.PageDownloadInstall.LoaderInit()
Metadata token: 06000513
```

原版逻辑会构造一个名为“关闭下载”的 `LoaderTask`，其回调直接抛出限制异常。

修改后的效果：

```csharp
ControlInit();
PageLoaderInit(
    LoadMinecraft,
    PanLoad,
    PanAllBack,
    null,
    ModDownload.DlClientListLoader,
    LoadMinecraft_OnFinish
);
```

这样下载页会走正常的 Mojang 版本清单加载流程，并生成 Minecraft 版本列表。

## Patch 2：恢复修改版本页的 Minecraft 卡片

方法：

```text
PCL.PageDownloadModify.LoadInstanceFinished(
    ModLoader.LoaderTask<ModMinecraft.McInstance, object>)
Metadata token: 06000652
```

原版在该方法中把 `CardMinecraft.Visibility` 固定编译为：

```text
ldc.i4.2   // Visibility.Collapsed
```

修改为：

```text
ldc.i4.0   // Visibility.Visible
```

## 安全处理：清理内嵌 CurseForge API 凭据

官方 PCL 发布版会在二进制中携带用于 CurseForge 请求的 API 凭据。该值不应被原样公开到 GitHub。

补丁工具会定位 `PCL.ModSecret.SecretHeadersSign` 中 `Convert.FromBase64String(...)` 前面的凭据字符串，并完成：

1. 将所有引用该凭据的 `ldstr` 指令替换为 `REDACTED` 对应的占位值；
2. 清理 PE 映像中残留的原始凭据字节；
3. 重新加载输出程序，检查 IL、元数据和 PE 中均不存在原始凭据。

副作用：修改版的 CurseForge API 请求不可用。这不影响 Minecraft 游戏本体下载。

## 验证结果

修改版已实际启动并打开下载页，UI Automation 检测到：

- `原版游戏`
- `最新版本`
- `正式版`
- `预览版`
- `远古版`
- `愚人节版`
- `开始下载`

日志中确认：

```text
LoaderTask`2 DlClientList Main: Loading -> Finished
```

测试时使用的 PCL 注册表设置在测试后已按备份恢复。
