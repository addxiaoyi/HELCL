# 前端 100 项完善交付（2026-11-19）

100 个审查编号均已落实实现或对应替代方案。实现完成与特定设备上的验收分开记录；并不声称每个 Android 屏幕、读屏软件或物理触控环境都已认证。

本轮保留既有 WPF / Avalonia Desktop / Mobile 风格与业务能力，新增安全确认、失败恢复、分页、上下文和数据保存。完整 GUI 基础与修补分层保存在 [Draft PR #5](https://github.com/addxiaoyi/HELCL/pull/5) 与 [Draft PR #6](https://github.com/addxiaoyi/HELCL/pull/6)，没有合并、发布或部署。

实现细节：

- [WPF 1–35 项](frontend-wpf-remediation.md)
- [手机 36–67 项](frontend-mobile-remediation.md)
- [桌面 68–99 项](frontend-desktop-remediation.md)
- 第100项：主要导航概念统一为启动、实例、资源、AI助手和设置；手机明确区分资源准备与未提供的本机游戏运行能力。

替代方案：实例卡片采用每批60项渐进加载，避免一次创建全库；不假称为真实卡片虚拟化。手机配对增加受控JSON文件导入，不引入扫描器依赖，指纹确认保持必需。

## 逐项状态

1. **已实现｜新输入的 AI / CurseForge API Key 明文显示。** Implemented: `SecretInput` defaults to a real `PasswordBox`, offers explicit reveal/hide, and hides again when unloaded. Existing stored-key hints remain available without returning the secret.

2. **已实现｜皮肤站登录关闭弹窗后才校验必填项。** Implemented: skin-site login uses `Dlg.EditAsync`; API/user/password checks and normalized-address errors stay in the open form with entered values retained.

3. **已实现｜统一通行证的服务器 ID 校验发生在弹窗关闭后。** Implemented: unified-pass login validates server ID/user/password in the open form before starting its task.

4. **已实现｜微软验证码未返回时复制与打开浏览器按钮已可点。** Implemented: device-code copy/browser controls start disabled, then become available only when the actual code/HTTPS address arrives.

5. **已实现｜设备登录缺少倒计时与就地重新取码。** Implemented: shared authentication events carry optional `expires_in`; WPF counts down the real TTL, disables expired actions and offers a fresh authorization. Missing TTL is explicitly unknown rather than fabricated.

6. **已实现｜账号 UUID 行缺少完整值复制入口。** Implemented: complete UUID is wrapped and can be copied from the account row.

7. **已实现｜账号列表缺少按名称或类型查找。** Implemented: account name/UUID text search and type filter.

8. **已实现｜长账号名与固定右侧操作在窄窗下可能拥挤。** Implemented: the account hero/list stack at narrow width; account title/actions use wrapping responsive rows. No claim of every display/scale combination.

9. **已实现｜收藏读取失败会回退为空收藏。** Implemented: favorites use acknowledged RPC; read failure is an explicit retryable error, not an empty collection.

10. **已实现｜模组启停失败后开关不回滚且缺行内错误。** Implemented: mod toggles await their commit, freeze duplicate input, roll back on failure, and report the row error; state text changes only after acknowledgement.

11. **已实现｜搜索开始用骨架屏替换全部旧结果。** Implemented: resource refresh retains visible rows with an updating/stale notice; a failed update keeps the previous result available.

12. **已实现｜资源搜索固定九秒超时，慢网络只能重试。** Implemented: a timed-out search offers an explicit 45-second retry/wait with the same criteria. Normal default remains nine seconds.

13. **已实现｜游戏版本下拉推荐列表写死。** Implemented: the version filter is refreshed from the manifest's release list, while preserving editable input and the existing fallback choices if unavailable.

14. **已实现｜打开资源页会自动读取剪贴板并使用识别链接。** Implemented: recognized clipboard links require an explicit use/ignore decision before changing the search input.

15. **已实现｜混合来源搜索缺少各来源状态。** Implemented: provider completion/failure metadata is preserved through `SearchCatalog` and displayed; the existing source selector supports retrying one provider. UI does not invent source status.

16. **已实现｜已安装资源删除没有回收或撤销窗口。** Implemented for all actual removable catalog files (mod/resourcepack/shader/datapack): move to the instance recycle store and offer undo; root's receipt/hash/no-overwrite checks protect restoration. The world catalog has no installed/delete method, so it is not a permanently deleting path; modpack label removal retains its separate semantics and keeps game data.

17. **已实现｜最近游玩排序只加载二十四个实例的时间。** Implemented: recent metadata request scales to the full instance list (shared backend cap raised to 10,000 by root).

18. **已实现｜排序和只看固定筛选的偏好不随其它库偏好保存。** Implemented: sort and pinned-only preference persist alongside group/view preferences, with old preference files remaining compatible.

19. **已实现替代方案｜实例卡片全部创建，缺少可见区域虚拟化。** Implemented with bounded progressive rendering: initially 60 instance cards, explicit loading of further pages. This avoids constructing every card on initial load; it is deliberately described as paging, not claimed to be recycling virtualization.

20. **已实现｜打开启动页会隐含将实例设为全局默认。** Implemented: opening the launch page selects its target without modifying the global default instance.

21. **已实现｜批量固定操作包含被筛选隐藏的已选实例。** Implemented: batch pin/unpin shows the exact target names/count and the number hidden from the current page before committing.

22. **已实现｜实例列表移除了默认焦点视觉，键盘定位需复核。** Implemented: default keyboard focus visual restored; existing arrow/Enter navigation remains intact. Screen-reader certification is not claimed.

23. **已实现｜版本列表双击会直接安装。** Implemented: double-click selects/focuses the install confirmation rather than submitting installation.

24. **已实现｜安装期间连版本搜索和浏览一起禁用。** Implemented: an active installation locks only its mutable target/configuration; version browsing, text search and category filtering remain available.

25. **已实现｜版本列表固定为二百九十像素高。** Implemented: version-list height follows available viewport height within bounded limits.

26. **已实现｜安装前没有下载量和磁盘占用摘要。** Implemented: explicit pre-install confirmation shows `InstallationPlan` destination, known client/library download estimate, available disk space and existing-file policy. Missing estimates are labeled unknown and require an explicit continue; known insufficient space refuses submission. Assets/Java/loader exclusions remain explicit.

27. **已实现｜加载器折叠项与禁用选项的兼容原因不够直观。** Implemented: loader options start expanded and disabled options explain their compatibility/loading/active-task reason.

28. **已实现｜保存设置可能先改目录、后续保存失败而部分生效。** Implemented as transparent staged behavior: all numeric input validates before any directory mutation; if directory change succeeds but main settings save fails, the error explicitly identifies partial application. This is not an atomic transaction claim.

29. **已实现｜非法分辨率或限速数字会静默回退默认值。** Implemented: resolution and download-rate values reject invalid/out-of-range input at the field; correction clears its error decoration.

30. **已实现｜AI 上下文非法或越界值会静默回退或截断。** Implemented: context-window input requires a value in the documented range, without silent clamping/defaulting.

31. **已实现｜设置混合即时、保存后和重启后生效规则。** Implemented: the visible footer distinguishes immediate preview/save/restart behavior and counts pending changes; leaving with pending edits warns that the draft remains unsaved.

32. **已实现｜大量设置项缺少文字搜索和定位。** Implemented: settings search returns category-linked text matches that switch the category and scroll to the matching field.

33. **已实现｜AI 对话搜索只查标题，不查正文。** Implemented: conversation search matches titles and message content.

34. **已实现｜AI 历史右键删除没有确认或撤销。** Implemented: chat deletion requires a specific confirmation and acknowledged RPC; delete errors are not silently converted to old state.

35. **已实现｜AI 历史搜索重建全部按钮，缺少虚拟化。** Implemented: AI history is an actual recycling virtual `ListBox`, with rows built by the existing virtual-row factory rather than thousands of eagerly constructed buttons.

36. **已实现｜导入整合包入口实际打开线上资源搜索。** Rename the misleading import entry to “查找整合包”; it continues to open online modpack search.

37. **已实现｜通知铃铛只写状态，没有通知或任务中心。** Bell opens a local task centre, showing current and up to 100 recent operations, target, progress and outcome; active operations can be cancelled.

38. **已实现｜搜索与筛选图标执行完全相同动作。** Search icon always reveals and focuses keyword input; filter icon independently expands/collapses filters.

39. **已实现｜资源兼容目标藏在默认收起的筛选区。** Compatibility target is outside the collapsed filter area and remains visible above the list.

40. **已实现｜新搜索先清空旧列表，失败后无法继续查看旧结果。** New searches retain the last successful list; failure/cancellation shows a retry message without erasing it. Filter changes mark results stale and prevent wrong-target selection.

41. **已实现｜资源搜索最多三十条，没有下一页入口。** “加载更多兼容资源” uses upstream offset/pageSize; offsets advance by requested page size and distinct source/project IDs append. Nonempty results allow trying another batch; only an empty upstream page marks the end, avoiding alias/dedup truncation.

42. **已实现｜资源详情返回后不恢复列表滚动位置。** Resource-list offset is saved before entering details and restored on return.

43. **已实现｜资源文件默认选第一项，推荐依据不清楚。** File summary identifies the recommendation basis, title, date, compatibility and filename before download.

44. **已实现｜资源下载只在全局状态文本中展示进度。** Resource progress includes project/file and completed/total where available; local task centre retains terminal result.

45. **已实现｜搜索、文件加载和下载会禁用整页与底部导航。** Resource operations no longer disable page or bottom navigation. Conflicting actions report the active task instead of silently returning.

46. **已实现｜实例表单校验错误只出现在全局状态条。** Name/version errors appear next to their form and focus the invalid input. Names reject path/control characters.

47. **已实现｜离开实例编辑页没有未保存提醒。** Instance edits have a saved-state comparison and target-specific discard dialog; cancel retains the draft.

48. **已实现｜创建与编辑版本主要依靠手工输入。** Create/edit version fields retain manual input plus a bounded, cancellable official release picker; modern `26.x` IDs are accepted.

49. **已实现｜参考加载器可被误解为已安装加载器。** Instance form explains that reference version/loader only filter resources and do not install a game/loader kernel.

50. **已实现｜微软授权有效期仅显示获取时的静态分钟数。** Device-code panel has a one-second real countdown and explicit expired/restart wording; cancellation stops it.

51. **已实现｜AI 流式更新强制滚到底，打断历史阅读。** AI follows only when the user is near the bottom, otherwise exposes “回到最新消息”.

52. **已实现｜AI 失败后没有就地重试或编辑重发。** Failed AI prompt returns to draft and offers retry/edit actions.

53. **已实现｜清空聊天没有确认或撤销。** Clear chat requires explicit confirmation and supports undo.

54. **已实现｜AI 回复按纯文本呈现，缺代码块操作。** Chat renderer separates fenced code, headings and lists; code has clipboard action and HTTPS links have explicit browser buttons.

55. **已实现｜AI 对话只存内存，保存范围缺少明确提示。** Bounded local `mobile-chat.json` persists at most 48 entries with a clear on-device storage notice; interrupted replies restore as stopped.

56. **已实现｜远程配对要求跨设备手工粘贴两份 JSON。** Pairing adds bounded JSON file import and public-offer file export, retaining explicit fingerprint confirmation and existing validation.

57. **已实现｜撤权确认勾选没有绑定到具体设备。** Revoke prompts with the exact target name and fingerprint, replacing the list-global checkbox as the active confirmation.

58. **已实现｜邀请只显示保存或过期状态，不显示到期时刻。** Selected computer shows room-ticket and renewal-subscription expiry times separately.

59. **已实现｜周期刷新重建所有行，读屏与键盘焦点可能跳动。** Remote host/task rows are keyed by stable IDs, updated in place and only inserted/removed when identity/order changes.

60. **已实现｜诊断字段直接显示内部英文 key。** Diagnostic fields use Chinese labels, with technical keys retained only in the data model.

61. **已实现｜固定高度 JSON 框叠在整页滚动中。** JSON is a collapsible technical section, with larger flexible bounds; field selection stays primary.

62. **已实现；原生/辅助技术验收单列｜图标与内联按钮的可视尺寸偏小，触控命中区待测。** Main icon, tab, inline, ComboBox and text-entry controls use 44 logical-pixel targets; explicit 32-pixel row buttons were enlarged.

63. **已实现；原生/辅助技术验收单列｜资源获取按钮的辅助名称不含项目名。** Resource-row action accessible name includes its bound project title.

64. **已实现；原生/辅助技术验收单列｜小字号与 Android 大字体适配尚需专项验收。** Android system FontScale is read and combined with a saved app 100/125/150/200% preference; text sizes are enlarged and labels wrap.

65. **已实现；原生/辅助技术验收单列｜Android 系统返回没有显式应用页栈接线。** TopLevel.BackRequested and Escape feed a page stack, resource-detail back and confirmation dismissal.

66. **已实现；原生/辅助技术验收单列｜刘海、状态栏与手势区安全边距尚未真机验收。** InsetsManager safe-area changes update Android padding, with duplicate automatic padding disabled.

67. **已实现；原生/辅助技术验收单列｜软键盘可能压缩或遮挡页底输入区域。** Android requests AdjustResize; InputPane events hide phone navigation while keyboard is open and reveal focused input.

68. **已实现｜删除世界存档没有前端二次确认或撤销。** World deletion now confirms the exact instance and world, with explicit backup/recovery warning.

69. **已实现｜备份还原直接覆盖，缺少还原前摘要。** Restore shows the target, backup and current-world modification information before overwrite confirmation.

70. **已实现｜删除备份缺少确认和撤销。** Backup deletion confirms the backup name and size/details and warns about removing the only recovery point.

71. **已实现｜删除模组或资源文件缺少前端确认。** Mod/resource deletion confirms instance, resource kind and filename, with explicit recovery guidance.

72. **已实现｜服务器移除立即生效，没有撤销。** Server removal confirms the record and exposes a one-record undo retaining its name, address, port and description.

73. **已实现｜全部启停模组忽略当前搜索范围。** Batch mod enable/disable evaluates the current filter at execution time. Hidden resources remain untouched; buttons say current filter.

74. **已实现｜分析实例日志会直接发送，缺少上传前内容预览。** Log analysis loads a redacted preview first. Sending that preview requires a second confirmation displaying the receiving host/mode and exact content.

75. **已实现｜日志分析在 UI 线程读取整个文件后取尾部。** Log reading runs off the UI thread and reads at most 64 KiB from the file tail, rather than reading the entire file.

76. **已实现｜AI 请求前清空草稿，失败后不恢复输入。** Failed AI prompts remain in the composer and expose a retry button.

77. **已实现｜AI 流式更新强制滚到底。** Streaming follows only if the reader was near the bottom. A jump-to-latest control appears when reading older content.

78. **已实现｜AI 输出没有 Markdown 和代码块结构。** AI document rendering now distinguishes headings, lists as separate readable lines, fenced monospace code with copying, and explicit safe HTTP(S) link buttons.

79. **已实现｜清空 AI 会话没有确认或撤销。** Clearing a conversation requires confirmation. Declining preserves it.

80. **已实现｜AI 配置加载失败只展开设置，不解释原因。** AI initialization failure shows a safe configuration/key-store repair hint and logs only the exception type.

81. **已实现｜下载忙状态会把资源页与实例页整体禁用。** Library and resource pages remain browseable while a task runs; conflicting writes still go through the owner gate.

82. **已实现｜忙时部分可点动作进入 RunTask 后直接返回。** Clicking a blocked action reports the currently running task instead of silently doing nothing.

83. **已实现｜忙时允许导航却跳过刷新，旧内容未标记。** Navigating during work labels data as pending refresh; task completion refreshes the visible page.

84. **已实现｜危险操作页隐藏当前实例副标题。** Full target-instance text is restored on world/mod/server pages and in the page chrome, with wrapping/tooltip.

85. **已实现｜左侧完整导航与顶部分类导航重复。** Repeated top-level segmented navigation is hidden; the side navigation is the single main location model.

86. **已实现｜窄窗固定二百零八像素侧栏挤压主内容。** The side rail narrows from 208 to 164 pixels below 1000px window width.

87. **已实现｜窄窗设置入口滚到侧栏底部才容易看到。** Settings is fixed outside the scrolling navigation list.

88. **已实现；原生/辅助技术验收单列｜十至十一点五像素辅助文字的可读性待测。** An explicit readable-text setting raises small labels to at least 13px and scales other labels. Actual native screen-reader/high-contrast certification is not claimed.

89. **已实现；原生/辅助技术验收单列｜实例图标操作只有 Tooltip，读屏名称待验证。** Instance mod/save icon buttons have explicit names including the instance name. Native screen-reader behavior still needs device testing.

90. **已实现｜改筛选清空结果，还需要再次点击搜索。** Filter changes retain the previous result list and trigger a safe refresh; failures retain those results with an error notice.

91. **已实现｜窄窗资源推荐区仍固定四列。** Resource discovery uses two columns below its narrow breakpoint and four when there is room.

92. **已实现｜固定宽度来源和排序挤压关键词输入框。** Source/sort controls move to a second row in narrow layouts; the query uses the available first-row width.

93. **已实现｜资源详情缺 WPF 已有的图库和完整介绍入口。** Resource details load real upstream gallery, full body and selected-file changelog using the shared typed Details API. Failure presents a retry action; upstream absence is stated explicitly.

94. **已实现｜资源列表有数量截断，却没有继续加载入口。** Real offset/page-size requests back previous/next paging. No fictional total is shown; next remains available for every nonempty batch because alias deduplication can shorten a page before upstream exhaustion.

95. **已实现｜任务日志详情固定一百五十六像素高。** Task logs have copy and expand/collapse actions and a larger initial area.

96. **已实现｜任务历史只保留本次进程、最多一百条。** Up to 100 terminal tasks survive restart in an atomically replaced, bounded, redacted local JSON history. Running tasks are not resurrected; corrupt files remain intact and are reported.

97. **已实现｜失败任务没有受限的就地重试入口。** Only explicit read/refresh operations get a retry capability. Deletes, overwrite, login, game launching and arbitrary operations are never replayed through this control.

98. **已实现｜Java 按钮写设为默认，实际绑定当前实例。** Java wording is now “用于当前实例”, matching its effect.

99. **已实现｜安装确认区缺目标路径、磁盘和已有版本处理摘要。** Version installation reads the typed upstream plan in the background and confirms target, published download bytes, free space and existing-file policy. Unknown parts remain labelled unknown; known insufficient space blocks installation. Failure allows cancel/retry or an explicit continue, not an invented estimate.

100. **已实现｜三端导航名称、设置分组与主要动作不一致。** Primary navigation concepts align across the clients: launch, instances, resources, AI assistant and settings. Mobile labels distinguish resource preparation from unavailable local game execution.

## 本地验收

- WPF 818/818；Portable 348/348；Mobile 57/57；桌面专项9项通过、1项Unix权限检查在Windows跳过。
- 共享HTTP分页/详情与回收/冲突/篡改边界8项通过；WPF真实UI行为17项通过。
- 完整WPF Release 0警告/0错误；Desktop、Mobile、Android ARM64构建通过，Core既有平台/过时API警告仍保留。
- 桌面自检保留原场景并补日志轮换、固定导入目标、拒绝覆盖与跨筛选迟到结果断言。
- 手机自检补持久聊天、损坏记录备份、确认模态、弱引用字体状态、任务中心、非阻塞导航和文件导入迟到结果隔离。

GitHub三尺寸预览与Windows/Linux测试收据已下载到 `TestResults/frontend-complete-20261119/github-tested-code/`。Android模拟器结果在最终收据中单独记录。

原工作区仍保留原分支和未提交内容。修改前文件字节及SHA-256在 `TestResults/frontend-complete-20261119/prechange/manifest.json`，100项机器矩阵在同目录上级 `completion-ledger.json`。
