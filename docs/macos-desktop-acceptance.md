# macOS 下载后首次打开验收

本轮没有真实 Mac。GitHub runner 的原生启动、ad-hoc 验签和钥匙串测试不能替代以下桌面操作。

1. 用 Safari 或常用浏览器下载对应架构的 ZIP 与同次构建的 SHA256SUMS，执行 `shasum -a 256 -c SHA256SUMS`。保留下载文件的隔离属性。
2. 将整个文件夹解压到当前用户可写的固定位置，保留 `HelL.app` 和同级 `data`，不要从 ZIP 内或只读位置启动。
3. 运行 `python3 tools/verify_macos_download.py /完整路径/HelL.app --report before-open.json`，记录签名、隔离标记和 Gatekeeper 结果。脚本只检查状态，不修改系统设置。
4. 在 Finder 中尝试打开。ad-hoc 没有 Developer ID 或公证，系统可能拒绝。确认来源和校验值后，按系统实际提示右键“打开”，或“系统设置 → 隐私与安全性 → 仍要打开”。不要关闭 Gatekeeper 或删除隔离属性。
5. 在启动器中创建名称含中文的测试实例，关闭窗口，再打开，确认实例和选择仍在。确认数据写在 `.app` 同级的 `data`；`.app/Contents` 内不应出现个人数据。记录系统版本、芯片架构和实际提示截图。
6. 退出启动器后重新运行检查脚本。前后 app host SHA-256 应一致，`codesign --verify --deep --strict` 应始终成功。首次运行的允许操作不代表 Developer ID 或公证成功。
7. 可选运行 `--check-restart`：脚本在临时隔离目录中启动两次并校验实例恢复，不读取日常账号。这项自动化结果单独记录，不能代替第 4、5 步。

遇到“应用已损坏”、校验不一致或签名失效时停止测试并记录输出；不要为使验收通过而更改安全策略。账号实测须本人授权，报告不包含密码、设备码、令牌或钥匙串内容。
