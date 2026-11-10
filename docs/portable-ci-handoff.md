# Portable native acceptance branch

此分支用于 `3.2.0-preview.2` 的原生平台验收，不替换仓库 main 上的完整启动器，不触发公开 release。源码来自本地 HelL 工作区，按跨平台项目依赖清单复制：核心源码、Avalonia 界面、测试、打包器、品牌/本地化资源和 MCP 内嵌静态资源。未包含个人配置、账号、实例、日志或构建缓存。

本地 Windows 已完成 40 项 .NET 测试、9 项打包测试、图形界面自检、中文实例创建，以及系统凭据库加密往返。远程结果以本次 Actions run 为准。

工作流 `portable-acceptance.yml` 在 `codex/portable-native-*` 分支推送后运行：Windows x64、macOS Intel、macOS Apple Silicon。macOS 包经过嵌套 Mach-O 与 `.app` ad-hoc 签名，解压最终 ZIP 后再次验签，再测试重启、界面与一次性钥匙串。`spctl` 的 Gatekeeper 结果保存为证据；ad-hoc 不是 Developer ID 或公证，不能将程序能运行解释为 Gatekeeper 已放行。

账号使用设备码授权和官方 Minecraft 身份链；令牌加密保存在 `portable-account.json`，AES-GCM 密钥位于当前用户的系统凭据库。密钥库不可用时明确提示仅在内存保留，换电脑后需重新登录。测试均使用虚构凭据，不读日常账户。

许可证来源见 `docs/license-provenance.md`，上游 MIT 声明附在 `docs/licenses/TipCL-MIT.txt`。HelL 原创部分根许可证、SignPath 申请和历史杀软检测调查尚未完成。工作流不提交 SignPath 请求，不上传正式发行版，不改动 HELL 插件市场。
