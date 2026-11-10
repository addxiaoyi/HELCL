# 许可证与发布仓库核实（2026-11-11）

通过当前已登录的 GitHub API 读取，结果如下：

- 启动器源码：[addxiaoyi/HELCL](https://github.com/addxiaoyi/HELCL)，公开仓库、main 分支，当前账号 `addxiaoyi` 有 ADMIN 权限。远程 HEAD 为 `e82bf4a48db6b12d4851015000e112122287c9bb`。根目录含 `TipCL.sln`、`src`、`tests`；GitHub `licenseInfo=null`，许可证 API 返回 404。
- 插件市场：[addxiaoyi/HELL](https://github.com/addxiaoyi/HELL)，公开仓库、main 分支、MIT，版权行为 `Copyright (c) 2026 addxiaoyi2048`。README 明确它是启动器插件公共目录，源码主要为 `market`、`plugins` 和目录构建工具，不能据此给启动器代码套用同一许可证。
- 上游：[wfwwq/TipCL](https://github.com/wfwwq/TipCL)，公开仓库、main 分支、MIT，版权行为 `Copyright (c) 2026 wfwwq`。远程 HEAD 为 `803dd3857e62b9c89d7eda2b0b892a0a724438a6`，LICENSE Git blob SHA 为 `3f8248572b3aaf6019cfc6b3209fdc187f3cf405`。

已原样保留上游许可文本：[licenses/TipCL-MIT.txt](licenses/TipCL-MIT.txt)，文件 SHA-256 为 `597eb74c2d55107819f7b31a972a74aec51488621147c4ed86778af1158dec3d`。便携包附带这份上游声明。

这份 MIT 授权允许在遵守原版权与许可声明的前提下使用、修改和再分发上游代码；它不自动决定 HelL 新增代码的许可证，也不能替代全部资产、第三方组件和历史来源的检查。当前没有擅自为用户原创部分新增根 LICENSE。

当前本地 checkout 无 remote，基准提交 `7fd46e2283f27a964082a0f6bf1a67e077bb75d6` 与远程 main 不同，且包含大量尚未提交工作。macOS 测试应使用 HELCL 下独立 `codex/portable-native-*` 分支，明确列出提交文件，不覆盖 main 或改动 HELL 插件市场。

SignPath 申请仍需补齐：HelL 根许可证及原创版权授权、第三方归属清单、真实的签名审批人/MFA/来源策略、历史安全软件检出的调查结论。未提交申请，未获得基金会背书。
