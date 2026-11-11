# 跨平台工作区与签名准备：preview.3

2026-11-11。源码提交 `6e784a7c68f7a69ee5ad966042cc5901bf597f77`，分支 `codex/portable-native-20261111`。
[Actions 42973528612](https://github.com/addxiaoyi/HELCL/actions/runs/42973528612) 五个平台全部成功。本轮未使用子代理，没有创建公开 Release、PR、合并 main 或改写原 WPF 工作区。

## 许可证与签名

维护者明确选择 HELCL 新增代码使用 MIT、版权署名 addxiaoyi。根 LICENSE 保留上游 wfwwq 版权并加入原创贡献声明。包内附带 LICENSE、THIRD_PARTY_NOTICES.md 和未改动的 TipCL MIT 文本。

再次读取 SignPath 官方 [terms](https://signpath.org/terms)，fork 条件仍要求 “the upstream project publishes signed builds”。上游最新 v1.1.0 的 GitHub 资产摘要仍为此前验签 `NotSigned` 的同一 EXE。因此许可证缺口已处理，但当前未提交 SignPath 申请；历史代码/资产来源、完整原生依赖声明和审批人/MFA 等事项仍需补齐。

真实 Mac 不可用，按维护者选择交付 [首次打开验收步骤](macos-desktop-acceptance.md) 与 `tools/verify_macos_download.py`。脚本在两个 macOS runner 中执行通过，保存深度验签、隔离标记、Gatekeeper、app host 哈希及数据目录信息；不删除隔离属性、不修改 Gatekeeper，报告明确 `manualFirstOpenVerified=false`。runner 没有浏览器下载隔离标记，不能据此声明真实首次打开通过。

## 已迁移界面

- 实例库：名称/分组搜索、固定筛选、分组筛选、封面和紧凑列表双视图、视图偏好持久化、改名、分组、固定、打开目录、自选封面与恢复截图。没有图片时显示明确占位；不使用无关图片。
- 窄窗口将编辑字段收进“管理”入口，为实例标题和封面保留空间。左侧持续显示当前实例，进入实例后回到启动页。
- 资源页：Modrinth/CurseForge 两个来源与六类资源、中文别名搜索、真实项目图标与作者、中文摘要/原文切换、兼容文件选择、进度/取消、失败重试和目标实例提示。详情页收起搜索栏，操作区保持可见。
- 下载边界：重新查询所选项目文件，检查版本兼容性、扩展名、文件名、链接目录与上游校验值；同名不同内容文件拒绝覆盖。资源包/光影落入所选版本目录，世界使用现有安全导入流程，数据包直接写入用户选择的已有存档。
- 分阶段限制：模组文件下载不会自动补齐依赖；界面明确列出必需依赖数量。整合包目前只下载原始文件，加载器安装与启动、AI、插件和完整存档管理尚未迁移。英文简介无中文缓存时显示原文，不宣称全文已汉化。

## 验证证据

| 验证 | 结果 |
| --- | --- |
| 本机 .NET 回归 | 57 通过；包括搜索→版本→下载、重复下载、取消、错误文件、损坏包、同名文件保护、存档数据包定位、分组/固定/改名恢复 |
| 打包回归 | 9 通过，检查许可证入包、清单校验和可执行权限 |
| GUI smoke | 800/1060 DIP 启动页、实例列表、封面、空状态、资源列表和详情；点击事件验证改名、固定、筛选和原文切换 |
| 五平台 CI | win-x64、linux-x64、linux-arm64、osx-x64、osx-arm64 原生 runner；57 项 .NET、9 项打包、解包后启动/重启恢复、GUI 与凭据往返全部通过 |
| ARM64 架构 | `native-platform.json`：process=arm64、system=arm64、runtime=linux-arm64；不是 x64 上交叉构建的运行结果 |
| 实时资源下载 | 中文“忠实”搜索到 `faithful-64x`，文件版本 `BauEG3pq`，下载 `Faithful 64x - Release 6.zip`，16,767,880 字节；界面确认真实图标已渲染，安装文件独立 SHA-512 匹配 |

实时资源包 SHA-512：`049569de400479d33bbc99acb79084b937d37da3e7a16edd8e248b135344eab48c1735bf664d697ce5f2ee15ea0b07f1dead634395a82862d9ab3b2bb8264589`。

CI 中 `native-evidence-*` 是小型验收附件，含架构、界面与凭据 JSON、截图、版本清单/校验和、macOS 签名报告；`native-acceptance-*` 包含完整便携包。附件会按 GitHub 保留期过期。

本机证据目录：`D:/UserCache/Temp/helcl-preview3-42973528612`、`D:/UserCache/Temp/helcl-workspace-ui-final-20261111`、`D:/UserCache/Temp/helcl-catalog-ui-20261111-02`。资源界面实测使用隔离元数据夹具定位安装目录，没有运行 Minecraft 或验证游戏内资源包启用；没有实测真实微软账号。CurseForge 在线完整流程尚未单独验收。未宣称鼠标/读屏或全部高 DPI 环境均通过。

构建仍含原有核心代码的过时网络 API、Windows API 可达性和两个旧测试空值警告，新迁移页面编译无新增警告。原目录 `D:/qwq/qdq` 的未提交工作未被混入本分支。
