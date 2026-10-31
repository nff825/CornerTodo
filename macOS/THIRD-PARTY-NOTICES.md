# Third-party notices · CornerTodo macOS 0.1.0-alpha.1

本测试包包含以下第三方运行时组件。版本依据 `Desktop/packages.lock.json` 与实际自包含发布输出整理。许可原文保存在同目录的 `ThirdPartyLicenses` 文件夹；来源和 SHA256 见 [manifest.json](ThirdPartyLicenses/manifest.json)。

本文件说明第三方组件及其原有授权，不改变 CornerTodo 自身的 MIT 源码许可。

## Avalonia

下列 11.3.19 包使用 Avalonia 的 MIT 许可：[许可原文](ThirdPartyLicenses/Avalonia-LICENSE.txt)，[对应源码提交](https://github.com/AvaloniaUI/Avalonia/tree/ccf78f4582d9a8107c6db3a57c496d503fd441b6)。

| 运行时包 | 版本信息 |
|---|---|
| Avalonia | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia/11.3.19) |
| Avalonia.Desktop | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Desktop/11.3.19) |
| Avalonia.Native | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Native/11.3.19) |
| Avalonia.Skia | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Skia/11.3.19) |
| Avalonia.Remote.Protocol | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Remote.Protocol/11.3.19) |
| Avalonia.Themes.Fluent | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Themes.Fluent/11.3.19) |
| Avalonia.Fonts.Inter（封装代码） | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Fonts.Inter/11.3.19) |
| Avalonia.FreeDesktop | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.FreeDesktop/11.3.19) |
| Avalonia.Win32 | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.Win32/11.3.19) |
| Avalonia.X11 | [NuGet 11.3.19](https://www.nuget.org/packages/Avalonia.X11/11.3.19) |

Avalonia 的公共桌面发布依赖会包含其他平台的托管程序集；本表一并保留其通知。`libAvaloniaNative.dylib` 及 Avalonia 提供的 Metal、Vulkan、OpenGL、MicroCom 封装等程序集归入上述同一源码许可。

`Avalonia.Fonts.Inter` 内嵌 Inter 字体的授权独立于封装代码：字体使用 **SIL Open Font License 1.1**，[完整原文](ThirdPartyLicenses/Inter-OFL.txt)，[官方字体项目](https://github.com/rsms/inter)。原文字体版权声明和许可条款一并保留。

## 其他运行时依赖

| 组件 | 发布版本／来源 | 随包许可与通知 |
|---|---|---|
| MicroCom.Runtime | [0.11.0](https://www.nuget.org/packages/MicroCom.Runtime/0.11.0) · [官方源码](https://github.com/kekekeks/MicroCom) | MIT · [原文](ThirdPartyLicenses/MicroCom.Runtime-LICENSE.txt) |
| Tmds.DBus.Protocol | [0.21.3](https://www.nuget.org/packages/Tmds.DBus.Protocol/0.21.3) · [对应源码](https://github.com/tmds/Tmds.DBus/tree/8cb04f66c330b64244e996ff68f685f27f7381a0) | MIT · [原文](ThirdPartyLicenses/Tmds.DBus.Protocol-LICENSE.txt) |
| SkiaSharp | [2.88.9](https://www.nuget.org/packages/SkiaSharp/2.88.9) | MIT · [原文](ThirdPartyLicenses/SkiaSharp-LICENSE.txt) · [第三方通知](ThirdPartyLicenses/SkiaSharp-THIRD-PARTY-NOTICES.txt) |
| SkiaSharp.NativeAssets.macOS | [2.88.9](https://www.nuget.org/packages/SkiaSharp.NativeAssets.macOS/2.88.9) | 与同版本 SkiaSharp 包内许可及通知逐字节相同，共用上两份原文 |
| HarfBuzzSharp | [8.3.1.1](https://www.nuget.org/packages/HarfBuzzSharp/8.3.1.1) | MIT · [原文](ThirdPartyLicenses/HarfBuzzSharp-LICENSE.txt) |
| HarfBuzzSharp.NativeAssets.macOS | [8.3.1.1](https://www.nuget.org/packages/HarfBuzzSharp.NativeAssets.macOS/8.3.1.1) | 同版本 MIT 许可 · [完整第三方通知](ThirdPartyLicenses/HarfBuzzSharp.NativeAssets.macOS-THIRD-PARTY-NOTICES.txt) |
| Microsoft.NETCore.App.Runtime.osx-arm64 / osx-x64 | [10.0.14 ARM64](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.osx-arm64/10.0.14) · [10.0.14 x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.osx-x64/10.0.14) | MIT · [原文](ThirdPartyLicenses/DotNet-Runtime-LICENSE.txt) · [完整第三方通知](ThirdPartyLicenses/DotNet-Runtime-THIRD-PARTY-NOTICES.txt) |
| Microsoft.NETCore.App.Host.osx-arm64 / osx-x64 | [10.0.14 ARM64](https://www.nuget.org/packages/Microsoft.NETCore.App.Host.osx-arm64/10.0.14) · [10.0.14 x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Host.osx-x64/10.0.14) | 同版本 .NET 许可与第三方通知，共用上两份原文 |

SkiaSharp、HarfBuzzSharp 和 .NET 的第三方通知包含其原生库及其他嵌入组件的版权和授权条款；这些文件按发行包原文保留，未翻译或删节。自包含 .NET 的 `System.*`、`Microsoft.*`、CoreCLR、hostfxr、hostpolicy、createdump 等文件归入对应 runtime / host 通知。

## 构建与平台范围

`Avalonia.BuildServices 11.3.2` 和 .NET SDK 仅用于构建，不作为本 Mac 应用包的运行时组件分发。锁定文件中 Linux、WebAssembly、Win32 的 SkiaSharp／HarfBuzzSharp 原生资产与 `Avalonia.Angle.Windows.Natives` 不在本 Mac 原生发布输出中；上游原文通知自身列出的组件仍按原文完整保留。

