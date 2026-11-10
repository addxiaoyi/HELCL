# 应用签名与发布政策

状态：签名与三平台便携包的本地代码/流水线已接入；**没有取得 SignPath Foundation 批准，没有正式证书签名，也没有执行公开发布**。已核实 HELCL 为启动器源码、HELL 为插件市场，见 [来源核实](license-provenance.md)。本地仍无 Git remote；流水线使用 `github.repository`，本地脚本要求显式填写仓库。

## Windows / SignPath Foundation

[SignPath Foundation](https://signpath.org/) 为符合其[条款](https://signpath.org/terms)的开源项目提供免费代码签名。不是开源就自动获批；证书主体是 SignPath Foundation，不是项目任意指定的发布者。Authenticode 提供发布者和完整性验证，但不保证移除 SmartScreen、杀毒软件或企业策略提示。

上游 wfwwq/TipCL 的 MIT 已通过 API 核实并保留原文，但 HelL 新增代码尚无已确认的根 LICENSE。仍须补齐原创授权、历史来源与全部再分发权利，不能代填许可证或声称已符合基金会资格。之前的安全软件检出也需要独立调查；签名不是误报证明。申请草稿见 [signpath-application.md](signpath-application.md)。

通过基金会审批后，在 GitHub 的 `release-signing` environment 中设置：

- Secret `SIGNPATH_API_TOKEN`：基金会批准的签名 API token；不要写到源码或聊天。
- Variables `SIGNPATH_ORGANIZATION_ID`、`SIGNPATH_PROJECT_SLUG`、`SIGNPATH_SIGNING_POLICY_SLUG`。
- `SIGNPATH_WPF_ARTIFACT_CONFIGURATION_SLUG`：仅签归档根目录的最终 `HelL.exe`（在 Fody/Costura 完成后）。
- `SIGNPATH_PORTABLE_ARTIFACT_CONFIGURATION_SLUG`：保留上传目录结构，签根目录 `HelL.Desktop.exe`、`HelL.Desktop.dll`、`HelL.Core.dll`；不重签 Microsoft/Avalonia 等第三方二进制。
- `SIGNPATH_CERTIFICATE_SUBJECT`：批准后证书的完整 Subject，精确匹配，不能填通配符。验证还要求有效 Authenticode 与时间戳。

Artifact configuration 由 SignPath 项目后台按官方格式配置并审查；不使用未经验证的 XML 模板。上传的是 GitHub Actions 构建 artifact ID，保留来源记录；签名策略应绑定批准的仓库/工作流和受保护标签。按基金会要求配置 MFA、提交者/审核者职责和逐次人工批准。GitHub environment 也应限制发布标签及审核人，避免未审查分支取得签名秘密。

两个发布工作流均为手动触发：

1. `Signed Windows release`：构建 WPF、测试 → 上传待签构建 → SignPath 人工批准 → 下载签名结果 → 验证签名、证书主体、时间戳 → 生成 EXE SHA-256 与 `update.json` → 再核对清单。
2. `Portable preview`：五个原生 runner 分别构建 win-x64、linux-x64/arm64、osx-x64/arm64。Windows 可明确选择未签名预览或 SignPath；选择 SignPath 后缺配置、超时或验证失败均停止，不回退成未签名包。macOS 必须在 macOS 上 ad-hoc 签名。

默认仅上传 Actions artifacts。`publish=true` 只允许向**已存在的草稿 release**添加附件，不自动公开发布，不覆盖同名附件。旧的 `release: published` 触发已移除，避免先公开再补签名。发布仓库/标签须由维护者确认。生产 WPF 更新源目前仍需核对，便携预览版禁用自动更新，不会投送到 WPF 更新通道。

## macOS / ad-hoc

打包器创建标准 `.app`、Retina 标识和品牌 `.icns`；逐个签嵌套 Mach-O，最后签应用 bundle，执行 `codesign --verify --deep --strict`。采用 `codesign --sign - --timestamp=none`，不伪造 Developer ID 或公证状态。不对 ad-hoc 包声称 Gatekeeper 放行。

数据始终位于 bundle 外。打包后不得再修改 `.app` 内的配置或二进制。采用 .NET JIT 默认签名策略，未开启 hardened runtime；以后接入 Developer ID/notarization 时需另行验证 JIT entitlement、证书和公证闭环。本轮不加入移除 quarantine、关闭 Gatekeeper 等操作。

## Linux / 校验

提供自包含 tar.gz 和 SHA-256 清单，保留启动程序执行权限。Linux 不伪装成 Authenticode/ad-hoc 签名，清单记录 `unsigned`。如需发布者签名，可后续接入独立的 GPG/minisign 信任流程；单独摘要只校验字节一致性。

## 本地构建与校验

需要 .NET 10 SDK、PowerShell 7、Python 3.11+。例如：

```powershell
./tools/publish_portable.ps1 -Runtime win-x64 -OutputDirectory publish/win-x64
python tools/package_portable.py --source publish/win-x64 --output publish/portable-win --runtime win-x64 --version 3.2.0-preview.2 --signature unsigned
python tools/verify_portable.py publish/portable-win
dotnet test tests/HelL.Portable.Tests -c Release
python -m unittest discover -s tests -p test_package_portable.py -v
```

版本来自项目 `Version` 和发布目录 `portable-build.json`，打包不匹配就拒绝。输出目录已存在时不会覆盖；检测到 data/config/accounts/logs 或符号链接时拒绝带入发布包。所有摘要在签名、归档完成后计算。

Windows 正式签名包增加 `--signature authenticode --expected-subject '批准后证书的完整 Subject'`；macOS 使用本机 RID 和 `--signature adhoc`。不要把不同平台的编译成功视为真实游戏、系统安全提示或字体渲染已验收。

参考：[SignPath GitHub Action v3.0](https://github.com/SignPath/github-action-submit-signing-request/tree/f6d04783b4569d051e0c80105fe66e82819d0092)、[Avalonia macOS 部署](https://docs.avaloniaui.net/docs/deployment/macos)、[Apple 首次打开说明](https://support.apple.com/102445)。
