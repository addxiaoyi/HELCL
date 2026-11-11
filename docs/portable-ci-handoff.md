# Portable native acceptance branch

## 2026-11-11 验收结果

分支：`codex/portable-native-20261111`。已验证源码提交：`bb254bae027288f0cd3bf711b9928fd746c627d3`。
[Actions run 42965979111](https://github.com/addxiaoyi/HELCL/actions/runs/42965979111) 的四个 job 全部成功；仅生成构建附件，未创建 Release、PR 或合并 main。

| 平台 | 构建附件 | 签名 | 原生验收 |
| --- | --- | --- | --- |
| Windows x64 | `native-acceptance-win-x64` | unsigned | 40 项 .NET 测试、9 项打包测试、解包后两次启动/实例恢复、界面及系统凭据往返通过 |
| Ubuntu 24.04 x64 | `native-acceptance-linux-x64` | unsigned | 同上；界面在 Xvfb 下运行，Secret Service 使用临时 D-Bus/钥匙环 |
| macOS Intel | `native-acceptance-osx-x64` | ad-hoc | 同上；最终 ZIP 解包后深度验签通过，使用临时钥匙串 |
| macOS Apple Silicon | `native-acceptance-osx-arm64` | ad-hoc | 同上；最终 ZIP 解包后深度验签通过，使用临时钥匙串 |

每个平台附件包含便携包、版本/架构/签名状态清单、`SHA256SUMS`、1060/800 DIP 界面截图、界面自检与凭据自检报告。macOS 另包含 `codesign.txt`、`gatekeeper.txt`。GitHub 构建附件会按仓库保留策略过期，不是永久下载地址。

四个附件已从上述 run 下载并在本机重新执行 `tools/verify_portable.py`：SHA-256、文件大小、版本与架构清单全部一致；压缩包内容检查、四组界面/凭据报告和八张截图复核通过。Linux 包保留可执行权限。取回的 Windows 包解压后再次执行 `tools/smoke_portable.py`，返回 `ok=true, launches=2, persistedInstances=2`；`Get-AuthenticodeSignature` 返回 `NotSigned`。

最终便携包 SHA-256（均为 `3.2.0-preview.2`）：

| 平台 | SHA-256 |
| --- | --- |
| win-x64 | `38acf28a332328c3359df90125e08ed2b692ce19b31374571dbb56bc1c334c69` |
| linux-x64 | `40b136fb5ad7f524963cc7989a258349341c97116bd8e026181596b0930e7cd1` |
| osx-x64 | `19ba7f19f782c3362a139a586eca7bc4343d04528af9b0d0b9d7a5d7a2c6c12b` |
| osx-arm64 | `8e357e3666173813d92162c1124f314f61467bea6818001dfa668e87dc7766b5` |

截图检查发现 Linux runner 原先缺少中文字体，界面虽然能启动，中文却显示为方框。工作流现安装 `fonts-noto-cjk`，并检查 fontconfig 可找到 Noto CJK；新版 Linux 截图已确认中文正常。程序仍依赖系统字体，Linux 用户需按 [下载说明](downloads.md) 安装中文字体。

Windows 的一次复验在认证测试清理阶段报 `Replay server did not stop`。模拟 HTTP 服务现使用可取消的异步接收，保留 5 秒退出断言，并传播非取消异常。本机完整测试 40/40 通过，原失败的刷新场景连续 5/5 通过，随后四个平台 CI 均通过。

### 验收边界

- 这是 Avalonia 迁移预览版，不代表 Windows WPF 完整功能已移植。模组/资源管理、AI、插件市场和存档界面仍待迁移。
- 微软登录、刷新、失效和凭据库测试使用虚构凭据；没有实测用户的正版账号授权和令牌刷新。此前 Windows 隔离 Minecraft 1.21.1/Java 21 运行记录仅证明 Demo 启动，不等于正版实测。
- macOS 的 ad-hoc 签名有效，但 `spctl` 仍返回 `rejected`；没有 Developer ID 或公证，也没有完成真实桌面的首次下载隔离与“仍要打开”交互验收。
- Linux ARM64 尚未进行原生启动验收；本轮也不声明 Windows ARM64 支持。
- .NET 构建仍有上游遗留的 Windows 专用 API 与过时网络 API 警告；本轮结果不是零警告承诺。
- SignPath 材料已准备，尚未提交。本次后续工作中维护者已确认原创代码使用 MIT 并署名 `addxiaoyi`，根许可证已加入分支；当前公开 fork 签名条件仍要求上游发布已签名构建，已核实的 TipCL v1.1.0 EXE 为 `NotSigned`。详见 [申请材料](signpath-application.md) 和 [许可证来源](license-provenance.md)。

## 分支范围与实现

此分支用于 `3.2.0-preview.2` 的原生平台验收，不替换仓库 main 上的完整启动器，不触发公开 release。源码来自本地 HelL 工作区，按跨平台项目依赖清单复制：核心源码、Avalonia 界面、测试、打包器、品牌/本地化资源和 MCP 内嵌静态资源。未包含个人配置、账号、实例、日志或构建缓存。

本地 Windows 已完成 40 项 .NET 测试、9 项打包测试、图形界面自检、中文实例创建，以及系统凭据库加密往返。远程结果以本次 Actions run 为准。

工作流 `portable-acceptance.yml` 在 `codex/portable-native-*` 分支推送后运行：Windows x64、Linux x64、macOS Intel、macOS Apple Silicon。Linux GUI 在 Xvfb 下检查，Secret Service 在一次性 D-Bus 与 GNOME keyring 会话中读写并删除测试密钥。macOS 包经过嵌套 Mach-O 与 `.app` ad-hoc 签名，解压最终 ZIP 后再次验签，再测试重启、界面与一次性钥匙串。`spctl` 的 Gatekeeper 结果保存为证据；ad-hoc 不是 Developer ID 或公证，不能将程序能运行解释为 Gatekeeper 已放行。

账号使用设备码授权和官方 Minecraft 身份链；令牌加密保存在 `portable-account.json`，AES-GCM 密钥位于当前用户的系统凭据库。密钥库不可用时明确提示仅在内存保留，换电脑后需重新登录。测试均使用虚构凭据，不读日常账户。

许可证来源见 `docs/license-provenance.md`，上游 MIT 声明附在 `docs/licenses/TipCL-MIT.txt`，原创贡献采用维护者确认的根 MIT 许可证。TipCL v1.1.0 发布的 EXE 尚未签名，SignPath Foundation 的 fork 条件仍不满足。工作流不提交 SignPath 请求、不上传正式发行版，也不改动 HELL 插件市场。
