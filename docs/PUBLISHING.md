# 在 GitHub 网页发布 CornerTodo v1.2.1

本说明供不使用 Git 命令的维护者操作。准备好本地文件后，再由你登录 GitHub 完成上传和发布。本文本身不会创建仓库或上传文件。已有仓库时，直接从更新源码的步骤开始，无需重新创建仓库。

## 先区分仓库与 Release

| 位置 | 放什么 | 访问者如何使用 |
| --- | --- | --- |
| 仓库的 Code 页面 | 解压后的源码、README、LICENSE、docs 和测试脚本 | 在线阅读、提出问题、下载源码或自行构建 |
| 仓库的 Releases 页面 | 某个版本的介绍与可下载附件 | 下载已经构建好的便携版，解压后运行 |

将一个 ZIP 上传到 Code 页面只会保存该压缩文件，不会自动展开其中的项目，也不会产生 Release。先上传源码，再给这份源码创建版本标签并附上便携版。

## 1. 准备本地文件

在项目根目录运行下面的命令，将本版文件输出到独立的 `release\v1.2.1` 文件夹，保留旧版本的发布文件：

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.2.1 -OutputDirectory release\v1.2.1
```

打包脚本会在该目录中生成以下三个下载文件：

- `CornerTodo-v1.2.1-source.zip`：整理过的项目源码。
- `CornerTodo-v1.2.1-windows-portable.zip`：可直接运行的便携版。
- `SHA256SUMS.txt`：两个压缩包的 SHA-256 摘要。

脚本还会保留 `release\v1.2.1\CornerTodo-v1.2.1-source` 干净源码目录，可直接使用；也可以将 **source.zip 解压到一个新文件夹**。打开源码内容，找到包含 `README.md`、`LICENSE`、`Build.ps1`、`CornerTodo.sln`、`CornerTodo.csproj`、`CornerTodo.Build.props`、`src`、`build`、`docs` 和 `tests` 的那一层；这层内容将上传到仓库根目录。生产源码位于 `src/App`、`src/Tasks`、`src/UI`、`src/Windows`，根目录保留工程与构建入口。保留 `docs\images\normal.png`，README 会引用它。

v1.2.1 尚未发布；本次目录整理后，重新生成同一 `release\v1.2.1` 目录中的文件，再用这一版干净源码和附件准备发布。旧版发布目录继续保留。

使用整理好的源码目录作为上传来源，避免从日常开发文件夹直接全选。你的 `Data` 文件夹、真实 `todos.json`、日志和临时验证数据应留在本机。便携版也应保持空白数据状态，让下载者首次运行时创建自己的事项。

## 2. 创建公开仓库

1. 登录 GitHub，在右上角「+」菜单选择 **New repository**。
2. 仓库名填写 `CornerTodo`，描述可填写「Windows 桌面右上角的本地半透明待办工具」。
3. 可见性选择 **Public**。公开仓库中的内容会对所有人可见。
4. 新仓库初始化选项中的 README、`.gitignore` 和 License 都先不勾选；准备好的源码包已包含对应文件。
5. 点击 **Create repository**。

详细操作可对照 [GitHub 官方：创建新仓库](https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-new-repository)。

## 3. 上传解压后的源码

1. 在空仓库页面点击 **uploading an existing file**；若已有内容，可使用 **Add file → Upload files**。
2. 将步骤 1 中那一层的文件和文件夹拖入上传区域。上传的是解压后的内容，仓库根目录应直接出现 `README.md`，不要多套一层 `CornerTodo-source` 文件夹。
3. 检查列表包含 `LICENSE`、`.gitignore`、`src` 及其四个子目录、工程文件、`build`、`docs/images/normal.png` 及 `tests`；检查没有真实待办数据、日志或临时程序输出。
4. 提交说明填写 `Prepare CornerTodo v1.2.1`，提交到默认分支（通常为 `main`），点击 **Commit changes**。
5. 已有仓库还需按下方说明删除原根目录的旧生产源码路径，并提交删除。上传新 `src` 和清理旧路径都应在创建 `v1.2.1` 标签之前完成。
6. 回到 Code 页面，确认根目录保留工程、配置和构建入口，生产 `.cs` 文件均在 `src` 中，`tests` 保留；README、截图和许可证正常显示。

GitHub 网页支持拖入文件和文件夹；当前限制为每次最多 100 个文件、单个文件不超过 25 MiB。本项目整理后的源码包适合使用网页上传。界面和限制如有变化，以 [GitHub 官方：向仓库添加文件](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository) 为准。

### 更新已有仓库时清理旧源码路径

网页上传新文件不会自动删除原路径。先提交完整的 `src`、共享清单及其他更新文件，再在 Code 页面逐个打开旧根目录文件，选择删除文件并提交删除。只清理下列旧根目录路径中实际存在的文件：

```text
ApplicationComposition.cs
ApplicationController.cs
ApplicationInteraction.cs
AssemblyInfo.cs
Diagnostics.cs
LaunchOptions.cs
Program.cs
SingleInstanceSession.cs
ITodoStore.cs
Models.cs
TaskDraft.cs
TodoSession.cs
TodoStateCopy.cs
TodoStore.cs
AppearanceEditor.cs
AppearanceSettings.cs
GrowthLayout.cs
MainWindow.cs
PanelController.cs
TaskEditor.cs
TaskListView.cs
Theme.cs
TodoPanelView.cs
DesktopAttachmentController.cs
DesktopHost.cs
StartupManager.cs
```

这里列的是仓库根目录的旧生产文件；保留 `src` 中的新路径，也保留 `tests` 下的测试文件，即使文件名相近或相同。整理后的生产编译清单见根目录 [CornerTodo.Build.props](../CornerTodo.Build.props)。完成新路径上传与旧路径清理后，再创建指向最终提交的版本标签。

## 4. 创建 v1.2.1 Release

1. 在仓库主页打开 **Releases**，点击 **Draft a new release** 或创建首个 Release 的入口。
2. 在 **Choose a tag** 中输入 `v1.2.1`，选择创建这个新标签；**Target** 选择已完成新路径上传和旧路径清理的 `main` 分支。版本标签用于固定这次发布对应的源码。
3. Release 标题填写 `CornerTodo v1.2.1`。
4. 填写版本说明，可使用下方草稿，并根据实际验证情况调整。
5. 在 **Attach binaries** 附件区域上传本地 `release\v1.2.1` 中的三个文件：便携版 ZIP、源码 ZIP 和 `SHA256SUMS.txt`。
6. 想先检查页面可选择 **Save draft**。确认文件名、标签和说明正确后，点击 **Publish release** 才会公开这个版本。

详细步骤见 [GitHub 官方：管理仓库中的 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。

可使用的版本说明：

```markdown
CornerTodo v1.2.1：工程结构整理与窗口恢复稳定性改善。

- 统一工程和构建入口，源码、引用、测试与打包使用共享清单。
- 生产源码按职责归入 src/App、src/Tasks、src/UI、src/Windows，保持单个应用程序集和 CornerTodo 命名空间。
- 拆分任务操作、显示交互、单实例与启动生命周期，便于维护。
- 修复延迟恢复使用旧快照的问题，等待编辑或模态操作结束后再读取最新状态。
- 改善退出后的恢复计时、排队挂接与旧窗口回调清理。
- 现有功能与 JSON 数据格式保持兼容，登录自启动仍默认关闭、由用户自行开启。

已通过11组功能测试和140项工程构建／打包检查；维护者反馈独立测试版在本机试用正常。
不同Windows环境、真实登录、Windows外部禁用及Explorer重启仍需进一步回归。

原目录升级保留已有自启登记。移动目录后，手动打开新位置程序并更新自启位置；删除前先关闭自启。
若在 Windows 中禁用了启动项，请在系统设置中重新启用；菜单登记状态不代表系统已允许启动。

下载 CornerTodo-v1.2.1-windows-portable.zip，完整解压后运行 CornerTodo.exe。
目标系统：Windows 10 / 11，.NET Framework 4.7.2 或更新版本。
尚未保证所有桌面工具、多显示器和 DPI 配置的兼容性。

本版本未进行代码签名，Windows 或 SmartScreen 可能显示未知发布者等信任提示。
请核对下载来源和 SHA256SUMS.txt，保持系统防护开启；文件摘要不能代替安全审查。

源码与许可见仓库，采用 MIT 许可证。
```

GitHub 发布后还会自动提供 **Source code (zip / tar.gz)**，它们是标签的源码快照。给普通使用者的下载指引应明确指向你上传的 `windows-portable.zip`。

## 5. 发布后核对

打开公开的仓库和 Release 页面，确认 README 截图可见、`v1.2.1` 标签正确、三个附件均可下载。可重新下载便携版，在新文件夹中完整解压，检查首次运行没有你自己的事项。

下载者可在 Windows PowerShell 中计算压缩包的摘要：

```powershell
Get-FileHash -Algorithm SHA256 .\CornerTodo-v1.2.1-windows-portable.zip
```

将输出的 `Hash` 与 `SHA256SUMS.txt` 中对应文件的一行比较，字母大小写不影响结果。摘要用于确认文件一致性，不保证程序在所有环境下兼容或安全。遇到 Windows 信任提示时，可先停止运行并核查来源与源码；无需关闭系统防护。

对外分享时，仓库地址用于介绍项目和提供源码，Release 页面用于下载版本。后续代码更新不会自动替换已发布附件；修订程序应准备新的版本号和发布包。
