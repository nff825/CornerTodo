# 在 GitHub 网页更新并发布 CornerTodo v1.4.0

本说明用于将 v1.3.0 源码更新到 v1.4.0。沿用现有仓库和目录结构，先更新源码，再创建新标签并上传发布附件。本文和本地打包脚本都不会替你操作 GitHub。

## 1. 准备本地发布文件

完成开发、自动测试及独立测试版试用后，在项目根目录运行：

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.4.0
```

脚本默认按版本输出到 `release\v版本号`，本版为 `release\v1.4.0`；`-OutputDirectory` 仍可显式指定其他项目内目录。发布文件使用独立版本目录，保留 v1.3.0 及更早的文件。核对本次打包结果和 `PACKAGE-REPORT.txt`，确认文件均来自同一次最新打包：

- `release\v1.4.0\CornerTodo-v1.4.0-source`：可上传的干净源码目录。
- `release\v1.4.0\CornerTodo-v1.4.0-source.zip`：源码下载包。
- `release\v1.4.0\CornerTodo-v1.4.0-windows-portable.zip`：普通用户下载的便携版。
- `release\v1.4.0\SHA256SUMS.txt`：两个压缩包的 SHA-256 摘要。

不要从日常开发目录直接全选上传。真实 `Data`、`todos.json`、日志、临时验证数据、`dist`、`output` 和 `archive` 应留在本机。重复打包前一批产物保存在 `archive/packages/v版本号/previous-bundles-*`，它们用于恢复本地发布文件；上传时始终选择 `release\v1.4.0` 内的最新产物。

## 2. 更新仓库源码

1. 打开现有仓库的 **Code** 页面，确认位于默认分支（通常是 `main`）和仓库根目录。
2. 点击 **Add file → Upload files**。
3. 打开本地 `release\v1.4.0\CornerTodo-v1.4.0-source`，将这一层的文件与文件夹拖入上传区域。不要把外层 `CornerTodo-v1.4.0-source` 文件夹整体套到仓库根目录下，也不要把源码 ZIP 当作源码更新。
4. 检查上传包含 `src`、`build`、`docs`、`tests` 及根目录工程和文档。包括新增的 `tests/EdgeUiTests.cs`、更新的时刻选择／面板／数据回归测试、`CornerTodo.Build.props`、原六张展示图片，以及新增的 `docs/images/time-picker.png`、`edge-expanded.png`、`edge-capsule.png`。README、更新的编辑器图和三张新功能图应一同上传；设计阶段的 AI 效果图不作为实机展示上传。
5. 提交标题可填写 `Release v1.4.0: time picker and edge collapse`。扩展说明可填写“新增小时／分钟点选和默认关闭的右侧贴边自动收起，保留旧数据与自动扩大规则。”
6. 若默认分支允许直接提交，选择 **Commit directly to the main branch**，点击 **Commit changes**。仓库若要求拉取请求，则按仓库规则提交分支并合并后继续。
7. 回到 Code 页面，确认源码已经更新，`VERSION` 为 `1.4.0`，README 正常显示，根目录仍是工程入口与文档，生产源码仍位于 `src` 的四个子目录。

本次沿用 v1.3.0 的目录结构，不需要删除现有生产源码文件。完整源码直接更新到原位置；共享清单、测试更新和新增图片必须一起上传。文件数量以本次公开清单及打包报告为准，不照抄上一版本。

网页上传操作可对照 [GitHub 官方：向仓库添加文件](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。若一次上传受限，可分批提交，但所有源码与文档更新完成后再创建版本标签。

## 3. 创建新版本草稿

1. 打开仓库的 **Releases**，点击 **Draft a new release**。
2. 在 **Choose a tag** 中输入 `v1.4.0`，选择创建新标签；**Target** 选择完成上述更新的 `main` 分支。不要修改或覆盖已发布的 `v1.3.0` 标签。
3. Release 标题填写 `CornerTodo v1.4.0｜时刻点选与贴边收起`。
4. 版本说明可复制 [RELEASE_NOTES.md](RELEASE_NOTES.md)；根据本次实际测试结果填写验证段落。不要沿用上一版本的测试数量或试用结论。
5. 可将上一个标签选为 `v1.3.0`，便于比较本次变化；自动生成的提交记录不能代替用户看得懂的功能与升级说明。
6. 暂存时选择 **Save draft**，核对完成前保持草稿状态。

版本说明至少保留以下内容：

- 小时 00–23、分钟 00–59 分别点选，也可键盘输入，保留任意分钟精度。
- 「贴边自动收起」默认关闭且保存在本机；首版支持主屏右侧，离开约 800 毫秒收起，点击展开，编辑或菜单打开时不收起。
- 胶囊显示未完成总数及其中逾期数；收起期间不会因到期自动弹出，展开时恢复当前紧急程度对应的面板大小，仍只在桌面显示。
- 备注、优先级、排序、细时间条、外观设置和默认关闭的登录自启动继续保留。
- 升级前备份 `Data`；旧数据自动采用新字段默认值。回退旧版应恢复升级前备份，避免旧版写回丢失新字段。
- 普通用户下载 `windows-portable.zip`，完整解压后运行；系统要求及代码未签名说明。

## 4. 添加附件并发布

1. 在草稿的附件区域上传以下三个文件，全部来自本地 `release\v1.4.0` 的最新产物：

   - `CornerTodo-v1.4.0-windows-portable.zip`
   - `CornerTodo-v1.4.0-source.zip`
   - `SHA256SUMS.txt`

2. 等待三个文件全部上传完成，检查文件名和版本号。不要上传 `Data`、整个开发目录或 `previous-bundles-*` 内的备份。
3. 如果这是通过试用后准备公开的正式版本，不勾选预发布选项；如页面提供 **Set as the latest release**，将本次正式版设为最新。
4. 检查标签、目标提交、正文和附件都一致后，点击 **Publish release**。保留原有 v1.3.0 及更早的 Release 和标签；发布新版本不需要删除旧版本。

详细操作可对照 [GitHub 官方：管理仓库中的 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。

## 5. 发布后核对

打开公开 Release 页面，确认 `v1.4.0` 标签、版本说明和三个附件均正确。GitHub 自动附带的 **Source code (zip / tar.gz)** 是标签源码快照；普通使用者应下载你上传的 `CornerTodo-v1.4.0-windows-portable.zip`。

重新下载便携包，在新文件夹完整解压并检查首次运行没有你的个人事项。验证升级时使用合成数据或备份副本，不让不同版本同时写入正式 `Data`。

可用 Windows PowerShell 核对下载文件：

```powershell
Get-FileHash -Algorithm SHA256 .\CornerTodo-v1.4.0-windows-portable.zip
```

将输出的 `Hash` 与同次发布的 `SHA256SUMS.txt` 对应行比较。摘要用于确认文件一致性，本身不代表安全保证或所有环境兼容。

后续修改仓库代码不会自动替换已发布附件。需要修订程序时，应重新测试并准备新的版本号和发布包。
