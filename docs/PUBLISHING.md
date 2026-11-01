# 在 GitHub 网页更新并发布 CornerTodo v1.5.0

本次为 Windows 正式版更新：无截止日期与简约滚动条。Mac 开发源码同步兼容，但不发布新 Mac 内测包；保留 Windows v1.4.0 与 Mac 原内测版的 Release、标签和附件。

## 1. 准备本地文件

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.5.0
```

产物位于 `release/v1.5.0/`：

- `CornerTodo-v1.5.0-source/`：可上传的干净源码展开目录。
- `CornerTodo-v1.5.0-source.zip`：源码下载包。
- `CornerTodo-v1.5.0-windows-portable.zip`：普通 Windows 用户下载的程序。
- `SHA256SUMS.txt`、`PACKAGE-REPORT.txt`：摘要与本次打包验证报告。

源码通过显式清单复制并从干净副本验证，包含 Windows 工程和 Mac 工程所需源码／许可，排除个人 Data、日志、缓存、bin/obj、旧发布包和 AI 效果图。日常 `dist` 与发布目录分开，升级日常程序需单独退出、备份和替换程序文件。

## 2. 更新仓库源码

1. 打开原仓库 Code 页，选择 `main`，位于仓库根目录。
2. 点击 Add file → Upload files。
3. 进入 `release/v1.5.0/CornerTodo-v1.5.0-source/`，拖入这一层的文件与文件夹，保留 `src`、`build`、`docs`、`tests`、`macOS` 及根目录工程文件的结构；不套一层外部源码目录。
4. 本次源码同时更新 Mac 接线，因为它链接同一套任务规则。所有文件均完成上传再创建标签；若网页提示一次文件数量限制，可按顶层文件夹分批提交。只分批，不删掉原仓库整体重建。
5. 确认隐藏的 `.gitignore` 和 `.gitattributes` 也上传成功；可单独上传，或在网页编辑现有文件。`.gitignore` 保留 `/macOS/artifacts/`、`[Bb]in/`、`[Oo]bj/` 等排除项。
6. 提交标题可填写 `Release v1.5.0: optional deadlines and scrollbar`。
7. 回到 Code 页，确认根目录 `VERSION` 为 `1.5.0`、源码和 Mac 接线完整，README 两张无日期新图正常显示。文件数量以本次打包报告为准，不照抄旧版数量。

网页步骤可对照 [GitHub 官方上传说明](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。不要上传整个工作目录中的 dist、output、release、archive 或个人 Data。

## 3. 创建 Windows Release

1. Releases → Draft a new release。
2. 创建标签 `v1.5.0`，Target 选择完成全部源码更新的 `main`。
3. 标题：`CornerTodo v1.5.0｜无截止日期与简约滚动条`。
4. 正文复制 [RELEASE_NOTES.md](RELEASE_NOTES.md)，按实际测试结果补充验证情况。
5. 升级说明必须保留：先退出并单独备份整个 Data；保存升级为 Version 2；旧 Windows 与已发布 Mac 0.1.0-alpha.1 不支持 Version 2；回退先恢复升级前备份，不交替写入。
6. 原 Mac 内测 Release 不改为新 Windows 标签，也不替换其附件。

## 4. 上传附件并发布

在 Release 附件区上传同一次打包的三个文件：

- `CornerTodo-v1.5.0-windows-portable.zip`
- `CornerTodo-v1.5.0-source.zip`
- `SHA256SUMS.txt`

等待全部上传完成。Windows 实机试用通过并准备正式分发后，发布为正式版；如页面提供 Set as latest release，可设为最新 Windows 版。未完成试用可先 Save draft。核对后点击 Publish release，保留旧 Release 和标签。

发布后下载便携包确认版本和文件完整，检查 README 图片、Mac 文档链接及资产名称。Windows 便携包不包含 Mac 程序；Mac 首轮实机内测仍单独进行。

GitHub 官方参考：[管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。本地脚本不会创建标签或上传文件。
