using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CornerTodo
{
    /// <summary>Creates the Windows services and connects them for one process.</summary>
    internal static class ApplicationComposition
    {
        internal static void Run(LaunchOptions options)
        {
            Diagnostics.Initialize(options.DataDirectory);
            Diagnostics.Log("Starting pid=" + System.Diagnostics.Process.GetCurrentProcess().Id +
                ", windowed=" + options.Windowed + ", preview=" + options.Preview + ", startup=" + options.Startup);
            string key = LaunchOptions.GetInstanceKey(options.DataDirectory, options.Preview);
            using (var instance = new SingleInstanceSession(key)) {
                if (!instance.IsPrimary) {
                    if (options.Startup) Diagnostics.Log("Automatic duplicate ignored");
                    if (!instance.NotifyExisting(options.Startup))
                        MessageBox.Show("已有待办进程正在运行，但尚未响应窗口恢复请求。请关闭旧版进程后重新启动。",
                            "桌面待办", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var store = new TodoStore(options.DataDirectory);
                TodoState state = options.Preview ? PreviewState() : store.Load();
                IStartupManager startupManager = CreateStartupManager(options.DataDirectory);
                var interaction = new ApplicationInteraction();
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                using (var controller = new ApplicationController(
                    delegate(TodoState current) {
                        return new MainWindow(store, new TodoSession(store, current, options.Preview),
                            options.Windowed, options.Preview, startupManager, null, interaction);
                    },
                    delegate(IApplicationWindow window) { app.MainWindow = (Window)window; },
                    delegate(int code) { app.Shutdown(code); }, interaction, new DispatcherRecoveryScheduler(app.Dispatcher))) {
                    DispatcherUnhandledExceptionEventHandler unhandled = delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                        e.Handled = true;
                        Diagnostics.Log("Unhandled dispatcher exception: " + e.Exception);
                        using (interaction.EnterModal())
                            MessageBox.Show("操作未能完成：" + e.Exception.Message, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Warning);
                        controller.Fail();
                    };
                    app.DispatcherUnhandledException += unhandled;
                    try {
                        instance.Listen(delegate(Action callback) {
                            if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(callback);
                        }, controller.Restore, delegate(Exception error) { Diagnostics.Log("Restore failed: " + error); });
                        controller.Start(state, options.Startup);
                        app.Run();
                    }
                    finally { app.DispatcherUnhandledException -= unhandled; }
                }
            }
        }

        private static IStartupManager CreateStartupManager(string dataDirectory)
        {
            try { return new StartupManager(System.Reflection.Assembly.GetExecutingAssembly().Location, dataDirectory); }
            catch (Exception ex) { return new UnavailableStartupManager(ex); }
        }

        private static TodoState PreviewState()
        {
            var state = new TodoState();
            string[] titles = { "向李老师汇报研究进展", "与王同学讨论实验方案", "整理本周实验数据", "给张老师发送修改稿" };
            for (int i = 0; i < titles.Length; i++) state.Items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Text = titles[i],
                DueDate = DateTime.Today.AddDays(i == 3 ? -1 : i + 1).AddHours(i == 0 ? 14 : i == 1 ? 10 : 18),
                HasTime = i < 2, CreatedAt = DateTime.Now.AddMinutes(-i - 5), IsCompleted = i == 3, CompletedAt = i == 3 ? (DateTime?)DateTime.Now.AddHours(-1) : null });
            return state;
        }
    }

    internal sealed class UnavailableStartupManager : IStartupManager
    {
        private readonly Exception reason;
        internal UnavailableStartupManager(Exception reason) { this.reason = reason; }
        public StartupRegistration Inspect() { throw Error(); }
        public void Enable() { throw Error(); }
        public void Disable() { throw Error(); }
        private Exception Error() { return new InvalidOperationException("当前无法访问 Windows 登录启动目录。", reason); }
    }
}
