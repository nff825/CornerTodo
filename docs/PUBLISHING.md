# 在 GitHub 网页更新并发布 CornerTodo v1.12.0

本次为 Windows「关联原文件」更新，数据格式升级为 Version 6。保留 v1.11.0 及更早的 Windows Release、Mac 内测 Release 及其标签和附件。本文件为操作教程，当前本地准备不会自动访问或上传 GitHub。

## 1. 准备本地文件

使用 `release/v1.12.0/` 内同一次打包的文件，先核对打包报告与摘要。从 `VERSION` 为 `1.12.0` 的工程执行：

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.12.0
```

展开目录 `CornerTodo-v1.12.0-source/` 用于上传可浏览源码，`CornerTodo-v1.12.0-source.zip` 用于源码下载，`CornerTodo-v1.12.0-windows-portable.zip` 用于直接运行 Windows 程序。另有 `SHA256SUMS.txt` 和 `PACKAGE-REPORT.txt`。包内不包含个人 Data、日志、缓存、关联原文件或 AI 示意图。

## 2. 更新仓库源码

1. 在原仓库 Code 页选择 main，保持在仓库根目录。
2. Add file → Upload files。
3. 拖入展开源码目录这一层的文件与文件夹，保留 src、build、docs、tests、macOS 及根文件结构，不额外套外层目录。
4. 本版干净源码共 188 个文件，分两批：第一批 `src`、`tests`、`build`，共 90 个文件；第二批 `docs`、`macOS` 和根目录全部 13 个文件，共 98 个文件。网页一次最多 100 个文件，子文件夹内部也计数。每批先 Commit changes，再回根目录开启新上传页；同页连续拖入仍会累加。重新打包后的总数以 `PACKAGE-REPORT.txt` 为准。
5. 根目录 `.gitignore` 和 `.gitattributes` 也要更新，遗漏时单独上传或在根目录编辑同名文件。
6. 提交标题可用 `Update v1.12.0 source (1/2)`，批次按实际调整。
7. 核对 `VERSION` 为 `1.12.0`，确认原文件关联、Version 6 保存与旧数据迁移、相关测试和 Mac 字段保留已提交。README 的两张新图应为 `docs/images/file-links-editor.png` 与 `file-links-popup.png`。

不要上传工作目录中的 dist、output、release、archive、个人 Data 或关联原文件。只把 ZIP 上传到 Code 不会更新可浏览源码。

参考：[GitHub 上传说明](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。

## 3. 创建 Release

1. Releases → Draft a new release。
2. 新标签 `v1.12.0`，Target 选择已完成全部源码更新的 main。
3. 标题使用 `CornerTodo v1.12.0｜关联原文件`。
4. 正文使用 [RELEASE_NOTES.md](RELEASE_NOTES.md)，验证范围按实际结果填写。
5. 保留升级前另存整个 `Data`、Windows v1.11.0 及更早和已发 Mac 无法读取 Version 6、回退先恢复独立备份的说明。
6. 明确 `Data` 只备份路径，不包含原件；换电脑或移动原文件后需要保管原件并重新关联。
7. Mac 已分发版继续为 0.2.0-alpha.1（构建 0.2.1），保存 Version 4。本次仅同步开发源码的 Version 6 字段保留，没有新 Mac 界面或程序。

## 4. 附件与发布

上传同一次打包的三个文件：

- `CornerTodo-v1.12.0-windows-portable.zip`
- `CornerTodo-v1.12.0-source.zip`
- `SHA256SUMS.txt`

等待附件上传完成。用独立的示例事项和测试文件检查多文件选择、取消不保存、清单／截止日历／四象限入口、打开与定位、移动原件后的重新关联、移除和删除事项不影响原件。检查重复规则继承相同原路径、改单次不改规则或其他期次，旧数据升级保存与重开正常。离屏检查不代替系统文件选择器、默认程序启动和资源管理器的原生操作验收。

尚未完成试用先 Save draft。准备正式分发后不勾选 pre-release，可设 Latest，再 Publish release。发布后重新下载程序包确认完整性和版本，保留旧 Release、标签与 Mac 附件。

参考：[管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。
