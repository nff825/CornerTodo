# CornerTodo macOS · 构建与内测分发

Mac 内测版本独立为 **0.1.0-alpha.1**。Windows 正式版本仍为1.4.0，两者分别维护发布目录，不覆盖 Windows 已发布附件或标签。

## 构建入口

在项目根目录执行：

```powershell
.\macOS\Build-Mac.ps1 -Architecture all -DotNetPath $dotnetPath -PythonPath $pythonPath
```

使用参数：

| 参数 | 用途 |
| --- | --- |
| Architecture | all、arm64或x64；all生成两架构 |
| DotNetPath | .NET SDK主程序完整路径 |
| PythonPath | Python 3.9+完整路径 |
| NuGetConfigPath | 默认macOS/NuGet.Config；可传本地离线源配置 |
| ToolingDirectory | CLI与NuGet缓存，默认output/macOS/tooling |
| OutputDirectory | 默认release/macos/v0.1.0-alpha.1，必须在本项目内 |

输出目录中已有同名包时，脚本拒绝覆盖；可指定新的输出目录重新验证。中间文件保留在 `output/macOS/build-*`，便于检查；不把它们加入源码仓库或分发。

构建会从 `Packaging/source-files.json` 的显式清单复制干净源码，在副本中编译并运行共享规则／数据测试，再自包含发布ARM64/x64，封装.app、检查架构／代码页哈希、ZIP CRC与执行权限。失败时不把半成品替换到发布目录。源码包只包含Mac构建必要的工程、共享源码、测试、文档和许可，不是Windows工程的完整替代品。

## 产物

默认目录为：

```text
release/macos/v0.1.0-alpha.1/
```

- `CornerTodo-macos-v0.1.0-alpha.1-arm64-experimental.zip`
- `CornerTodo-macos-v0.1.0-alpha.1-x64-experimental.zip`
- `CornerTodo-macos-v0.1.0-alpha.1-source.zip`
- 各包的manifest和单独SHA256，以及合并的`SHA256SUMS.txt`
- `PACKAGE-REPORT.txt`

便携包包含CornerTodo.app和测试说明，许可及第三方通知位于应用包内。源码展开目录可用于核对或重新构建。源码ZIP来自同一次干净构建副本，清单和逐文件摘要均可检查。

界面与菜单栏使用本项目自有图标导出的PNG资源。打包时也保留一份 `CornerTodo.app/Contents/Resources/CornerTodo.png`，原始ICO保留在源码包中；本轮没有生成或声明Finder专用的ICNS图标。

## 内测前核对

1. 检查PACKAGE-REPORT和构建日志，确认共享测试与两种架构打包均成功。
2. 检查manifest中的Mac实机标记保持实际结果；未验证时必须为false。
3. 确认包中不含个人Data、日志、本地离线源路径、开发缓存或Windows发布包。
4. 告知测试者系统范围、芯片选择、自包含运行环境，以及未Developer ID签名、未Apple公证、尚待真机验收。
5. 先按TESTING.md小范围测试，收集启动、桌面层级、时间选择、贴边、全屏和退出反馈。

静态Mach-O代码页哈希通过，不代表Developer ID签名、Apple信任、Gatekeeper、资源封装签名或Mac启动通过。不要把Windows辅助测试写成Mac实机验收，不提供修改系统全局安全设置的处理脚本。

## GitHub 分发

获得维护者明确发布授权后，可在现有仓库新增Mac内测Release，例如标签`macos-v0.1.0-alpha.1`，勾选pre-release，并避免将其设为替代Windows正式版的latest。正文需写明实验性质、系统版本和测试范围，再上传两个架构ZIP、Mac源码ZIP与SHA256SUMS。

本地构建脚本不会创建仓库、上传文件或发布Release。尚未完成Mac用户验收时，不应称为Mac正式稳定版。

