# CornerTodo macOS v0.2.0-alpha.1 · 构建与内测分发

本文保留已分发 **Mac 0.2.0-alpha.1 实验性内测版**的构建与分发说明，构建号 **0.2.1**，数据格式为 **Version 4**。该批交付范围为无截止日期、数据兼容、视觉统一、版本反馈和迁移前备份。

Windows v1.12.0 对应的当前共享开发源码已采用 Version 6，已分发 Mac 内测程序无法读取这一格式。当前 Mac 源码保留原文件路径、重要标记等共享字段，编辑已有事项保留原关联；本次没有新 Mac 程序、关联文件操作或四象限界面。重现原内测包请使用相应发布源码快照，原版本的附件和标签继续保留。

## 构建

从项目根目录执行：

```powershell
.\macOS\Build-Mac.ps1 -Architecture all -DotNetPath $dotnetPath -PythonPath $pythonPath
```

SDK／Python 参数填写完整可执行文件路径；`NuGetConfigPath` 可指定已有离线源，`ToolingDirectory` 必须在本项目 `output` 内。默认输出：

```text
release/macos/v0.2.0-alpha.1/
```

脚本在干净源码副本中测试、发布两架构自包含运行时、构造.app、检查清单与逐文件摘要。现有输出拒绝覆盖，失败不替换已发布文件。静态 Mach-O／执行权限检查不等于 Mac 启动、Developer ID 签名、Gatekeeper 或 Apple 公证通过。

版本来源需一致：`macOS/VERSION`、两个工程的版本、Program.Version；Program.BuildNumber、Desktop FileVersion 和 Info.plist 的内部构建号对应 **0.2.1**。独立构建号随测试版本递增，避免不同 alpha 在 Finder 中无法区分；不要复用其他版本的构建号。

## 附件

- `CornerTodo-macos-v0.2.0-alpha.1-arm64-experimental.zip`：M1等Apple芯片Mac用户。
- `CornerTodo-macos-v0.2.0-alpha.1-x64-experimental.zip`：Intel Mac用户。
- `CornerTodo-macos-v0.2.0-alpha.1-source.zip`：维护者源码，普通用户不需要。
- `SHA256SUMS.txt`：三个压缩包的摘要。
- `PACKAGE-REPORT.txt`、各架构manifest和单独摘要：维护者验证记录。

每个程序ZIP包含 CornerTodo.app 和测试说明。应用内带运行时、许可证与第三方声明，不带个人Data。PNG资源不声明为ICNS原生图标。

## 内测交付前核对

1. 检查两架构打包、共享核心测试、Mac迁移备份测试、辅助界面检查与包校验。
2. 检查 manifest 中版本／构建号／架构正确，原生Mac启动、Developer ID与公证状态保持真实结果。
3. 确认源码来自同一次干净构建，无本地缓存、测试数据、私人路径配置或Windows程序包。
4. 核对数据格式与回退说明。已分发 0.2.0-alpha.1 使用 Version 4，旧 0.1.0-alpha.1 无法读取；当前开发源码保存 Version 6 后，Windows v1.11.0 及更早版本和所有已分发 Mac 内测程序均无法读取。回退前恢复升级前另存的完整数据目录；关联原文件不在备份中，需要单独保管。
5. 按 [TESTING.md](TESTING.md) 找Mac用户试用。先拿Apple芯片结果，Intel另行标注实测状态。Windows辅助渲染不能算Mac真机验收。

## GitHub 内测 Release

维护者完成上传时使用：

- 标签：`macos-v0.2.0-alpha.1`
- 标题：`CornerTodo macOS v0.2.0-alpha.1｜无截止日期与视觉优化内测`
- 正文：[RELEASE_NOTES.md](RELEASE_NOTES.md)
- 勾选 **This is a pre-release**，保持Windows正式版为Latest。
- 上传两个架构程序ZIP、Mac源码ZIP和SHA256SUMS，等待附件完成后核对。
- 保留原 `macos-v0.1.0-alpha.1` 的标签和附件。

本地构建不会上传或创建GitHub Release。小范围测试者只需要对应芯片的程序ZIP、测试说明和反馈模板。完整Mac源码包不替代仓库内Windows完整工程，不将Mac精简源码目录覆盖仓库根目录。

## 后续

收集首次启动、保存、找回、退出和窗口行为结果后再扩展功能。面向更多用户分发时，另行安排Mac环境中的Developer ID签名与Apple公证。
