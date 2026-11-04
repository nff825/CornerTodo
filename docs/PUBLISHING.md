# 在 GitHub 网页更新并发布 CornerTodo v1.7.0

本次发布 Windows「视觉统一与可选悬浮置顶」。保留 v1.6.0 及更早 Windows Release、标签和 Mac 原内测附件，不把新程序替换到旧标签下。

## 1. 准备本地文件

已有本版发布文件时直接使用 `release/v1.7.0/`。需要重新打包时，从版本已更新为 1.7.0 的工程根目录执行：

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.7.0
```

产物为：

- `CornerTodo-v1.7.0-source/`：上传 GitHub 的干净源码展开目录。
- `CornerTodo-v1.7.0-source.zip`：源码下载包。
- `CornerTodo-v1.7.0-windows-portable.zip`：普通用户的 Windows 程序包。
- `SHA256SUMS.txt`、`PACKAGE-REPORT.txt`。

源码按显式白名单复制并从干净副本构建，包含 Mac 共享工程和许可，排除个人 Data、日志、bin/obj、缓存和 AI 效果图。日常 `dist` 与发布文件分开；升级日常程序需另行退出、备份和替换。

## 2. 分批上传源码

1. 打开现有仓库的 Code 页，选择 `main`，保持在仓库根目录。
2. 点击 Add file → Upload files。
3. 打开 `release/v1.7.0/CornerTodo-v1.7.0-source/`，拖入这一层的文件和文件夹，保持 `src`、`build`、`docs`、`tests`、`macOS` 及根目录工程文件的结构。不要额外套一层外部源码目录。
4. 网页单次最多上传 100 个文件，文件夹内文件也计数。可按顶层目录分批；每批等待上传完成并点击 Commit changes，随后回到仓库根目录重新开启下一次上传。在同一上传页连续拖入几批仍会累计数量。实际数量以本次 `PACKAGE-REPORT.txt` 为准。
5. 根目录 `.gitignore` 和 `.gitattributes` 也要更新；若遗漏可单独上传，或在网页编辑同名文件。未存在时在仓库根目录 Add file → Create new file，填写完整文件名并复制本版内容。
6. 分批提交标题可用 `Update v1.7.0 source (1/2)`、`Update v1.7.0 source (2/2)`，批次数按实际调整。个人仓库沿用直接提交到 `main` 的流程。
7. 全部完成后，确认根目录 `VERSION` 为 `1.7.0`、README 新图片和 Mac 文档链接可用，再创建标签。仅上传压缩包不能替代更新仓库可浏览的源码。

不要上传工作区中的 `dist`、`output`、`release`、`archive` 或真实 `Data`。Mac 工程引用同一任务模型，需要与共享设置一起更新；本次不发布新 Mac 二进制。

网页参考：[GitHub 官方上传说明](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。

## 3. 创建 Windows Release

1. Releases → Draft a new release。
2. 创建标签 `v1.7.0`，Target 选择已完成全部源码更新的 `main`。
3. 标题：`CornerTodo v1.7.0｜视觉统一与可选悬浮置顶`。
4. 正文使用 [RELEASE_NOTES.md](RELEASE_NOTES.md)，按实际试用结果填写验证情况。
5. 保留说明：悬浮默认关闭，桌面／悬浮自动扩大独立保存；本版没有到期弹窗或声音提醒；Mac 本轮仅源码数据兼容，没有新 Mac 包。
6. 保留数据说明：继续 Version 3，v1.6.0 可读任务但旧版保存会丢新显示偏好；v1.5.0 及更早和已发布 Mac alpha.1 不支持 Version 3，升级前单独备份整个 Data。

## 4. 上传附件并发布

上传同一次打包的三个文件：

- `CornerTodo-v1.7.0-windows-portable.zip`
- `CornerTodo-v1.7.0-source.zip`
- `SHA256SUMS.txt`

等待所有附件完成上传。实机检查桌面／悬浮切换、办公软件上方显示、编辑和菜单、贴边胶囊及重新启动后的设置保留。准备正式分发后，不勾选 pre-release，可将其设为 Latest，再点击 Publish release；尚未完成试用先 Save draft。

发布后重新下载程序包，核对版本、附件完整性与摘要，检查 README 图片。旧 Release、标签和 Mac 内测附件继续保留。打包脚本不会创建标签、上传文件或替用户发布。

官方参考：[管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。
