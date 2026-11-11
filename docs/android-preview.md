# Android 移动工作区预览

Android 8.0（API 26）及以上，APK 包含 ARM64 手机与 x86_64 模拟器代码。此阶段提供实例管理、资源查找和下载；**尚不能在 Android 启动 Minecraft Java 版**，也没有接入 Android 微软登录或模组加载器。

## 页面与数据

底部导航包含“实例”“资源”“设置”。支持中文名称、分组、固定、改名、自选封面、实例 ZIP 备份；资源使用现有中文映射与对应项目图标，可选择来源、参考游戏版本和具体文件。资源包与光影保存到实例目录，世界可以导入，数据包写入所选已有存档。整合包仅下载原文件；模组按所选参考加载器下载，必需依赖需另行准备。

创建实例时填写的游戏版本，以及编辑区的原版/Fabric/Forge/NeoForge/Quilt 选择都是**资源兼容性参考**，不会伪造游戏已安装状态，也不提供不可用的“启动游戏”按钮。资源包下载不等于已经在 Minecraft 中启用。英文摘要无中文缓存时保留原文。

数据保存在应用专属目录，卸载应用会删除。升级/换机前从实例页导出备份到自己选定的位置。备份不包含账号或系统凭据；目前需在支持 ZIP 的设备上解压后手动迁移，应用内完整备份恢复仍待接入。

## 签名与安装

预览包采用独立、可复用的自签名 Android 证书：`CN=HelL Android Preview, O=addxiaoyi`。证书 SHA-256：

`FA:F0:DF:5A:64:5D:37:0E:05:5C:5D:04:6D:CC:03:23:A0:F1:41:D5:2B:6A:08:56:6C:24:D5:93:CF:68:60:7B`

同一签名和应用 ID 的后续包可直接升级。CI 从仓库机密读取密钥，私钥不进入源码、APK 或附件；本机受限目录另保存了密钥和绑定 Windows 当前用户的加密密码。请维护者备份密钥，丢失后无法继续对旧安装进行同签名更新。预览签名不代表 Play 商店认证、SignPath 或平台背书。

从 `Android preview APK` 的 `android-preview-apk` 构建附件获取 APK、SHA256SUMS 和 signature.txt。安装前核对文件校验值和证书。首次通过文件管理器安装时，Android 可能要求允许该来源安装应用，按当前系统提示操作；不要关闭系统安全检测。

## 构建与验收

工作流 `.github/workflows/android-preview.yml` 使用 .NET 10 Android workload、Java 21 和 Android SDK 36，输出签名 APK 后调用 `apksigner verify --print-certs`，再在 API 35 x86_64 模拟器中安装、运行页面自检、截图并重启。

桌面预览使用同一 `HelL.Mobile` 页面：`dotnet run --project tools/HelL.Mobile.Preview -c Release -- <全新绝对目录>`。它只用于布局检查，不能作为 Android 原生运行证据。

APK 原生报告保存到 `android-native-evidence` 附件。测试只使用应用的独立 `ui-smoke` 目录和虚构资源，不访问日常账号。真机触控、软键盘、系统文件选择器以及不同厂商后台限制仍需实际设备验收。
