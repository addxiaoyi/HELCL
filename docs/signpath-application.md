# SignPath Foundation 申请草稿（未提交）

项目：HelL Minecraft Launcher。Windows 完整版使用 WPF/.NET Framework 4.8；Avalonia/.NET 10 跨平台预览版正在迁移。核心用途为管理 Minecraft 实例、下载游戏与资源并调用 Java 启动游戏。

已核实的项目来源：https://github.com/addxiaoyi/HELCL 。GitHub 维护者账号为 `addxiaoyi`（当前 ADMIN 权限）；插件市场 https://github.com/addxiaoyi/HELL 为独立仓库。上游 https://github.com/wfwwq/TipCL 使用 MIT，版权属于 `2026 wfwwq`，原文已保留在 `docs/licenses/TipCL-MIT.txt`。详细证据见 [license-provenance.md](license-provenance.md)。

提交前由项目所有者补充并确认：

- 正式发布仓库、维护者法定身份与联系地址。源码归属 HELCL 已核实，签名申请不能只用 GitHub 昵称代替需要的真实身份。
- HelL 新增代码的 OSI 许可证及完整版权/来源链。上游 MIT 已核实；当前 HelL 根 LICENSE 仍缺失，历史反编译来源、图标和全部第三方组件归属仍需完整登记。
- 可审查的公开源码、对应 release 历史、构建说明和二进制来源证明。
- 谁可以提交签名，谁可以审批，每位责任人启用 MFA，逐次人工批准如何落实。
- 预览版采用 Microsoft 设备码登录，密码在系统浏览器输入；令牌用 AES-GCM 加密，随机密钥在 Windows 凭据管理器、macOS 登录钥匙串或 Linux Secret Service 中保存。系统凭据库不可用时明确提示仅保留会话。AI 和插件界面仍未迁移，不应声称整个核心不包含这些功能。
- 对此前安全软件检出提供具体文件、SHA-256、构建链和调查结果；尚不能定性为误报。

拟签名范围：最终 WPF `HelL.exe`；Avalonia 便携版的 `HelL.Desktop.exe`、`HelL.Desktop.dll` 和 `HelL.Core.dll`。不签用户模组、下载后的 Java/Minecraft 或无关第三方程序。

构建来源：GitHub Actions 检出匹配版本的受保护标签，在托管 runner 上构建测试，上传 artifact 并以 artifact ID 请求签名；签名结束后验证证书 Subject、Authenticode 状态和可信时间戳，再生成最终校验与更新清单。审批/证书条件尚待申请通过后配置。

本文件只是本地申请材料，不代表符合资格、不代表基金会背书，也没有代替维护者发送邮件、填写表单或接受服务条款。
