# 桌面层次与留白精修

基线：`e8403e1f1f058bb27927d2fa0628f889cbebd812`。本轮只调整桌面显示层，不修改实例、安装、同步、远程或账号实现。

## 调整

- 无系统模糊支持或只支持普通透明时，窗口改用清晰的实色后备背景；真正支持 Acrylic、Blur 或 Mica 时继续使用原有通透度。实色选项保持不透明，主题和玻璃偏好不被后备显示覆盖。透明能力及实际主题传播变化时重新应用窗口色调；窗口底色由该逻辑单独控制，移除重复的资源背景绑定，避免系统主题改变后背景和文字不同步或重新引入半透明。设置页用简短说明解释效果取决于系统能力。
- 面板改为较安静的平面层次，减轻高光边框和大阴影，辅助文字使用桌面专用色值。实例列表撤去覆盖整列空白的背景；选中行保留明显的细边与填色。检视器按内容顶对齐，宽窗不再拉满空白高度，窄窗保留有界滚动与原有编辑能力。
- 侧栏实例数改为独立小标记并留出间距，导航字号统一到 13px。实例名称和固定标记使用受约束的两列布局，长名称省略后可查看完整提示，右侧三个操作保持可见。刷新、视图切换等次操作采用轻量样式，主要安装和启动操作保持突出。

## 对照与验证

`MainWindow.RefineCheck.cs` 接入既有 `--ui-smoke`。对照在基线产品文件和最终产品文件之间使用同一检查程序、排序后的同一隔离数据、相同主题和 800/1180 视口。两边都明确选择 `WindowTransparencyLevel.None`，验证的是无合成器模糊的真实后备路径；这不是对操作系统原生磨砂效果的截图认证。对照脚本逐字节保存并在 `finally` 中恢复专树产品文件，源码哈希恢复检查通过。

基线对照退出 **1**，记录后备背景仍半透明、侧栏计数紧贴、浅色辅助文字对比不足、宽窗检视器空白拉伸，以及长名称布局失败。最终实际 UI 退出 **0**：

- 无模糊后备的背景 alpha 为 **255**，玻璃模式偏好仍可保留和恢复。
- 侧栏计数与标签的测得间距由约 **0.7px** 提升到 **16.7px**。
- 浅色检视器辅助文字对实际截图背景的对比由约 **3.9** 提升到约 **5.9**；深色约 **7.8**。这是指定文字和背景采样的验证，不是全应用对比度认证。
- 1180 宽度下，检视器由约 **554px** 的空白整列收敛到约 **357px** 的内容面板。
- 800/1180 的长中文名称与固定标记均未覆盖右侧三个操作；初始可见的主要操作保留。受控实际主题传播检查也通过。

已查看同一数据的明暗主题 × 800/1180 截图，以及两张长名称截图。既有 `desktop-icons.json`、`ui-smoke.json`、`frontend-recovery.json`、`desktop-recheck.json`、`desktop-details.json` 和 `document-release-binding.json` 仍为成功。截图底部的任务状态来自既有受控故障场景，不代表真实用户任务结果。

```powershell
dotnet build src/HelL.Desktop -c Release --no-incremental -v quiet
dotnet src/HelL.Desktop/bin/Release/net10.0/HelL.Desktop.dll --home <new-isolated-profile> --ui-smoke
dotnet test tests/HelL.Desktop.Tests -c Release --logger 'trx;LogFileName=desktop.trx'
git diff --check
```

最终完整构建退出 **0**，**0 错误**，保留 Core 既有的 **16 条平台/过时 API 警告**，没有修改警告策略；桌面服务测试 **27 通过、1 个 Unix 权限专项在 Windows 跳过**。没有新增依赖或动画，没有提交、推送或改动原工作区。

证据目录：`TestResults/frontend-refine-20261120/desktop/`，包括 `before-complete/`、`final-complete/`、`comparison-complete-receipt.json`、`tests/desktop-final.trx`、构建和退出日志。`final-complete/refine/desktop-{light,dark}-{800,1180}.png` 与 `desktop-long-name-{800,1180}.png` 为最终对照图。早期对照保留在 `before-final/`、`final/` 与 `comparison-receipt.json`。生成配置和截图不纳入代码提交。Linux/macOS 的整合验证、原生磨砂、读屏及物理显示缩放由后续验收分别记录。
