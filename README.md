<div align="center">


<img src="docs/icon.png" width="140" alt="TipCL" />

# TipCL

**AI 原生的 Minecraft 启动器**

一句话装模组、诊断崩溃卡顿、备份存档、开房联机——说出来就行

<sub>An AI-native Minecraft launcher for Windows</sub>

<br/>

<img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows" />
<img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET Framework 4.8" />
<img src="https://img.shields.io/badge/UI-WPF-6CB33E?style=flat-square" alt="WPF" />
<img src="https://img.shields.io/badge/MCP-Server-000000?style=flat-square" alt="MCP Server" />
<img src="https://img.shields.io/badge/%E8%AF%AD%E8%A8%80-%E4%B8%AD%E6%96%87%20%7C%20English-F4A93B?style=flat-square" alt="中文 | English" />
<a href="https://github.com/wfwwq/TipCL/actions/workflows/ci.yml"><img src="https://github.com/wfwwq/TipCL/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>

[AI 助手](#-ai-助手) · [功能](#-功能) · [下载与构建](#-下载与构建) · [MCP Server](#-mcp-server) · [目录结构](#-目录结构) · [开发者选项](#-开发者选项)

</div>

---

## 🤖 AI 助手

TipCL 的 AI 不只是聊天框，它能直接操作启动器：查实例、装东西、改设置、读日志。涉及写入的操作会先征得你同意。

| 你可以这样说 | 它会做什么 |
|---|---|
| “给 1.20.1 Fabric 装上钠和小地图” | 搜索 Modrinth / CurseForge，匹配版本和前置，一起装好 |
| “游戏闪退了” | 读崩溃报告和日志，定位冲突模组，给出一键修复 |
| “好卡” | 看硬件、内存和模组数量，建议优化模组，自动调内存和 GC |
| “备份一下我的生存存档” | 打包备份，之后可以随时回滚（回滚前会先备份当前状态） |
| “开个房和朋友联机” | 启动内置 Terracotta，把房间号发给你 |
| “把这张图设成皮肤” | 附上图片即可，支持离线账号 |
| 截图报错界面 | 模型支持视觉时直接看图分析 |

- **看图**：自动判断当前模型是否支持多模态，支持选择、粘贴、拖入图片，也可以在设置里手动指定。
- **长期记忆**：跨对话记住你常玩的版本、硬件和偏好，记录保存在本地 `ai_memory.md`，可随时查看和删除。
- **按需加载工具**：只在话题相关时才加载对应工具，省 token，也更准。
- **对外开放**：可作为 MCP Server，让 Claude、Cursor 等外部 AI 调用启动器能力。

## ✨ 功能

| | |
|---|---|
| 🎮 **版本安装** | 原版、Forge、NeoForge、Fabric、Quilt、OptiFine，自动选择镜像源 |
| 🧩 **模组与整合包** | Modrinth / CurseForge 搜索安装，mrpack 与 CurseForge 包导入导出 |
| 🗂️ **实例与存档** | 多实例隔离、存档管理与备份回滚、Java 自动检测 |
| 👤 **账号** | 微软正版、外置登录（authlib-injector）、离线皮肤 |
| 🌐 **联机** | 内置 Terracotta 局域网联机 |
| 📱 **手机同步** | 同一 Wi-Fi 或 USB 数据线和手机启动器互传存档，配对码连接，谁改了传谁，覆盖前自动备份 |
| 🤖 **AI 助手** | 崩溃诊断、卡顿优化、看图、长期记忆、MCP Server，见[上文](#-ai-助手) |
| 🏠 **仪表盘首页** | 启动配置、实时日志、快捷入口、游戏时长、新闻，布局随心拖拽 |
| 🎨 **外观** | 浅色 / 深色主题，中英双语界面 |

## 📦 下载与构建

**直接使用**：在 [Releases](https://github.com/wfwwq/TipCL/releases) 下载 `TipCL.exe`，双击运行。只需要系统自带的 .NET Framework 4.8，不用装任何运行时。AI 助手开箱即用，也可以在设置里换成自己的模型和 Key。

**自己编译**：需要 .NET SDK 8 或更高版本。

```bat
git clone https://github.com/wfwwq/TipCL.git
cd TipCL
dotnet build src/TipCL/TipCL.csproj -c Release

# 运行单元测试（Windows / Linux 都可以）
dotnet test tests/TipCL.Tests
```

产物在 `src/TipCL/bin/Release/net48/TipCL.exe`，依赖已通过 Costura 打进单个 exe。每次 push 和 PR 都会由 CI 自动构建并跑测试。

## 📁 目录结构

```
src/TipCL/
├─ App.xaml / MainWindow.xaml   应用入口与主窗口
├─ Themes/                      浅色、深色调色板与控件样式
├─ Pages/                       各功能页面（含 AI 页）
├─ Services/                    动画、对话框、壁纸、输入法守卫等界面服务
├─ Models/                      界面数据模型
├─ Core/                        启动器内核：下载、安装、启动、账号、模组、联机
│  └─ Launcher/Ai/              AI 工具、记忆、看图、MCP Server
└─ Resources/                   内置词表与 AI 模型规则
tests/TipCL.Tests/              单元测试（xUnit）
```

## 🔌 MCP Server

TipCL 可以作为本机 MCP Server，让 Claude、Cursor 等外部 AI 直接调用启动器的工具：查实例、装版本 / 模组 / 整合包 / 光影、启动游戏、诊断崩溃和卡顿、备份存档。

在 **设置 → AI 助手 → MCP Server** 打开开关即可，地址：`http://127.0.0.1:25585/mcp`（只监听本机）。

Claude Code：

```bat
claude mcp add --transport http tipcl http://127.0.0.1:25585/mcp
```

Cursor（`~/.cursor/mcp.json`）：

```json
{ "mcpServers": { "tipcl": { "url": "http://127.0.0.1:25585/mcp" } } }
```

Claude Desktop 等只支持 stdio 的客户端可以通过 `npx mcp-remote http://127.0.0.1:25585/mcp` 接入。端口可以在数据目录的 `mcp_server.json` 里改。

## 📱 手机同步

先在手机启动器里打开 **存档管理 → 从 zip 导入 → 启动同步服务**，屏幕上会显示地址和 6 位配对码。然后在 TipCL 的 **设置 → 手机同步** 里填上地址（也可以点「扫描局域网」）和配对码，连接后打开同步面板，就能对比两边的世界、逐个互传或一键同步。也可以直接对 AI 说「把存档同步到手机」。

- 插数据线也行：手机打开 USB 调试，把 Android platform-tools 里的 `adb.exe` 放到 TipCL 同目录，点「USB 自动连接」
- 两端都改过同一个世界时不会自动覆盖，由玩家选择保留哪一份
- 被覆盖的一边一定会先备份到电脑的备份目录，可以随时回滚
- 两边游戏版本要一致，旧版本打开新存档会损坏

## 🛠️ 开发者选项

| 启动参数 | 作用 |
|---|---|
| `--nav-dump` | 打印侧栏编排结果 |
| `--i18n-check` | 扫描未本地化的中文字面量 |
| `--smoke` | 冒烟模式，跳过需要手动点击的弹窗 |
| `--lang xx` | 指定界面语言 |

环境变量：`TIPCL_HOME`（数据目录）、`TIPCL_LANG`、`TIPCL_WPF_DEBUG`。

## 🔄 更新机制

启动器默认读取本仓库的 [`update.json`](update.json) 检查更新。发布新版本时修改其中的 `version`、`url`（下载地址）和 `sha256` 即可。

## 💬 社区建议

来自 [LINUX DO](https://linux.do) 等社区的每一条建议都会在这里登记，并标出处理状态。

| 建议 | 来源 | 状态 | 说明 |
|---|---|---|---|
| （等待第一条建议） | | | |

状态说明：✅ 已完成 · 🚧 开发中 · 📋 已采纳待排期 · ⏸️ 暂不考虑（附原因）

---

<div align="center"><sub>Made with ❤️ for Minecraft players · 感谢 <a href="https://linux.do">LINUX DO</a> 社区的支持</sub></div>

## 🙏 致谢

- QQ `368****168`：帮忙测试了正版（微软）验证

## 前端百项复核（2026-11-19）

本次增量修复真实实例虚拟化、原子设置保存与三端异步状态边界，验收与未验证设备条件见 [复核交付说明](docs/frontend-recheck-2026-11-19.md) 和 [100 项状态](docs/frontend-completion-status.json)。基于 Draft PR #6，不包含合并、发布或部署。

## 前端交互细节（2026-11-20）

继续完善登录输入校验、错误与焦点恢复、AI 草稿及日志发送确认、配置保存反馈和资源列表阅读位置。具体修补与验收边界见 [交互细节记录](docs/frontend-details-2026-11-20.md)，基于已验提交 `1fbdeae`；真机触控与读屏仍需专项验收。

## 图标重绘与界面细节（2026-11-20）

三端使用共享的 66 种矢量图形，统一导航和常用操作，完善选中状态、图标辅助名称、窗口对齐与桌面中文字库。实现、图集和验收范围见 [图标优化记录](docs/frontend-icons-2026-11-20.md)，基于已验提交 `1d147f0`。

## 审美细节精修（2026-11-20）

继续收敛表面层次、标题与辅助文字、实例检视器和资源行布局，保留玻璃偏好与既有交互。相同数据的明暗和窄窗对照、字号及验收边界见 [审美精修记录](docs/frontend-refine-2026-11-20.md)，基于已验提交 `e8403e1`。

## 安装与阅读体验（2026-11-20）

继续精修安装流程、AI 对话与任务日志的文字层级、内容间距、代码呈现和大字布局。实现与验收范围见 [阅读精修记录](docs/frontend-reading-2026-11-20.md)，基于已验提交 `5f9af75`。

2026-11-23 后续修复覆盖资源末页与重试绑定、安装目标确认与及时取消、Mobile 清理数量确认和代码复制反馈。资源自动搜索现在合并连续筛选，隐藏页面暂缓查询，返回后只查询最新条件；完整 Desktop 回归保留修复前失败和修复后通过回执。最新 CI 见 [草稿 PR #11](https://github.com/addxiaoyi/HELCL/pull/11)，真机读屏及真实资源源末页仍待专项验收。

同日后端补强覆盖 AI 配置与凭据轮换、实例选择和固定项的失败回滚、损坏元数据保护、封面恢复、加载器继承版本的资源目标，以及 CurseForge 密钥来源和下载取消收尾。修复范围、失败回归与验收边界见 [后端记录](docs/backend-hardening-2026-11-23.md)。
