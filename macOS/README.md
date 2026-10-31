# CornerTodo for macOS · 实验性内测版

版本：**0.1.0-alpha.1**。首批目标为 **macOS 14 或更新版本**，提供 Apple 芯片 ARM64 与 Intel x64 两种包。Windows 正式版本仍使用独立的版本号与发布目录。

本工程参考 CornerBreak 的 .NET 10 / Avalonia 工程和打包方式，复用 CornerTodo 的任务、排序、截止时间、时间进度与尺寸计算规则。Mac 的界面、窗口层级、菜单栏及用户数据目录单独实现。

## 验证边界

本版在 Windows 环境交叉构建。**Mac 启动、桌面层级、Spaces、全屏、多屏、睡眠唤醒和真实鼠标交互尚待 Mac 用户验收。** 生成包与通过共享规则测试不等于 Mac 实机可用。

本版没有 Developer ID 发布者签名，也没有通过 Apple 公证。打包器检查 Mach-O 架构与代码页哈希、复制文件一致性、ZIP CRC 和 UNIX 执行权限；这些检查不代表 Apple 信任、Gatekeeper 或 Mac 启动验证。请作为实验性内测包试用，暂不按正式 Mac 版本宣传。

## 下载与启动

| 设备 | 选择文件 |
| --- | --- |
| Apple M 系列芯片 | `CornerTodo-macos-v0.1.0-alpha.1-arm64-experimental.zip` |
| Intel Mac | `CornerTodo-macos-v0.1.0-alpha.1-x64-experimental.zip` |

1. 确认下载来自作者实际发布的地址，可使用随附 `SHA256SUMS.txt` 核对文件一致性。
2. 完整解压后双击 **CornerTodo.app**。不要进入应用包内部单独移动可执行文件。
3. 测试包自带运行环境，无需安装 .NET、SDK、Python 或其他开发工具。
4. 菜单栏入口用于恢复面板和退出；退出前完成正在编辑的内容。

首次启动可能出现开发者身份提示。确认来源后，可参考 [Apple 官方说明](https://support.apple.com/en-us/102445) 决定是否允许此应用运行；如果提示已损坏、会损坏电脑，或启动即退出，请截图反馈设备与版本，不需要关闭系统防护或运行处理隔离属性的脚本。

## 本轮试用重点

- 新增、编辑、删除、完成与恢复事项；截止日期及可选的小时／分钟点选。
- 纯文字备注，高／中／低优先级，截止时间优先／优先级优先排序。
- 约 3 DIP 的时间已用细条，到期满格，已完成事项保留划线并移入完成区域。
- 半透明面板与独立文字颜色、浅色／深色外观；操作后保存或取消。
- 右上角尺寸增长、默认关闭的右侧贴边胶囊；鼠标离开后收起，点击展开，编辑和菜单期间暂停自动变化。
- 面板应能被其他软件遮住，并可通过菜单栏恢复。Mac 桌面层级与全屏行为是本轮重点验收项目。

这些行为的具体结果请按 [TESTING.md](TESTING.md) 记录。自动规则测试不替代 Mac 上的实际操作验收。原型不包含手机端、跨设备同步或 Mac 登录自启。

## 数据与更新

数据与日志放在用户目录：

```text
~/Library/Application Support/CornerTodo/
```

任务文件为 `todos.json`，不写进 `CornerTodo.app` 包内。替换应用前请退出并备份这个目录，替换应用后保留数据目录。

Windows 与 Mac 使用相同的 Version 1 任务结构，兼容备注、优先级、排序与贴边偏好；两平台的窗口位置需要在各自屏幕范围内重新确认。跨平台迁移先使用备份副本验证，不让两个运行实例同时写同一目录。旧版写回会丢失其不认识的字段，回退请使用升级前备份。

Windows 上辅助验证 Mac 前端时使用独立预览目录，不读取正式 Windows `dist/Data`。程序离线保存；当前没有自动跨设备同步。

## 维护者构建

需要 .NET SDK 10.0.400 或后续可兼容功能带（本次验证使用 10.0.403）、PowerShell，以及 Python 3.9+。桌面依赖锁定 Avalonia 11.3.19，自包含运行时固定为 .NET 10.0.14。普通使用者不需要安装这些工具。

从项目根目录执行：

```powershell
.\macOS\Build-Mac.ps1 -Architecture all -DotNetPath $dotnetPath -PythonPath $pythonPath
```

两个变量应设置为本机可执行文件的完整路径。默认使用 `macOS/NuGet.Config`；离线源可通过 `-NuGetConfigPath` 指定。输出默认位于 `release/macos/v0.1.0-alpha.1`，中间产物及缓存位于 `output`；不会更新 Windows 的 `dist` 或旧发布包。

脚本从显式公开清单复制干净源码，运行共享测试，再生成两架构自包含应用、源码包、摘要与打包报告。重复构建不会覆盖已有同名附件；重新试验时用 `-OutputDirectory` 指定新的项目内目录。更多信息见 [PUBLISHING.md](PUBLISHING.md)。

MIT 许可及第三方声明见根目录 `LICENSE`、[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

