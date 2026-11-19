# 前端百项复核补充

本轮基于首次交付 `4aec474`，对应 Draft PR #6。复核确认首次“全部完成”表述过宽：渐进加载没有解决累计卡片创建，设置错误提示也没有防止部分生效。本轮修复这些残留及三端异步交互边界，保留已有界面风格与业务能力。

- WPF：真正的实例库回收虚拟化；编辑草稿防后台刷新；目录、语言、多开、外观和常规设置经单次 RPC 原子持久化；提交失败保留草稿与原配置；显式主题加载与草稿替换确认。
- 手机：Modrinth 标准 ID 和旧数据 slug 分页去重；资源请求绑定完整上下文；取消返回不消耗页面历史；文件选择绑定原实例；触控目标统一至少 44 逻辑像素。
- Avalonia Desktop：详情文档绑定具体文件和请求版本；AI 旧失败重试不覆盖新输入；日志读取迟到错误不串实例；日志预览增加常见地址和含空格路径过滤，仍需用户确认上传。

实现与失败优先证据分别见 [WPF](frontend-wpf-recheck.md)、[手机](frontend-mobile-recheck.md)、[桌面](frontend-desktop-recheck.md)。[百项机器矩阵](frontend-completion-status.json)记录每项对应方案，并为本次专项项新增回执名称；它不是 100 项分别通过真机认证的声明。

## 本地整合验证

Windows 隔离工作树 `frontend-recheck`：WPF 818 通过；Portable 351 通过、1 个 Unix 符号链接专项跳过；Mobile 57 通过；Desktop 27 通过、1 个 Unix 权限专项跳过；Remote 120 通过。完整 WPF Release 构建 0 警告、0 错误。Desktop 构建成功，Core 首次构建仍有既有的 16 条平台/过时 API 警告；Mobile Preview 构建成功，0 警告、0 错误。

实际 UI 验收覆盖 WPF 首次修补、2000 条实例虚拟化、新增原子设置和主题交互；桌面实际 UI/TCS 验收覆盖文件 A→B→A、迟到成功/错误、取消重试、AI 草稿与实例日志；手机普通与远程预览保留新增受控异步断言。本轮本地回执在 `TestResults/frontend-recheck-20261119/`，生成配置和临时凭据夹具不提交。

GitHub 增加 macOS 服务测试，保留 Linux/Windows、三种手机普通/远程尺寸、桌面预览与 Android API35 模拟器验收；WPF 工作流强制运行新增复核，Android 必须取得新增手机复核回执，保留 150% 进程重启和密钥恢复，并追加 200% Activity 字体验收。远端结果以当前提交的 Actions 回执为准。

物理 Android 设备、TalkBack/桌面读屏、厂商键盘和不同刘海设备尚无本轮验证证据。模拟器字体验收不能代替上述条件。未合并、发布或部署。
