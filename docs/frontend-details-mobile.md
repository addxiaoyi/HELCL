# 手机交互细节补充

基线为已验提交 `1fbdeae746f64e5cc001f0726e340151265acde7`。本轮沿用现有界面和业务能力，补三个明确复现的交互边界。

## 修补范围

- AI 失败恢复：点击“重试上个问题”时，新输入的不同草稿会保留，并提示先发送或清空；“编辑后重发”在替换不同草稿前确认，取消后保留文字与焦点。确认返回时重新检查失败问题、草稿与任务状态，防止迟到确认替换已变化的内容。清空聊天成功后移除旧失败问题的恢复入口。
- 确认框焦点：保留原先默认聚焦安全取消按钮与禁用背景的行为。取消后，仅对仍可见、启用且属于当前顶层窗口的原控件恢复焦点；不会把焦点拉回隐藏页面、禁用输入或已移除控件。确认操作由后续操作自行决定焦点。
- 资源浏览位置：普通资源列表离开页面前保存阅读位置，重新进入时恢复。打开详情、返回列表的既有位置恢复行为保持。

## 失败优先验收

新增 `MobileHome.DetailsPreview.cs` 接入既有 `CheckFrontendRemediationAsync`，因此完整手机 smoke 与 Android native smoke 都会执行。AI 发送使用私有、仅 smoke 赋值的受控异步接缝；资源结果使用已有受控来源，均不调用外部服务。测试配置只写当前随机隔离 profile，结束后还原原 AI 字段，不创建真实密钥。

首次实际 390×844 完整 smoke 退出 1，回执记录六条失败：重试覆盖新草稿、编辑替换缺少确认、取消后草稿已丢失、取消未恢复原焦点、清空后保留旧失败入口、返回资源页丢失位置。原先取消按钮的初始焦点检查通过，因此没有改动该行为。

修复后扩充为 15 条实际 UI 检查，还覆盖：空草稿正常重试并清除旧失败状态、确认期间恢复目标改变时不替换、取消不恢复隐藏/禁用/移除控件焦点，以及真实可滚动资源列表的往返位置。

本地证据根目录：`TestResults/frontend-details-20261120/mobile/`，包括 `red-390x844/mobile-details-preview.json`、`red-390x844/mobile-smoke-error.txt`、构建日志、TRX 和各完整预览回执。生成的配置、聊天夹具与临时密钥不得作为源码批量提交。

## 验证命令

```powershell
dotnet build tools/HelL.Mobile.Preview -c Release -v quiet
dotnet test tests/HelL.Mobile.Tests -c Release --logger 'trx;LogFileName=mobile-details.trx'
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 320 720
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 390 844
$env:HELL_MOBILE_SYSTEM_TEXT_SCALE = '2'
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 320 720
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 390 844
```

这些是 Windows 托管的 Avalonia UI 验收，不能代替物理触控、Android 软键盘或 TalkBack 的专项验证。最终集成分支和原生 CI 结果由总交付记录单独登记。

本轮专树构建退出 0，最终构建为 0 警告、0 错误；Mobile 单测 57/57。完整普通 320×720、390×844、系统字体 200% 的 320×720 与 390×844 均退出 0，15 条新增断言及全部既有断言通过；远程 390×844 预览也退出 0。`git diff --check` 通过。已查看两种字体下的资源返回截图，确认结果仍处于离开前的滚动位置。
