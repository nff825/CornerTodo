using System.Globalization;
using Avalonia;
using CornerTodo.MacCore;

namespace CornerTodo.MacDesktop;

internal sealed class DesktopOptions
{
    internal bool Preview { get; private set; }
    internal bool SmokeTest { get; private set; }
    internal string? RenderDirectory { get; private set; }
    internal string Scenario { get; private set; } = "normal";
    internal string DataDirectory { get; private set; } = AppPaths.GetDataDirectory();
    internal static DesktopOptions Parse(string[] args)
    {
        var result = new DesktopOptions();
        string Value(ref int index)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("启动参数缺少值。");
            return args[index];
        }
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--preview": result.Preview = true; break;
                case "--smoke-test": result.Preview = result.SmokeTest = true; break;
                case "--render-ui": result.RenderDirectory = Path.GetFullPath(Value(ref index)); result.Preview = true; break;
                case "--preview-scenario": result.Scenario = Value(ref index); result.Preview = true; break;
                case "--data-dir": result.DataDirectory = Path.GetFullPath(Value(ref index)); break;
                default: throw new ArgumentException("无法识别的启动参数。");
            }
        }
        if (result.Scenario is not ("normal" or "urgent" or "overdue")) throw new ArgumentException("预览状态无效。");
        return result;
    }
}

internal static class Program
{
    internal const string Version = "0.1.0-alpha.1";
    internal static DesktopOptions Options { get; private set; } = new();
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            if (args.SequenceEqual(new[] { "--version" })) { Console.WriteLine(Version); return 0; }
            Options = DesktopOptions.Parse(args);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            using var instance = new SingleInstance(Options.DataDirectory, Options.Preview);
            Instance = instance;
            if (!instance.IsPrimary) { Console.WriteLine("已请求现有 CornerTodo 显示面板。"); return 0; }
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            Log(error); Console.Error.WriteLine("CornerTodo 启动失败：" + error.GetType().Name); return 1;
        }
        finally { Instance = null; }
    }

    internal static void Log(Exception error)
    {
        if (Options.Preview) return;
        try
        {
            Directory.CreateDirectory(Options.DataDirectory);
            File.AppendAllText(Path.Combine(Options.DataDirectory, "CornerTodo.log"),
                DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + " " + error.GetType().FullName + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static TodoState PreviewState()
    {
        DateTime now = DateTime.Now;
        var state = new TodoState();
        state.Items.Add(new TodoItem { Id = "sample-one", Text = "向李老师汇报研究进展", Notes = "汇总本周实验结果\n准备下一阶段研究计划",
            Priority = TaskPriority.High, CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2), DueDate = now.AddDays(2), HasTime = true });
        state.Items.Add(new TodoItem { Id = "sample-two", Text = "整理研究数据", Priority = TaskPriority.Normal,
            CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2), DueDate = now.AddDays(3), HasTime = false });
        state.Items.Add(new TodoItem { Id = "sample-three", Text = "修改论文第二章", Priority = TaskPriority.Low,
            CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2), DueDate = now.AddDays(5), HasTime = true });
        state.Items.Add(new TodoItem { Id = "sample-done", Text = "完成阶段实验记录归档", IsCompleted = true,
            CreatedAt = now.AddDays(-3), DueDate = now.AddDays(-1), CompletedAt = now.AddHours(-1), HasTime = false });
        if (Options.Scenario != "normal")
        {
            state.Items[0].CreatedAt = now.AddHours(-22);
            state.Items[0].DeadlineSetAt = now.AddHours(-22);
            state.Items[0].DueDate = Options.Scenario == "urgent" ? now.AddHours(2) : now.AddHours(-1);
        }
        return state;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
        // The Windows build is only an auxiliary preview host. Keep its image
        // validation independent of driver-specific translucent-window rendering.
        if (OperatingSystem.IsWindows()) builder = builder.With(new Win32PlatformOptions {
            RenderingMode = new[] { Win32RenderingMode.Software }
        });
        return builder;
    }
}
