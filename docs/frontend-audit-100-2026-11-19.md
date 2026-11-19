# 前端 100 项不足与改进清单（2026-11-19）

范围：当前 Windows 工作区的 WPF、Avalonia Desktop、Mobile 代码与本轮隔离预览。只读审查，没有修复、提交或部署。

这不是 100 个已复现严重 bug：确认 63 项，建议 26 项，待测 11 项。确认指已见代码路径或截图支持，不代表每个异常情景都经运行注入。P1 优先处理（30 项），P2 体验完善（69 项），P3 增强（1 项）。手机预览由 Windows 承载，不能证明 Android 真机、TalkBack、软键盘、安全区行为。

本轮截图包含：WPF 900/1180 宽、浅/深主题的首页与设置；Avalonia Desktop 800/1060/1180 宽的实例、资源、安装、任务、设置、AI；Mobile 390×844 的资源、账号、AI，以及远程/诊断。测试内容来自隔离 fixture，未把模拟日期或模拟错误当作真实产品故障。

已实现的断网恢复、撤权、诊断取消/重试和权限边界未被重复列为缺失。

## 本轮截图与流程

1. WPF 首页与设置：基本导航、保存固定区正常；仍有可发现性与输入反馈问题。
2. 桌面实例、资源、安装、任务：流程可运行；窄窗密度、危险操作确认和目标提示仍需完善。
3. 手机资源、账号、AI：预览可运行；入口语义、分页、失败重试和输入体验不足。
4. 手机远程与诊断：闭环自检通过；配对便利性、设备专属确认和读屏稳定性仍有改进空间。

![WPF 设置，本轮 900px](D:/qwq/qdq/TestResults/frontend-audit-20261119/wpf/settings-game-dark-900.png)

![Avalonia Desktop 安装，本轮 1180px](D:/qwq/qdq/TestResults/frontend-audit-20261119/desktop/ui-install-1180.png)

![Mobile 首页，本轮 390×844](D:/qwq/qdq/TestResults/frontend-audit-20261119/mobile-390x844/mobile-home.png)

## 100 项清单

### Windows WPF

1. **[P1｜确认] 新输入的 AI / CurseForge API Key 明文显示。** 默认遮挡，按需显隐，避免录屏或共享屏幕泄露。 证据：[src/HelL/Pages/SettingsPage.cs:261](D:/qwq/qdq/src/HelL/Pages/SettingsPage.cs:261)

2. **[P1｜确认] 皮肤站登录关闭弹窗后才校验必填项。** 在原弹窗内提示错误并保留输入，避免重新填写。 证据：[src/HelL/Pages/AccountPage.cs:538](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:538)

3. **[P1｜确认] 统一通行证的服务器 ID 校验发生在弹窗关闭后。** 在提交前校验服务器 ID 和凭据格式，错误留在当前流程。 证据：[src/HelL/Pages/AccountPage.cs:562](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:562)

4. **[P2｜确认] 微软验证码未返回时复制与打开浏览器按钮已可点。** 验证码和地址就绪前禁用相关动作，避免复制占位内容。 证据：[src/HelL/Pages/AccountPage.cs:490](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:490)

5. **[P2｜建议] 设备登录缺少倒计时与就地重新取码。** 显示剩余有效期，并在过期后给明确的重新授权入口。 证据：[src/HelL/Pages/AccountPage.cs:592](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:592)

6. **[P3｜建议] 账号 UUID 行缺少完整值复制入口。** 支持复制完整标识，截断时提供完整值提示。 证据：[src/HelL/Pages/AccountPage.cs:240](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:240)

7. **[P2｜建议] 账号列表缺少按名称或类型查找。** 为多账号用户提供搜索、类型筛选和当前账号定位。 证据：[src/HelL/Pages/AccountPage.cs:105](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:105)

8. **[P2｜待测] 长账号名与固定右侧操作在窄窗下可能拥挤。** 用长名称实测窄屏，必要时将次要操作折入菜单。 证据：[src/HelL/Pages/AccountPage.cs:261](D:/qwq/qdq/src/HelL/Pages/AccountPage.cs:261)

9. **[P1｜确认] 收藏读取失败会回退为空收藏。** 区分读取失败和真实空列表，并保留重试入口。 证据：[src/HelL/Pages/CatalogPage.cs:607](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:607)

10. **[P1｜确认] 模组启停失败后开关不回滚且缺行内错误。** 等待提交结果，失败恢复原状态并说明原因。 证据：[src/HelL/Pages/CatalogPage.cs:1169](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:1169)

11. **[P2｜建议] 搜索开始用骨架屏替换全部旧结果。** 保留上次结果并标记刷新，减少连续搜索时闪变。 证据：[src/HelL/Pages/CatalogPage.cs:626](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:626)

12. **[P2｜确认] 资源搜索固定九秒超时，慢网络只能重试。** 提供继续等待或缓存结果选择，保留当前条件。 证据：[src/HelL/Pages/CatalogPage.cs:658](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:658)

13. **[P2｜确认] 游戏版本下拉推荐列表写死。** 从可用版本目录更新推荐项，继续允许手输。 证据：[src/HelL/Pages/CatalogPage.cs:27](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:27)

14. **[P2｜确认] 打开资源页会自动读取剪贴板并使用识别链接。** 先显示是否使用剪贴板链接，避免意外发起搜索。 证据：[src/HelL/Pages/CatalogPage.cs:1297](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:1297)

15. **[P2｜建议] 混合来源搜索缺少各来源状态。** 分辨部分来源失败与确实无结果，允许单来源重试。 证据：[src/HelL/Pages/CatalogPage.cs:631](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:631)

16. **[P2｜建议] 已安装资源删除没有回收或撤销窗口。** 保留现有确认，并考虑可恢复删除或短时撤销。 证据：[src/HelL/Pages/CatalogPage.cs:1324](D:/qwq/qdq/src/HelL/Pages/CatalogPage.cs:1324)

17. **[P2｜确认] 最近游玩排序只加载二十四个实例的时间。** 覆盖所有候选实例，避免其余实例统一按零排序。 证据：[src/HelL/Pages/InstancePage.Library.cs:26](D:/qwq/qdq/src/HelL/Pages/InstancePage.Library.cs:26)

18. **[P2｜确认] 排序和只看固定筛选的偏好不随其它库偏好保存。** 统一保存并恢复实例库显示状态。 证据：[src/HelL/Pages/InstancePage.cs:125](D:/qwq/qdq/src/HelL/Pages/InstancePage.cs:125)

19. **[P2｜确认] 实例卡片全部创建，缺少可见区域虚拟化。** 对大实例库做虚拟化；实际性能收益需规模测试。 证据：[src/HelL/Pages/InstanceTiles.cs:27](D:/qwq/qdq/src/HelL/Pages/InstanceTiles.cs:27)

20. **[P2｜确认] 打开启动页会隐含将实例设为全局默认。** 把导航与修改默认实例分开，或明确提示副作用。 证据：[src/HelL/Pages/InstancePage.cs:447](D:/qwq/qdq/src/HelL/Pages/InstancePage.cs:447)

21. **[P2｜建议] 批量固定操作包含被筛选隐藏的已选实例。** 操作前预览目标，区分全部选中项与仅可见项。 证据：[src/HelL/Pages/InstancePage.Batch.cs:68](D:/qwq/qdq/src/HelL/Pages/InstancePage.Batch.cs:68)

22. **[P2｜待测] 实例列表移除了默认焦点视觉，键盘定位需复核。** 实测 Tab 和方向键，并补独立于选中态的焦点指示。 证据：[src/HelL/Pages/InstancePage.cs:43](D:/qwq/qdq/src/HelL/Pages/InstancePage.cs:43)

23. **[P1｜确认] 版本列表双击会直接安装。** 双击只选定或要求确认，保留显式安装按钮。 证据：[src/HelL/Pages/VersionPage.cs:174](D:/qwq/qdq/src/HelL/Pages/VersionPage.cs:174)

24. **[P2｜确认] 安装期间连版本搜索和浏览一起禁用。** 冻结当前安装参数，同时允许继续只读浏览。 证据：[src/HelL/Pages/VersionPage.Installation.cs:86](D:/qwq/qdq/src/HelL/Pages/VersionPage.Installation.cs:86)

25. **[P2｜确认] 版本列表固定为二百九十像素高。** 随窗口可用高度伸缩，改善大窗利用率和小窗密度。 证据：[src/HelL/Pages/VersionPage.cs:203](D:/qwq/qdq/src/HelL/Pages/VersionPage.cs:203)

26. **[P2｜建议] 安装前没有下载量和磁盘占用摘要。** 展示预计下载量、额外空间和可用磁盘空间。 证据：[src/HelL/Pages/VersionPage.Installation.cs:58](D:/qwq/qdq/src/HelL/Pages/VersionPage.Installation.cs:58)

27. **[P2｜建议] 加载器折叠项与禁用选项的兼容原因不够直观。** 在禁用项旁直接解释版本或加载器约束。 证据：[src/HelL/Pages/VersionPage.Installation.cs:37](D:/qwq/qdq/src/HelL/Pages/VersionPage.Installation.cs:37)

28. **[P1｜确认] 保存设置可能先改目录、后续保存失败而部分生效。** 做原子提交或报告每阶段结果，避免只显示整体失败。 证据：[src/HelL/Pages/SettingsPage.cs:1364](D:/qwq/qdq/src/HelL/Pages/SettingsPage.cs:1364)

29. **[P1｜确认] 非法分辨率或限速数字会静默回退默认值。** 定位错误字段并阻止错误提交，明确最终采用值。 证据：[src/HelL/Pages/SettingsPage.cs:1394](D:/qwq/qdq/src/HelL/Pages/SettingsPage.cs:1394)

30. **[P2｜确认] AI 上下文非法或越界值会静默回退或截断。** 显示有效范围和实际保存值，不静默修正。 证据：[src/HelL/Pages/SettingsPage.cs:1415](D:/qwq/qdq/src/HelL/Pages/SettingsPage.cs:1415)

31. **[P2｜建议] 设置混合即时、保存后和重启后生效规则。** 逐项标记生效时机，并汇总未保存变更。 证据：[src/HelL/Pages/SettingsPage.cs:657](D:/qwq/qdq/src/HelL/Pages/SettingsPage.cs:657)

32. **[P2｜建议] 大量设置项缺少文字搜索和定位。** 提供按 JVM、代理、密钥等关键词检索设置。 证据：[src/HelL/Pages/SettingsWorkspace.cs:14](D:/qwq/qdq/src/HelL/Pages/SettingsWorkspace.cs:14)

33. **[P2｜确认] AI 对话搜索只查标题，不查正文。** 明确标为标题搜索，或支持对话内容检索。 证据：[src/HelL/Pages/AiPage.cs:957](D:/qwq/qdq/src/HelL/Pages/AiPage.cs:957)

34. **[P1｜确认] AI 历史右键删除没有确认或撤销。** 增加目标确认或撤销，避免整段对话误删。 证据：[src/HelL/Pages/AiPage.cs:1042](D:/qwq/qdq/src/HelL/Pages/AiPage.cs:1042)

35. **[P2｜确认] AI 历史搜索重建全部按钮，缺少虚拟化。** 对长历史做列表虚拟化与增量更新；卡顿需规模实测。 证据：[src/HelL/Pages/AiPage.cs:933](D:/qwq/qdq/src/HelL/Pages/AiPage.cs:933)

### 手机端

36. **[P1｜确认] 导入整合包入口实际打开线上资源搜索。** 改为查找整合包，或接入真正的本地文件导入。 证据：[src/HelL.Mobile/MobileHome.Layout.cs:161](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Layout.cs:161)

37. **[P2｜确认] 通知铃铛只写状态，没有通知或任务中心。** 接到当前任务、历史结果和失败定位。 证据：[src/HelL.Mobile/MobileHome.Layout.cs:157](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Layout.cs:157)

38. **[P2｜确认] 搜索与筛选图标执行完全相同动作。** 分别聚焦关键词与展开筛选，减少意图歧义。 证据：[src/HelL.Mobile/MobileHome.axaml:171](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:171)

39. **[P1｜确认] 资源兼容目标藏在默认收起的筛选区。** 常显当前实例、版本和加载器摘要，解释搜索范围。 证据：[src/HelL.Mobile/MobileHome.axaml:188](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:188)

40. **[P2｜确认] 新搜索先清空旧列表，失败后无法继续查看旧结果。** 保留最后成功列表，并标记更新失败。 证据：[src/HelL.Mobile/MobileHome.Resources.cs:35](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Resources.cs:35)

41. **[P1｜确认] 资源搜索最多三十条，没有下一页入口。** 支持分页或加载更多，说明已加载数量。 证据：[src/HelL.Core/Portable/PortableCatalog.cs:55](D:/qwq/qdq/src/HelL.Core/Portable/PortableCatalog.cs:55)

42. **[P2｜确认] 资源详情返回后不恢复列表滚动位置。** 分别保存列表与详情滚动位置。 证据：[src/HelL.Mobile/MobileHome.Resources.cs:66](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Resources.cs:66)

43. **[P2｜建议] 资源文件默认选第一项，推荐依据不清楚。** 突出所选文件、日期、发布类型和兼容理由。 证据：[src/HelL.Mobile/MobileHome.Resources.cs:110](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Resources.cs:110)

44. **[P2｜确认] 资源下载只在全局状态文本中展示进度。** 增加当前文件、总体数量、大小与任务记录视图。 证据：[src/HelL.Mobile/MobileHome.Resources.cs:137](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Resources.cs:137)

45. **[P1｜确认] 搜索、文件加载和下载会禁用整页与底部导航。** 只冻结冲突动作，允许切页查看任务与其它内容。 证据：[src/HelL.Mobile/MobileHome.axaml.cs:266](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml.cs:266)

46. **[P2｜确认] 实例表单校验错误只出现在全局状态条。** 在错误字段旁提示并引导焦点。 证据：[src/HelL.Mobile/MobileHome.axaml.cs:278](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml.cs:278)

47. **[P2｜确认] 离开实例编辑页没有未保存提醒。** 增加脏状态、草稿或保存与放弃提示。 证据：[src/HelL.Mobile/MobileHome.axaml.cs:195](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml.cs:195)

48. **[P2｜建议] 创建与编辑版本主要依靠手工输入。** 提供可搜索版本选择，同时保留手输。 证据：[src/HelL.Mobile/MobileHome.axaml:253](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:253)

49. **[P1｜确认] 参考加载器可被误解为已安装加载器。** 在字段旁说明仅影响资源筛选、不安装内核。 证据：[src/HelL.Mobile/MobileHome.axaml:269](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:269)

50. **[P2｜确认] 微软授权有效期仅显示获取时的静态分钟数。** 显示实时倒计时和过期重新获取入口。 证据：[src/HelL.Mobile/MobileHome.Account.cs:52](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Account.cs:52)

51. **[P1｜确认] AI 流式更新强制滚到底，打断历史阅读。** 仅在用户靠近底部时跟随，提供回到最新按钮。 证据：[src/HelL.Mobile/MobileHome.Ai.cs:104](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Ai.cs:104)

52. **[P2｜确认] AI 失败后没有就地重试或编辑重发。** 保留失败问题并提供重试和编辑入口。 证据：[src/HelL.Mobile/MobileHome.Ai.cs:65](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Ai.cs:65)

53. **[P2｜确认] 清空聊天没有确认或撤销。** 防止触屏误点丢失整段会话。 证据：[src/HelL.Mobile/MobileHome.Ai.cs:95](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Ai.cs:95)

54. **[P2｜确认] AI 回复按纯文本呈现，缺代码块操作。** 结构化显示列表、链接、代码，并支持复制代码块。 证据：[src/HelL.Mobile/MobileHome.axaml:335](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:335)

55. **[P2｜确认] AI 对话只存内存，保存范围缺少明确提示。** 说明重启行为，或提供主动保存和导出。 证据：[src/HelL.Mobile/Ai/MobileChat.cs:17](D:/qwq/qdq/src/HelL.Mobile/Ai/MobileChat.cs:17)

56. **[P1｜建议] 远程配对要求跨设备手工粘贴两份 JSON。** 提供受控二维码或文件导入，并保留指纹确认。 证据：[src/HelL.Mobile/MobileHome.Remote.cs:29](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Remote.cs:29)

57. **[P1｜建议] 撤权确认勾选没有绑定到具体设备。** 确认框显示目标设备名和指纹，避免多设备误撤权。 证据：[src/HelL.Mobile/MobileHome.Remote.cs:33](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Remote.cs:33)

58. **[P2｜确认] 邀请只显示保存或过期状态，不显示到期时刻。** 显示剩余有效期，并区分房间票据与续期订阅。 证据：[src/HelL.Mobile/MobileHome.RemoteDetails.cs:19](D:/qwq/qdq/src/HelL.Mobile/MobileHome.RemoteDetails.cs:19)

59. **[P2｜待测] 周期刷新重建所有行，读屏与键盘焦点可能跳动。** 按稳定 ID 更新行，并实际验证焦点与阅读位置。 证据：[src/HelL.Mobile/MobileHome.Remote.cs:137](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Remote.cs:137)

60. **[P2｜确认] 诊断字段直接显示内部英文 key。** 优先使用本地化标签，技术 key 放详情。 证据：[src/HelL.Mobile/MobileHome.Diagnostics.cs:120](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Diagnostics.cs:120)

61. **[P2｜建议] 固定高度 JSON 框叠在整页滚动中。** 提供摘要、展开或全屏预览，减少嵌套滚动。 证据：[src/HelL.Mobile/MobileHome.Diagnostics.cs:16](D:/qwq/qdq/src/HelL.Mobile/MobileHome.Diagnostics.cs:16)

62. **[P1｜待测] 图标与内联按钮的可视尺寸偏小，触控命中区待测。** 核对实际命中区，为小控件增加触控空间。 证据：[src/HelL.Mobile/MobileApp.axaml:119](D:/qwq/qdq/src/HelL.Mobile/MobileApp.axaml:119)

63. **[P2｜待测] 资源获取按钮的辅助名称不含项目名。** 读屏名称绑定到具体项目，验证列表导航可区分。 证据：[src/HelL.Mobile/MobileHome.axaml:225](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:225)

64. **[P2｜待测] 小字号与 Android 大字体适配尚需专项验收。** 在 1.3、1.5、2 倍字体下检查换行和控件扩高。 证据：[src/HelL.Mobile/MobileHome.axaml:393](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:393)

65. **[P1｜待测] Android 系统返回没有显式应用页栈接线。** 验证先关键盘、再回列表与页面，不意外退出。 证据：[src/HelL.Android/MainActivity.cs:11](D:/qwq/qdq/src/HelL.Android/MainActivity.cs:11)

66. **[P1｜待测] 刘海、状态栏与手势区安全边距尚未真机验收。** 测试全面屏、打孔屏与手势导航，补安全区适配。 证据：[src/HelL.Mobile/MobileHome.axaml:5](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:5)

67. **[P1｜待测] 软键盘可能压缩或遮挡页底输入区域。** 实测 AI、邀请 JSON、实例名称和横屏输入。 证据：[src/HelL.Android/MainActivity.cs:9](D:/qwq/qdq/src/HelL.Android/MainActivity.cs:9)

### Avalonia 桌面

68. **[P1｜确认] 删除世界存档没有前端二次确认或撤销。** 展示世界名称并确认，提供可恢复策略。 证据：[src/HelL.Desktop/Views/SavesManagerView.axaml.cs:124](D:/qwq/qdq/src/HelL.Desktop/Views/SavesManagerView.axaml.cs:124)

69. **[P1｜确认] 备份还原直接覆盖，缺少还原前摘要。** 显示覆盖目标和当前世界时间后确认。 证据：[src/HelL.Desktop/Views/SavesManagerView.axaml.cs:136](D:/qwq/qdq/src/HelL.Desktop/Views/SavesManagerView.axaml.cs:136)

70. **[P1｜确认] 删除备份缺少确认和撤销。** 显示备份名及大小，保护唯一恢复点。 证据：[src/HelL.Desktop/Views/SavesManagerView.axaml.cs:147](D:/qwq/qdq/src/HelL.Desktop/Views/SavesManagerView.axaml.cs:147)

71. **[P1｜确认] 删除模组或资源文件缺少前端确认。** 确认具体文件并说明恢复方式。 证据：[src/HelL.Desktop/Views/ModsManagerView.axaml.cs:154](D:/qwq/qdq/src/HelL.Desktop/Views/ModsManagerView.axaml.cs:154)

72. **[P2｜确认] 服务器移除立即生效，没有撤销。** 提供短时撤销，减少误点造成的连接信息丢失。 证据：[src/HelL.Desktop/Views/ServersView.axaml.cs:144](D:/qwq/qdq/src/HelL.Desktop/Views/ServersView.axaml.cs:144)

73. **[P1｜确认] 全部启停模组忽略当前搜索范围。** 明确全部数量，或增加仅当前筛选的操作。 证据：[src/HelL.Desktop/Views/ModsManagerView.axaml.cs:101](D:/qwq/qdq/src/HelL.Desktop/Views/ModsManagerView.axaml.cs:101)

74. **[P1｜确认] 分析实例日志会直接发送，缺少上传前内容预览。** 云端模式先展示脱敏内容和接收服务，再确认发送。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml.cs:102](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml.cs:102)

75. **[P1｜确认] 日志分析在 UI 线程读取整个文件后取尾部。** 后台有界读取末尾，避免大日志卡界面。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml.cs:102](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml.cs:102)

76. **[P2｜确认] AI 请求前清空草稿，失败后不恢复输入。** 保存失败草稿并提供本条重试。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml.cs:120](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml.cs:120)

77. **[P2｜确认] AI 流式更新强制滚到底。** 尊重用户查看旧内容的位置，提供跳到最新。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml.cs:159](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml.cs:159)

78. **[P2｜确认] AI 输出没有 Markdown 和代码块结构。** 补代码块、列表、链接及单块复制。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml:61](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml:61)

79. **[P2｜确认] 清空 AI 会话没有确认或撤销。** 提供可撤销清空，避免丢失长对话。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml.cs:141](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml.cs:141)

80. **[P2｜确认] AI 配置加载失败只展开设置，不解释原因。** 区分密钥库、地址和配置损坏，提供安全修复提示。 证据：[src/HelL.Desktop/Views/AiAssistantView.axaml.cs:29](D:/qwq/qdq/src/HelL.Desktop/Views/AiAssistantView.axaml.cs:29)

81. **[P2｜确认] 下载忙状态会把资源页与实例页整体禁用。** 区分可浏览区域与会冲突的修改操作。 证据：[src/HelL.Desktop/MainWindow.axaml.cs:469](D:/qwq/qdq/src/HelL.Desktop/MainWindow.axaml.cs:469)

82. **[P1｜确认] 忙时部分可点动作进入 RunTask 后直接返回。** 禁用对应按钮，或明确提示当前任务。 证据：[src/HelL.Desktop/MainWindow.axaml.cs:431](D:/qwq/qdq/src/HelL.Desktop/MainWindow.axaml.cs:431)

83. **[P2｜确认] 忙时允许导航却跳过刷新，旧内容未标记。** 显示数据更新时间，并在任务完成后刷新当前页。 证据：[src/HelL.Desktop/MainWindow.axaml.cs:535](D:/qwq/qdq/src/HelL.Desktop/MainWindow.axaml.cs:535)

84. **[P1｜确认] 危险操作页隐藏当前实例副标题。** 在存档、模组和服务器页常显完整目标实例。 证据：[src/HelL.Desktop/Views/SavesManagerView.axaml:9](D:/qwq/qdq/src/HelL.Desktop/Views/SavesManagerView.axaml:9)

85. **[P2｜建议] 左侧完整导航与顶部分类导航重复。** 区分全局导航与页内切换，降低定位负担。 证据：[src/HelL.Desktop/MainWindow.axaml:160](D:/qwq/qdq/src/HelL.Desktop/MainWindow.axaml:160)

86. **[P2｜建议] 窄窗固定二百零八像素侧栏挤压主内容。** 增加折叠侧栏或紧凑导航模式。 证据：[src/HelL.Desktop/MainWindow.axaml:16](D:/qwq/qdq/src/HelL.Desktop/MainWindow.axaml:16)

87. **[P2｜建议] 窄窗设置入口滚到侧栏底部才容易看到。** 固定设置入口；当前可滚动到达，不是不可达缺陷。 证据：[src/HelL.Desktop/MainWindow.axaml:32](D:/qwq/qdq/src/HelL.Desktop/MainWindow.axaml:32)

88. **[P2｜待测] 十至十一点五像素辅助文字的可读性待测。** 验证缩放、低视力和高对比环境，并提供密度选择。 证据：[src/HelL.Desktop/Views/TasksView.axaml:31](D:/qwq/qdq/src/HelL.Desktop/Views/TasksView.axaml:31)

89. **[P2｜待测] 实例图标操作只有 Tooltip，读屏名称待验证。** 增加含实例名的明确辅助名称。 证据：[src/HelL.Desktop/Views/InstanceLibraryView.axaml:23](D:/qwq/qdq/src/HelL.Desktop/Views/InstanceLibraryView.axaml:23)

90. **[P2｜确认] 改筛选清空结果，还需要再次点击搜索。** 保留旧结果或自动重查，降低比较筛选条件的成本。 证据：[src/HelL.Desktop/Views/ResourceBrowserView.axaml.cs:45](D:/qwq/qdq/src/HelL.Desktop/Views/ResourceBrowserView.axaml.cs:45)

91. **[P2｜建议] 窄窗资源推荐区仍固定四列。** 按可用宽度降为两列，减少标题和说明反复折行。 证据：[src/HelL.Desktop/Views/ResourceBrowserView.axaml:71](D:/qwq/qdq/src/HelL.Desktop/Views/ResourceBrowserView.axaml:71)

92. **[P2｜建议] 固定宽度来源和排序挤压关键词输入框。** 窄窗将筛选移到第二行或折叠区域。 证据：[src/HelL.Desktop/Views/ResourceBrowserView.axaml:26](D:/qwq/qdq/src/HelL.Desktop/Views/ResourceBrowserView.axaml:26)

93. **[P2｜建议] 资源详情缺 WPF 已有的图库和完整介绍入口。** 补齐图库、完整介绍与版本更新日志。 证据：[src/HelL.Desktop/Views/ResourceBrowserView.axaml:139](D:/qwq/qdq/src/HelL.Desktop/Views/ResourceBrowserView.axaml:139)

94. **[P2｜确认] 资源列表有数量截断，却没有继续加载入口。** 说明结果范围并支持分页，避免只能看当前批次。 证据：[src/HelL/Core/Launcher/CatalogSearch.cs:45](D:/qwq/qdq/src/HelL/Core/Launcher/CatalogSearch.cs:45)

95. **[P2｜建议] 任务日志详情固定一百五十六像素高。** 允许展开、独立查看和复制长错误日志。 证据：[src/HelL.Desktop/Views/TasksView.axaml:44](D:/qwq/qdq/src/HelL.Desktop/Views/TasksView.axaml:44)

96. **[P2｜确认] 任务历史只保留本次进程、最多一百条。** 说明保存范围，提供轻量持久历史或导出。 证据：[src/HelL.Desktop/Views/TasksView.axaml.cs:31](D:/qwq/qdq/src/HelL.Desktop/Views/TasksView.axaml.cs:31)

97. **[P2｜建议] 失败任务没有受限的就地重试入口。** 仅为可安全重放的任务提供重试。 证据：[src/HelL.Desktop/Views/TasksView.axaml:45](D:/qwq/qdq/src/HelL.Desktop/Views/TasksView.axaml:45)

98. **[P2｜确认] Java 按钮写设为默认，实际绑定当前实例。** 改为用于当前实例，并显示实例名称。 证据：[src/HelL.Desktop/Views/JavaEnvironmentView.axaml:27](D:/qwq/qdq/src/HelL.Desktop/Views/JavaEnvironmentView.axaml:27)

99. **[P2｜建议] 安装确认区缺目标路径、磁盘和已有版本处理摘要。** 在提交前明确下载成本、空间和处理方式。 证据：[src/HelL.Desktop/Views/VersionInstallerView.axaml:97](D:/qwq/qdq/src/HelL.Desktop/Views/VersionInstallerView.axaml:97)

### 跨端一致性

100. **[P2｜建议] 三端导航名称、设置分组与主要动作不一致。** 统一启动、资源、我的或设置等命名与功能可用范围说明。 证据：[src/HelL.Mobile/MobileHome.axaml:391](D:/qwq/qdq/src/HelL.Mobile/MobileHome.axaml:391)

## 优先顺序

先处理密钥明文、危险删除/覆盖、错误状态误报、批量操作范围和日志发送确认；随后处理搜索/安装/AI失败恢复，再做密度、无障碍与真机适配。

完整数据：`TestResults/frontend-audit-20261119/findings.json`。本轮截图与构建/自检记录均在同目录。
