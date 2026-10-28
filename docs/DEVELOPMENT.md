# 开发工程说明

当前应用版本为 **1.2.1**。已统一开发与构建入口，提取任务操作服务，拆分面板视图、交互控制器与启动生命周期。本版整理内部结构并修正恢复和清理边界，保持现有产品功能与数据格式。生产源码按职责归入 `src`，工程、配置与构建入口保留在根目录。

## 工程与共享清单

```text
CornerTodo.sln           Visual Studio 解决方案
CornerTodo.csproj        单个传统 WPF 应用项目
CornerTodo.Build.props   显式源码、框架引用、测试元数据和公开文件白名单
Build.ps1               无 .NET SDK 依赖的脚本构建入口
Package.ps1             干净源码与便携版打包入口
VERSION                 应用版本
app.manifest            Windows 应用清单
CornerTodo.exe.config   运行时配置
src/App/                入口、依赖组合、应用生命周期、日志与单实例通信
src/Tasks/              任务状态、操作服务、复制与存储
src/UI/                 编辑器、面板视图、外观与交互布局
src/Windows/            桌面挂接和 Windows 登录自启动集成
build/Common.ps1        构建、测试与打包共用的清单读取和基础操作
build/Generate-Icon.ps1  脚本与工程共用的图标生成入口
tests/                  原有功能测试及其脚本入口
docs/                   使用、开发与发布说明
```

应用继续使用 .NET Framework 4.7.2、C# 5 和 AnyCPU。四个源码目录仍属于同一个应用项目、同一个程序集，命名空间保持 `CornerTodo`；本次不引入新的运行时依赖。

26 个生产源码文件的归类如下，完整编译路径以根目录共享清单为准：

| 目录 | 文件 |
| --- | --- |
| `src/App`（8） | `ApplicationComposition.cs`、`ApplicationController.cs`、`ApplicationInteraction.cs`、`AssemblyInfo.cs`、`Diagnostics.cs`、`LaunchOptions.cs`、`Program.cs`、`SingleInstanceSession.cs` |
| `src/Tasks`（6） | `ITodoStore.cs`、`Models.cs`、`TaskDraft.cs`、`TodoSession.cs`、`TodoStateCopy.cs`、`TodoStore.cs` |
| `src/UI`（9） | `AppearanceEditor.cs`、`AppearanceSettings.cs`、`GrowthLayout.cs`、`MainWindow.cs`、`PanelController.cs`、`TaskEditor.cs`、`TaskListView.cs`、`Theme.cs`、`TodoPanelView.cs` |
| `src/Windows`（3） | `DesktopAttachmentController.cs`、`DesktopHost.cs`、`StartupManager.cs` |

`CornerTodo.csproj` 与 PowerShell 脚本读取同一份 `CornerTodo.Build.props`。修改源码列表、框架引用、测试登记或公开文件白名单时，应在这份共享清单中维护，避免 IDE、脚本测试与发布包采用不同输入。

## 当前界面与任务操作架构

应用仍是单进程、单个应用程序集。任务与本地面板设置由 `TodoSession` 管理，`MainWindow` 通过命令修改状态，再把快照交给视图呈现；面板几何与交互由 `PanelController` 管理。当前服务在 UI 线程调用，没有引入后台状态线程或新的框架依赖。

| 组件 | 职责与边界 |
| --- | --- |
| `Program` / `ApplicationComposition` | 入口解析启动参数与设置文化；组合入口创建存储、任务服务、Windows 适配器和 WPF 应用，连接并释放一个进程的依赖 |
| `SingleInstanceSession` | 保留按数据目录与预览模式区分的实例身份，管理命名互斥体及 Wake/Ack 通信，把恢复请求交给 UI 调度；不创建窗口，不管理待办 |
| `ApplicationController` / `IApplicationWindow` | 管理当前主窗口、正常退出、句柄丢失后的延迟重建及排队恢复请求；通过窗口窄接口和可替换调度器测试，不直接实现原生桌面挂载 |
| `ApplicationInteraction` | 跨替换窗口共享编辑状态、支持嵌套的模态作用域及退出请求；避免重建或退出打断仍在提交的编辑操作 |
| `TaskEditor` / `TaskDraft` | 编辑器负责输入控件、格式校验与日期映射，仅返回 `Text`、`DueDate`、`HasTime` 三个输入字段；不生成任务 ID 或生命周期时间戳 |
| `MainWindow` | 连接视图、面板控制器、任务服务和挂载控制器，协调菜单、对话框及刷新；持有显示快照，通过 `Execute` 调用服务命令并展示错误，不自行修改或保存任务对象，也不重建主窗口 |
| `TodoPanelView` / `TaskListView` | 构建面板和事项控件树，应用配色、文案、字号与折叠呈现，并向窗口转发新增、编辑、完成等操作意图；不保存数据，不决定窗口位置 |
| `PanelPalette` | 根据外观设置提供面板、文字、按钮和勾选框的颜色及样式，不改变设置或任务状态 |
| `PanelController` | 管理拖动、调整大小、增长动画、交互暂停和常态尺寸／右上锚点；接收增长进度，不决定任务紧迫程度，不写入待办或设置 |
| `IPanelHost` / `DesktopPanelHost` | 提供面板边界、位置更新和工作区的窄接口；生产适配器沿用 `DesktopHost`，测试可替换为假宿主 |
| `DesktopAttachmentController` / `IDesktopAttachmentAdapter` | 组织渲染后的首次挂载、定时重试、挂载状态提示和句柄丢失转发，释放时取消排队工作；适配器保留现有 `DesktopHost` 的 Win32 实现 |
| `TodoSession` | 独占已提交状态，集中处理新增、编辑、完成、恢复、删除及本地面板设置命令，决定身份和时间戳规则 |
| `TodoStateCopy` | 生产状态图的统一深复制入口，复制任务、设置与外观对象；不通过 JSON 往返复制状态，以免引入加载兼容逻辑 |
| `ITodoStore` | 仅声明 `Save(TodoState)`，是服务所需的写入边界；不包含加载或界面能力 |
| `TodoStore` | 实现写入接口，保留现有 JSON 加载、兼容读取、损坏恢复、原子保存与备份机制；加载仍由应用启动流程负责 |
| `TodoRules` / `GrowthLayout` | 保留截止时间、排序、紧迫程度及增长几何的现有规则 |
| `StartupManager` / `DesktopHost` | 继续负责 Windows 登录登记与原生桌面集成，不把这些系统状态移入待办 JSON |

一次修改按以下顺序执行：

1. 编辑器产生 `TaskDraft`，或窗口发出完成、删除、设置等命令。
2. `TodoSession` 从当前状态创建独立候选副本，在副本中完成验证和修改。
3. 正常模式先把候选状态的另一份深拷贝交给 `ITodoStore.Save`。只有保存成功，服务才切换已提交状态；保存抛错时不发布候选状态。写入方持有或修改入参也不能改变服务拥有的对象图。
4. 窗口通过 `Snapshot()` 取得新的深拷贝并交给视图更新显示。`Execute` 只捕获服务操作的错误；保存已经成功后的渲染错误不应被误报成保存失败。

视图的回调只表达操作请求，具体服务命令及成功后的刷新仍由窗口协调。自动增长沿用 `TodoRules` 和 `GrowthLayout`；窗口把现有规则计算出的进度传给控制器。保存位置时，窗口读取控制器的 `BaselineBounds`，再调用任务服务保存，避免把自动扩大的临时尺寸覆盖到常态配置。原生桌面挂载、恢复和层级仍由现有 Windows 集成负责。

`Snapshot()` 返回可独立使用的副本，修改该副本不会提交更改。构造参数、快照、保存入参以及 `Add` 返回的任务都不应泄露服务拥有的可变对象；需要持久修改时使用服务命令。新增状态字段时，应同时更新 `TodoStateCopy` 和对应复制隔离测试。

### 身份与时间规则

- 新增任务由服务生成 ID，同一次新增使用同一个保存时刻设置 `CreatedAt` 和 `DeadlineSetAt`。
- 编辑保留 ID、`CreatedAt`、完成状态及 `CompletedAt`。只有 `TodoRules.Deadline` 计算出的有效截止时间变化，才重设 `DeadlineSetAt`；文字变化或截止表达方式变化但有效时间相同，不会重置它。旧数据中的空 `DeadlineSetAt` 也按这个规则保留。
- 标记完成设置 `CompletedAt`，恢复未完成清空它；这些操作不修改创建时间、截止时间或调度起点。重复设置相同完成状态不再次保存。
- 只填日期仍以次日零点为有效截止；UTC 截止先按本地时间显示。编辑器的日期／分钟控件未改时，会映射回原始 `DueDate` 的 ticks 和 `Kind`，避免控件精度丢失被误判成重新安排截止时间。有效截止是否变化最终仍由服务判断。

`preview=true` 仍执行验证、命令和内存状态提交，但不调用写入接口；服务构造与快照读取也不写盘。`Session` 测试组使用假写入方、可控时钟及临时目录验证这些规则，不创建 WPF `Application`／`Window`，也不挂载真实桌面。

### 启动、退出与恢复

正常数据的实例键继续兼容现有版本；独立数据目录和预览模式互不冲突。自动重复启动静默结束，不唤醒、展开或重置已有面板；手动重复启动才发送恢复请求。首次显示保留保存的位置与折叠设置，登录启动不激活窗口。自启动登记仍由 `StartupManager` 的明确操作处理，生命周期服务不会替用户登记或修复它。

`ApplicationController` 只接受当前窗口的关闭与丢失事件。句柄丢失后等待两秒；编辑器或模态操作仍在运行时继续等待，真正重建前才读取最新已提交快照。恢复期间到达的手动恢复请求合并到新窗口执行，旧窗口的过期事件与已取消回调不能影响替换窗口。正常退出保存基准位置并释放窗口；释放控制器会取消恢复工作、解除订阅和清理窗口，不主动调用应用退出。原生挂载调度的清理也独立于任务 JSON 和登录登记。

## 路径一：Windows PowerShell 脚本

在项目根目录使用 **Windows PowerShell** 运行：

```powershell
powershell -NoProfile -File .\Build.ps1
```

脚本使用 Windows 自带的 .NET Framework C# 编译器；这条路径无需另装 .NET SDK、Visual Studio 或下载 NuGet 包。需要可用的 .NET Framework 编译器及 WPF 框架库。

默认输出为 `dist\CornerTodo.exe`，同目录的 `CornerTodo.exe.config` 应一并保留。开发时优先使用独立目录：

```powershell
powershell -NoProfile -File .\Build.ps1 -OutputDirectory dist-dev
powershell -NoProfile -File .\Build.ps1 -Test -OutputDirectory dist-dev
```

第一条只构建；第二条构建并执行测试。独立目录用于避免覆盖正在运行的正式程序；不得把正式 `Data` 文件夹复制进去作为测试样本。

构建脚本不会启动应用、创建登录自启动项、修改真实待办或上传文件。运行生成的应用则会使用该应用所在目录旁的 `Data`，与其他便携副本保持独立。

## 路径二：Visual Studio 或 MSBuild

使用支持传统 WPF 项目的 Visual Studio／MSBuild，并安装 **.NET Framework 4.7.2 Targeting Pack**。运行时已安装不等于开发所需的引用程序集齐备；这项开发要求不影响普通用户直接使用便携版。

1. 打开 `CornerTodo.sln`，确认项目为 `CornerTodo`。
2. 选择 `Debug` 或 `Release`，平台为 `Any CPU`。
3. 构建输出分别位于 `bin\Debug`、`bin\Release`；中间文件位于 `obj`。
4. IDE 构建不会覆盖脚本的默认 `dist`，也不会自动替换正在使用的程序。

若开发环境已提供 `MSBuild.exe`，可从相应的开发者命令行运行：

```powershell
msbuild .\CornerTodo.sln /p:Configuration=Debug /p:Platform="Any CPU"
msbuild .\CornerTodo.sln /p:Configuration=Release /p:Platform="Any CPU"
```

脚本与 IDE 的源码及引用定义一致，但构建配置和编译器可能不同；不以 Debug、Release 和脚本产物的二进制哈希完全相同作为一致性要求。

脚本路径从本机安装的 .NET Framework 目录取得编译器和框架库；标准工程则声明目标为 4.7.2，由开发环境的 Targeting Pack 提供对应引用程序集。新增框架 API 时，应保持 4.7.2 兼容，并在具备该 Targeting Pack 的开发环境中验证；仅脚本编译成功不能代替这项目标框架检查。

只查看工程求值后的源码和引用声明，可以运行：

```powershell
msbuild .\CornerTodo.csproj /t:DescribeBuild
```

这个目标不执行编译，也不代表本机已具备完成 .NET Framework 4.7.2 构建的全部开发组件。

## 新增或调整源码

新增生产 `.cs` 文件时，按职责放入对应的 `src` 子目录，再在 `CornerTodo.Build.props` 的现有 `Compile` 清单中增加项目相对路径，例如：

```xml
<Compile Include="src/UI/NewFeature.cs" />
```

移动、移除或重命名文件时，在同一清单调整对应路径；若该文件还由 `AssemblyInfoFile` 等属性单独引用，也要同步更新该属性。仅把文件放进目录不代表参与构建；不要再在 `Build.ps1`、某个测试脚本或 `CornerTodo.csproj` 中单独增加另一套生产源码清单。测试源码继续放在 `tests` 并按测试元数据登记。

通过 IDE 新建文件后，检查它是否自动向 `.csproj` 添加了 `Compile` 项；生产源码的登记应集中到 `.props`，避免重复编译或仅 IDE 能构建。框架引用和测试元数据也应按清单中已有字段统一维护。

共享清单目前使用以下项目类型：

| 项目类型 | 用途 |
| --- | --- |
| `Compile` | 生产源码；可用 `TestGroups` 标记供部分纯逻辑测试使用的源码组 |
| `Reference` | 框架程序集名及对应的 `FrameworkFile` 路径 |
| `CornerTodoTestSuite` | 共享清单登记的功能测试名称、入口、源码组、执行脚本和可选输出目录 |
| `CornerTodoTestSource` | 测试源码及所属套件 |
| `CornerTodoPublicFile` | 除生产源码、测试源码和套件脚本以外，需要公开打包的文档、资源和工程辅助文件 |

公开源码清单会合并生产源码、测试源码、套件脚本与 `CornerTodoPublicFile`。新增生产源码只需调整 `Compile`，不要再把同一路径重复加入公开文件白名单；新增开发文档等非源码文件则登记到 `CornerTodoPublicFile`。重复路径会使打包检查失败，运行数据和构建产物也不能进入公开清单。

这份 `.props` 作为脚本和 MSBuild 共用的**纯数据清单**，限制如下：

- 每个源码项填写一个明确、项目内的相对文件路径。
- 不使用 `Condition`、`*`／`?`／`**` 通配符或由目录扫描推断源码。
- 不使用 `$(...)`、`@(...)`、`%(...)` 等 MSBuild 动态表达式。
- 不在清单中加入可执行的 Target、Task 或其他动态构建逻辑。

需要扩展清单能力时，应先同步调整共享读取与验证逻辑，并验证脚本和工程得到相同输入；不要只依赖 MSBuild 能解释某种写法。

## 测试与调试数据

共享清单目前登记十一组功能测试；原有入口保留，并增加单实例、桌面挂载调度及应用生命周期入口：

```powershell
powershell -NoProfile -File .\tests\Run-StoreTests.ps1
powershell -NoProfile -File .\tests\Run-SessionTests.ps1
powershell -NoProfile -File .\tests\Run-GrowthLayoutTests.ps1
powershell -NoProfile -File .\tests\Run-PanelTests.ps1
powershell -NoProfile -File .\tests\Run-ViewTests.ps1
powershell -NoProfile -File .\tests\Run-OffscreenUiTests.ps1
powershell -NoProfile -File .\tests\Run-AppearanceUiTests.ps1
powershell -NoProfile -File .\tests\Run-StartupTests.ps1
powershell -NoProfile -File .\tests\Run-InstanceTests.ps1
powershell -NoProfile -File .\tests\Run-DesktopTests.ps1
powershell -NoProfile -File .\tests\Run-LifecycleTests.ps1
```

`Build.ps1 -Test` 执行共享清单登记的功能测试组。`Session` 套件按 `TestGroups=Session` 选择服务及其依赖源码，验证先保存后提交、保存失败与重试、状态图隔离、时间戳、预览，以及临时目录中的真实存储往返，不依赖 WPF 控件。`Panel` 套件用假宿主和可控时钟验证几何、动画与交互状态；`View` 套件检查未显示的控件树、呈现和操作意图。离屏界面测试另外覆盖控制器、窗口与预览服务之间的基准位置保存接线。

`Instance` 套件使用独立的命名空间验证单实例通信及释放，不启动正式应用。`Desktop` 套件使用未显示的窗口与假原生适配器，检查挂载调度、重试、丢失和取消回调。`Lifecycle` 套件先使用假窗口、手动调度器和内存状态，验证正常退出、模态保护、延迟恢复、最新快照及过期事件；另用未显示的真实主窗口检查菜单退出、共享交互保护、预览服务保存和回调取消的接线，不创建原生窗口句柄或挂载桌面。

测试沿用隔离数据、独立命名空间、假宿主和假管理器等方式，不应访问或修改用户的正式 `Data`，也不会替用户开启真实登录自启动、重启 Explorer 或注销、重启电脑。这些检查没有验证实际桌面的鼠标／键盘输入、原生桌面层组合、Explorer 重启、系统颜色对话框或真实登录；相应回归仍需在目标 Windows 环境中完成。

工程与构建流程变更还需单独执行深度构建回归：

```powershell
powershell -NoProfile -File .\tests\Run-BuildTests.ps1
```

该脚本按公开白名单创建测试自有的工程副本，检查未声明源码不会混入编译；再在副本中移动一份生产源码，只修改共享 `Compile` 项，验证构建、清单登记的功能测试与干净打包仍能完成，并检查源码包内容和合成数据保持情况。源码移动、包输出和样本数据都在隔离目录中完成，不执行正式应用。

`Run-BuildTests.ps1` 是独立集成检查，不能登记成会被 `Build.ps1 -Test` 调用的功能测试组：它自身会执行 `Package.ps1 → Build.ps1 -Test`，加入后会形成递归。这个脚本作为 `CornerTodoPublicFile` 随源码公开，供维护者按需执行。

调试程序时建议使用示例模式：

```powershell
.\dist-dev\CornerTodo.exe --preview --windowed
```

IDE 中也可为调试会话设置 `--preview --windowed` 参数。若需要创建可保存的测试事项，使用新的隔离目录传入 `--data-dir`，不要指向正式数据。所有运行方式的默认数据位置仍是正在运行的 `CornerTodo.exe` 旁的 `Data`，不因工程整理而改到项目根目录或用户配置目录。

## 本地打包与发布边界

`Package.ps1` 仍负责从公开文件清单建立干净副本，在副本中构建、测试并生成本地便携包和源码包。源码包保留 `src/App`、`src/Tasks`、`src/UI`、`src/Windows` 的目录结构，工程文件、共享清单、共享脚本与开发说明一并保留；个人待办、日志、调试输出和本机自启动登记继续排除。

```powershell
powershell -NoProfile -File .\Package.ps1 -Version 1.2.1 -OutputDirectory release\v1.2.1
```

该命令会生成或更新本地 `release\v1.2.1` 产物；它不是开发日常构建的必要步骤，也不会替用户创建仓库、上传附件或发布 GitHub Release。v1.2.1 尚未发布，目录整理完成后重新生成该目录中的源码包、便携包、校验文件和干净源码目录。独立的版本目录保留旧版文件与校验记录；上传完整 `src` 并清理仓库旧根目录生产源码后，再为本次源码创建 `v1.2.1` 标签，保留已发布的 `v1.2.0`。

公开发布的网页步骤见 [PUBLISHING.md](PUBLISHING.md)；登录自启动行为见 [STARTUP.md](STARTUP.md)。
