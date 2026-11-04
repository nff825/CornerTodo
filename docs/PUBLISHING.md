# 在 GitHub 网页更新并发布 CornerTodo v1.8.0

本次为Windows到期提醒更新。保留旧Windows和Mac内测Release、标签及附件，不用新程序覆盖旧标签。

## 1. 准备本地文件

使用 `release/v1.8.0/` 内已完成验证的文件；重新打包时须从VERSION为1.8.0的工程执行：

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.8.0
```

展开目录 `CornerTodo-v1.8.0-source/` 用于上传可浏览源码；`CornerTodo-v1.8.0-source.zip` 用于源码下载；`CornerTodo-v1.8.0-windows-portable.zip` 用于普通用户；另有SHA256SUMS.txt和PACKAGE-REPORT.txt。源码与程序包不包含个人Data、日志、缓存或AI示意图。

## 2. 更新仓库源码

1. 原仓库Code页选择main，保持在仓库根目录。
2. Add file → Upload files。
3. 拖入展开源码目录这一层的文件与文件夹；保持src、build、docs、tests、macOS及根文件结构，不额外套外层源码目录。
4. 网页一次最多100文件，文件夹内部也计数。按顶层目录分批，每批先点击Commit changes，再回根目录开启新上传页；同页连续拖入仍会累加。文件数以本次打包报告为准。
5. 根目录.gitignore和.gitattributes也要更新。遗漏时单独上传，或在根目录编辑／创建同名文件，复制本版内容。
6. 提交标题可用 `Update v1.8.0 source (1/2)` 等，批次按实际调整。
7. 核对VERSION=1.8.0、README实际控件图片正常、ReminderRules及调度／卡片／编辑器源码和测试完整，Mac引用同步，再创建标签。

不要上传工作目录中的dist、output、release、archive或真实Data。仅上传ZIP到Code不会更新可浏览的源码。

参考：[GitHub上传说明](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)。

## 3. 创建 Release

1. Releases → Draft a new release。
2. 新标签 `v1.8.0`，Target选择已完成全部源码更新的main。
3. 标题：`CornerTodo v1.8.0｜按需到期提醒`。
4. 正文使用[RELEASE_NOTES.md](RELEASE_NOTES.md)，按实际试用结果填写验证范围。
5. 明确：默认关闭、勾选展开、天时分组合一次提醒；需具体截止时刻且程序保持运行，无声音或系统后台通知；Mac本次仅数据／源码兼容。
6. 保留Version4及升级备份说明，旧1.7及更早程序不能读新版数据。

## 4. 附件与发布

上传同一次打包的三个文件：

- CornerTodo-v1.8.0-windows-portable.zip
- CornerTodo-v1.8.0-source.zip
- SHA256SUMS.txt

等待附件完成。用独立示例数据检查：默认折叠、点选／输入天时分、全0到期、卡片出现时焦点、查看／完成／关闭、同时多项、重启不重复、改期撤销、重复跨日前提醒。

尚未完成试用先Save draft。准备正式分发后不勾选pre-release，可设Latest，再Publish release。发布后重新下载程序包确认文件完整和版本，并保留旧Release／标签和原Mac附件。

参考：[管理Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)。本地脚本不会上传或发布。
