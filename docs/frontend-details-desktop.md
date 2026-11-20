# 桌面交互细节补充

基线：`1fbdeae746f64e5cc001f0726e340151265acde7`。实现位于独立 `frontend-details-desktop` 工作树，未提交或推送。

## 修补范围

- AI 整段复制此前直接等待系统剪贴板；拒绝或不可用会让异常离开 UI 回调。现在保留原回复、显示可重试提示并记录异常类型，避免把提供程序内部路径显示给用户。代码块原有错误处理保持原样。
- 日志发送确认等待期间，另一项异步日志预览可能更新输入。现在批准与确认前的原始草稿、日志审阅状态绑定，内容变化后保留新草稿并要求重新确认；未变化的已批准日志仍可正常发送。
- AI 连接设置保存期间只锁定设置表单，防止后台保存完成后清空或隐藏后续输入。失败后恢复编辑并保留模型、地址和密钥草稿；成功后才清空密钥输入。
- 任务列表无选中记录时的说明改为最近 100 条已结束任务保存在本机，与已有持久化行为一致。

## 失败优先与实际界面验收

`MainWindow.DetailCheck.cs` 接入原有 `--ui-smoke`，保留此前所有断言。使用真实窗口和 TCS 控制剪贴板拒绝、确认期间新预览以及慢配置保存；不访问外部 AI 服务，不写入真实密钥。确认、发送和保存夹具在 `finally` 中还原；检查结束恢复输入和设置表单原状态。

修补前实际 UI 运行 exit **1**，`desktop-details.json` 明确记录三组失败：`copyFailureHandled=false`、`changedPromptPreserved=false`、`configLockedWhileSaving=false`。

最终实际 UI 运行 exit **0**：`copyFailureHandled`、`copyRetry`、`changedPromptPreserved`、`approvedPromptSent`、`configLockedWhileSaving`、`saveFailurePreserved` 均为 true；旧 `ui-smoke.json`、`frontend-recovery.json`、`desktop-recheck.json` 和 `document-release-binding.json` 保持成功。

```powershell
dotnet build src/HelL.Desktop -c Release -v quiet
dotnet src/HelL.Desktop/bin/Release/net10.0/HelL.Desktop.dll --home <isolated-evidence-root> --ui-smoke
dotnet test tests/HelL.Desktop.Tests -c Release --logger 'trx;LogFileName=desktop.trx' --results-directory <evidence-root>
git diff --check
```

最终 Desktop 构建 **0 警告、0 错误**；服务测试 **27 通过、1 项 Unix 权限专项在 Windows 跳过**；差异空白检查通过。首次构建 Core 时仍有基线既有的 16 条平台与过时 API 警告，未通过更改警告策略隐藏。

父工作区证据目录：`TestResults/frontend-details-20261120/desktop/`，包含 `red/desktop-details.json`、`green/desktop-details.json`、`final/desktop-details.json`、最终旧回归 JSON 与 `desktop.trx`。构建和退出日志位于父目录的 `frontend-details-20261120-desktop-*.log`。生成配置、截图、假密钥夹具和 TRX 不纳入源码提交。

本轮是 Windows 上的实际 Avalonia UI 与受控故障验收；Linux/macOS 组合分支与原生读屏结果由整合后流程记录，尚无本子任务的原生读屏认证。
