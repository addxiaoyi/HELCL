# 下载与首次运行

Avalonia 跨平台版目前为 **3.2.0-preview.3 迁移预览版**，不是 Windows WPF 完整版的直接替代。提供原版安装与启动、Java 管理、微软登录与刷新、实例列表/封面双视图、固定/分组/改名和自定义封面；资源页支持中文搜索、真实图标、版本筛选、资源包/光影下载、世界导入与向已有存档安装数据包。模组依赖需另行安装，整合包仅下载原始文件；加载器安装与启动、AI、插件、完整存档管理和旧版外观设置仍在迁移。试玩遵循游戏自身限制。

发布包由仓库的 Portable preview 工作流生成。此文档不表示这些包已公开发布或已获得 SignPath 签名。请从项目维护者确认的发布仓库下载，核对版本、平台、架构和同批 SHA256SUMS；不要从陌生转发链接下载。

## 免安装便携包

- Windows 10/11 x64：`HelL-3.2.0-preview.3-win-x64.zip`。完整解压到个人文件夹，双击 `HelL.Desktop.exe`。无需另装 .NET，也不要求管理员权限。
- Linux x64 / ARM64：对应 `linux-x64.tar.gz` / `linux-arm64.tar.gz`，解压后运行 `./HelL.Desktop`。依赖图形会话和系统原生库；建议 Ubuntu 24.04。缺库时安装系统提供的 fontconfig、X11、ICE/SM、libicu 与 OpenGL 驱动。中文显示需安装中文字体（如 Noto Sans CJK）。这不是静态链接程序，也不是 AppImage。
- macOS 14+ Intel / Apple Silicon：对应 `osx-x64.zip` / `osx-arm64.zip`，解压整个文件夹，再打开 `HelL.app`。`.app` 和旁边的 `data` 保持在同一个可写文件夹中。无需安装 .NET。macOS 包由 macOS runner 生成并做 ad-hoc 完整性签名，**不具备 Apple Developer ID 身份或公证**。

Windows ARM64 尚无专用包。Linux ARM64 和 Apple Silicon 上旧版 Minecraft 的本地库可能不兼容；跨平台游戏实测会按版本逐步补齐。打包成功不代表所有版本均可运行。

## macOS 第一次打开

第一次打开请尝试右键点击 `HelL.app` →「打开」；部分新版 macOS 仍会阻止打开。确认下载来源可信、校验一致后，先尝试打开一次，再按系统提示前往「系统设置 → 隐私与安全性 → 仍要打开」。可用按钮因 macOS 版本和设备管理策略而异。

如果系统提示包含恶意软件、已损坏，或校验不一致，请停止运行并反馈维护者。不要关闭 Gatekeeper、删除隔离属性或修改安全软件设置。详见 [Apple 官方说明](https://support.apple.com/102445)。

## 数据、迁移与更新

Windows/Linux 默认把配置、实例、Java 和缓存放在程序旁的 `data/`；macOS 使用 `.app` 旁的 `data/`，不修改签名后的应用内部。可通过绝对路径环境变量 `TIPCL_HOME` 更换位置。目录不可写、应用被 macOS 临时转置运行或数据目录已被占用时，会显示错误而不会悄悄切换位置。

不要让预览版直接使用完整版本的生产数据目录。需要试用旧实例时，先备份并复制到独立目录。实例中可能含平台专用模组、Java 路径和本地库；拷贝数据不是跨系统兼容性保证。预览版使用独立的 `portable-account.json`，不会读取旧版 `accounts.json`。

## 微软登录与凭据保护

点击「登录微软」，在系统浏览器的微软页面中输入界面显示的设备码。无需将密码或设备码发送给维护者。完成后取消「试玩模式」即可使用所登录角色启动；启动前自动检查令牌，授权失效时会提示重新登录。下载和授权等待都可取消。

「在本机安全保存登录」启用时，令牌使用 AES-256-GCM 加密后写入便携数据目录，随机密钥保存在 Windows 凭据管理器、macOS 登录钥匙串或 Linux Secret Service。Linux 需要 `libsecret-tools` 和已解锁的桌面钥匙环；缺少时明确提示仅保留本次会话，不降级为明文。

便携数据文件可随程序移动，但密钥绑定当前系统用户。跨电脑或跨系统移动后需要重新登录。Windows/Mac 的系统钥匙环若弹出访问提示，请本人判断；启动器不会关闭安全设置。取消保存或退出登录会移除账号密文，并尝试清理对应系统密钥；清理失败会提示。不要把含个人数据的 `data` 文件夹分享或打包发布。

更新时先退出游戏和启动器，备份 `data`，把新包解压到新目录，再复制数据。确认新版本正常后再自行清理旧包。预览版不执行自动覆盖更新；完整目录应一起移动，不能只复制 EXE。

## 校验与签名状态

Windows：`Get-FileHash .\HelL-3.2.0-preview.3-win-x64.zip -Algorithm SHA256`。

Linux：`sha256sum -c SHA256SUMS`。macOS：`shasum -a 256 -c SHA256SUMS`。校验文件与包应从同一个可信发布渠道获取；SHA-256 本身不是发布者身份证明。

每个平台随包的 JSON 明确记录 `signature`（`unsigned` / `authenticode` / `adhoc`），并记录包大小和 SHA-256。未签名预览版可能出现 Windows 警告。只有经过 SignPath 审核、实际签名并通过验证的构建才标记为 `authenticode`；签名不保证消除 SmartScreen 或杀毒软件提示。

SignPath Foundation 证书的发布者是 SignPath Foundation，不是随意指定的个人或项目名。当前接入状态与待办见 [签名政策](code-signing.md)。
