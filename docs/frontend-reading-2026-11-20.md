# 安装、对话与日志阅读精修

本轮基于已验提交 `5f9af75f55f35086e02b46be5f772547d6428d91`，继续完善安装流程、AI 消息和任务日志的排版。保留既有品牌、配色、66 种矢量图标及上一轮首页和资源行布局。

## 改进范围

- WPF 安装页明确版本选择和目标确认的阅读顺序，调整版本元信息、摘要和主操作的层级；任务状态与日志使用更清楚的字号和间距。
- Avalonia Desktop 的 AI 正文、代码块与复制操作重新组织，减少重复卡面；任务页减少空白列表占用，将阅读空间留给详情与长日志。
- Mobile 对话区强化正文与角色标签的关系，代码块和输入区使用一致的阅读节奏；任务中心区分任务标题、时间、状态与说明，大字体仍保留完整触控区域。

代码块的文字、空行、缩进与复制内容保留，字体回退以实际字形和渲染核验。没有引入新的 Markdown 引擎或动画；后续缺陷修复也覆盖安装确认、取消、任务重试与资源查询时序。

实现和同数据前后对照见 [WPF](frontend-reading-wpf.md)、[桌面](frontend-reading-desktop.md)、[手机](frontend-reading-mobile.md)。截图使用隔离的安装清单、对话和错误日志，不把受控数据当作真实安装、模型响应或生产任务。

## 验收边界

新增检查接入原有 WPF 复核、桌面完整 UI 和手机 smoke，针对实际文字、渲染、按钮边界、长内容及缩放验证；保留此前草稿、焦点、确认、分页、图标、主题后备和 200% 字号检查。Android 普通和 200% 会话必须产生新的阅读检查回执。

完整构建、服务测试、同数据截图和最终 GitHub 回执分别保存在 `TestResults/frontend-reading-20261120/`。以最终整合提交为准，不通过修改无关 Core、警告策略或削弱既有断言获得通过。物理触控、TalkBack/桌面读屏、原生高对比与显示缩放、所有系统磨砂效果和厂商设备仍待专项实测。未合并、正式发布或部署。

## 后续缺陷修复（2026-11-23）

- Desktop 资源空页保留末个非空页并说明已到末页；编辑搜索词标明旧结果与页码，偏移上限有明确终止提示。任务重试保留原分页、项目和加载器条件，历史任务 ID 稳定，损坏实例元数据的刷新如实失败且可重试。
- 安装入口共用目标、Minecraft 版本及加载器构建确认，准备计划、确认与安装持续持有任务门控。改变选择后拒绝旧计划；取消或关闭窗口及时结束等待并关闭确认对话框，不开始安装。安装页保留独立目标，繁忙或隐藏时延后加载器查询，返回后查询最新选择。
- Mobile 清理任务确认具体数量，仅删除确认时捕获的已结束记录；Mobile 和 Desktop 代码复制反馈更新可访问名称。WPF 日志与三端大字号阅读回归继续保留；读屏实际播报仍需 TalkBack/桌面读屏验收。
- 本次发现资源自动搜索会从隐藏页面占用全局任务门控，并在连续筛选时重复排队、忙碌时丢失最新条件。现在合并待查请求，执行前复查页面/窗口可见性、忙碌和关闭状态；返回资源页或任务结束后执行最新请求。编辑搜索词取消自动排队，显式搜索消费待查请求。

新增 `MainWindow.ResourceRefreshCheck.cs` 接入完整 Desktop UI smoke，覆盖隐藏页实例刷新、连续筛选、忙碌恢复、旧响应丢弃、搜索词草稿、显式搜索去重、离页再入与关闭窗口。`TestResults/resource-refresh-red-20261123b/desktop-resource-refresh.json` 在修复前失败；`TestResults/resource-refresh-green-20261123/` 的新回归、安装确认/取消和完整 UI smoke 均通过。Desktop 服务测试为 29 通过、1 个 Unix 权限检查在 Windows 跳过，回执位于 `TestResults/resource-refresh-tests-20261123/desktop.trx`。

独立复核补出首次请求的边界：首次失败或尚未成功返回时，筛选变化也需补查。现在单独记录“已发起查询”，保留“已有显示结果”用于旧页提示；未搜索过的页面仍保持空闲，失败后不自动循环重试。固定回归在 `TestResults/resource-initial-red-20261123/ui-smoke-error.txt` 先失败，最终 `TestResults/resource-initial-green-20261123b/` 的首次请求、完整资源刷新与全量 Desktop smoke 回执全部通过。

同轮 Mobile 服务测试 57/57 通过，320×720、390×844、844×390 桌面宿主预览的 `mobile-smoke.json` 和 `mobile-reading.json` 均为 `ok: true`，回执位于 `TestResults/resource-refresh-mobile-<尺寸>-20261123/`。这些本地预览不代表原生 Android 或读屏验收。

此前检查点 `40d7a2e` 的 [前端验收](https://github.com/addxiaoyi/HELCL/actions/runs/44986791930) 与 [标准 CI](https://github.com/addxiaoyi/HELCL/actions/runs/44986791941) 已全部通过，包括 Android 模拟器键盘、返回键、边距和系统字号检查。最新提交的 CI 以 [PR #11](https://github.com/addxiaoyi/HELCL/pull/11) 当前 HEAD 为准。受控资源 fixture 不证明 Modrinth/CurseForge 实际末页；原生读屏和真机仍未验收。所有运行产物留在未跟踪的 `TestResults/`，不提交到仓库。
