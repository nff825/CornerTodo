# 在 GitHub 网页更新并发布 CornerTodo v1.6.0

本次发布 Windows 重复任务功能。保留 v1.5.0及更早 Windows Release、标签和 Mac 原内测附件；不把新 Windows 包替换到旧标签下。

## 1. 准备文件

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.6.0
```

产物位于 `release/v1.6.0/`：

- `CornerTodo-v1.6.0-source/`：可上传的干净源码目录。
- `CornerTodo-v1.6.0-source.zip`：源码下载包。
- `CornerTodo-v1.6.0-windows-portable.zip`：普通用户的程序包。
- `SHA256SUMS.txt`、`PACKAGE-REPORT.txt`。

源码按显式白名单复制并从干净副本构建，包含Mac共享工程与许可证，排除个人Data、日志、bin/obj、缓存和AI效果图。日常dist程序与发布包分开，升级前须另存整个Data备份。

## 2. 上传源码

1. 打开原仓库 Code 页，选择main，位于根目录。
2. Add file → Upload files。
3. 进入干净源码目录，拖入其中的src、build、docs、tests、macOS及根目录工程文件，不把外层源码目录整体套进去。
4. 包含新增RecurringTasks.cs、RecurringTasksWindow.cs、RecurrenceTests.cs、RecurringUiTests.cs及测试入口、更新的共享清单和4张实际WPF展示图。Mac工程链接同一任务模型，需要一起上传。
5. 超过网页单次文件限制时按顶层目录分批提交，全部上传完成后再建标签。确认.gitignore/.gitattributes也成功上传；可单独上传或网页编辑。
6. 提交标题可填写 `Release v1.6.0: recurring tasks`。
7. 确认VERSION为1.6.0，README图片及Mac文档链接可用，文件数量以本次打包报告为准。

网页参考：[GitHub官方上传说明](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。不上传dist/output/release/archive或真实Data。

## 3. 创建Release

1. Releases → Draft a new release。
2. 创建标签 `v1.6.0`，Target选择完成全部更新的main。
3. 标题：`CornerTodo v1.6.0｜每日、每周与每月重复任务`。
4. 正文使用 [RELEASE_NOTES.md](RELEASE_NOTES.md)，按实际试用结果填写验证情况。
5. 保留升级说明：Version 3；旧v1/v2只读载入迁移；旧程序不支持v3；回退前先恢复升级前整个Data备份。
6. 说明重复默认关闭，周期生成在程序运行/重新打开时执行，没有定时通知或声音提醒；Mac本次只有源码数据兼容，没有发布新Mac程序。

## 4. 附件与发布

上传同一次打包的三个文件：

- `CornerTodo-v1.6.0-windows-portable.zip`
- `CornerTodo-v1.6.0-source.zip`
- `SHA256SUMS.txt`

等待附件全部完成上传。实机试用通过后发布正式Windows版，可设为Latest；尚未完成试用可先Save draft。核对标签、目标提交、正文与附件后Publish release，旧版本继续保留。

发布后下载程序包确认文件完整，检查README展示与附件名称。Mac原alpha.1保持Pre-release状态，不能读取v3数据。

官方参考：[管理Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。本地脚本不创建标签或上传文件。
