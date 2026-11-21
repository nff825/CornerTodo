# 在 GitHub 网页更新并发布 CornerTodo v1.10.0

本次为 Windows 文字大小调节更新。保留 v1.9.0 及更早的 Windows Release、Mac 内测 Release 及其标签和附件。

## 1. 准备本地文件

使用 `release/v1.10.0/` 内同一次打包的文件，先核对打包报告与文件摘要。重新打包时，从 `VERSION` 为 `1.10.0` 的工程执行：

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.10.0
```

展开目录 `CornerTodo-v1.10.0-source/` 用于上传可浏览源码，`CornerTodo-v1.10.0-source.zip` 用于源码下载，`CornerTodo-v1.10.0-windows-portable.zip` 用于直接运行 Windows 程序。另有 `SHA256SUMS.txt` 和 `PACKAGE-REPORT.txt`。源码与程序包不包含个人 Data、日志、缓存或 AI 示意图。

## 2. 更新仓库源码

1. 原仓库Code页选择main，保持在仓库根目录。
2. Add file → Upload files。
3. 拖入展开源码目录这一层的文件与文件夹；保持src、build、docs、tests、macOS及根文件结构，不额外套外层源码目录。
4. 网页一次最多100文件，文件夹内部也计数。按顶层目录分批，每批先点击Commit changes，再回根目录开启新上传页；同页连续拖入仍会累加。文件数以本次打包报告为准。
5. 根目录.gitignore和.gitattributes也要更新。遗漏时单独上传，或在根目录编辑／创建同名文件，复制本版内容。
6. 提交标题可用 `Update v1.10.0 source (1/2)` 等，批次按实际调整。
7. 核对 `VERSION` 为 `1.10.0`、README 图片显示正常，确认外观设置、字号保存、主面板和截止日历的文字显示及相关测试已提交，再创建标签。

不要上传工作目录中的dist、output、release、archive或真实Data。仅上传ZIP到Code不会更新可浏览的源码。

参考：[GitHub上传说明](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。

## 3. 创建 Release

1. Releases → Draft a new release。
2. 新标签 `v1.10.0`，Target选择已完成全部源码更新的main。
3. 使用 `CornerTodo v1.10.0｜文字大小调节` 作为标题。
4. 正文使用[RELEASE_NOTES.md](RELEASE_NOTES.md)，按实际试用结果填写验证范围。
5. 保留文字大小范围、预览与保存、配色预设保留字号和两种恢复操作的说明，以及 Version 4 与升级备份说明。Mac 内测版本继续为 0.2.0-alpha.1（构建 0.2.1）。

## 4. 附件与发布

上传同一次打包的三个文件：

- CornerTodo-v1.10.0-windows-portable.zip
- CornerTodo-v1.10.0-source.zip
- SHA256SUMS.txt

等待附件上传完成。用独立示例数据试用 90%、100%、150% 的主面板和截止日历，检查长文字换行、日期、已完成事项、临期与逾期层级和滚动操作。核对字号的实时预览、保存重开、取消、恢复100%、配色预设保留字号及底部恢复默认。字号功能尚待原生桌面实测，发布正文的验证范围应按实际结果填写。

尚未完成试用先Save draft。准备正式分发后不勾选pre-release，可设Latest，再Publish release。发布后重新下载程序包确认文件完整和版本，并保留旧Release／标签和原Mac附件。

参考：[管理Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。本地脚本不会上传或发布。
