using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;

namespace CornerTodo
{
    internal static class Program
    {
        private static Application app;
        private static MainWindow window;
        private static TodoStore store;
        private static Mutex mutex;
        private static bool windowed;
        private static bool preview;
        private static bool quitting;
        private static bool recovering;
        private static EventWaitHandle wakeEvent;
        private static EventWaitHandle wakeAcknowledged;
        private static RegisteredWaitHandle wakeWait;
        public static bool EditorOpen { get; set; }

        [STAThread]
        public static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data-dir" && i + 1 < args.Length) dataDirectory = Path.GetFullPath(args[++i]);
                else if (args[i] == "--preview") { preview = true; windowed = true; }
                else if (args[i] == "--windowed") windowed = true;
            }
            try
            {
                Diagnostics.Initialize(dataDirectory);
                Diagnostics.Log("Starting pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + ", windowed=" + windowed + ", preview=" + preview);
                string key;
                using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant() + (preview ? "-preview" : "")))).Replace("-", "").Substring(0, 24);
                bool first;
                mutex = new Mutex(true, "Local\\CornerTodo-" + key, out first);
                if (!first)
                {
                    try
                    {
                        using (var signal = EventWaitHandle.OpenExisting("Local\\CornerTodo-Wake-" + key))
                        using (var acknowledged = EventWaitHandle.OpenExisting("Local\\CornerTodo-Ack-" + key))
                        {
                            acknowledged.Reset(); signal.Set();
                            if (acknowledged.WaitOne(4000)) return;
                        }
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                    MessageBox.Show("已有待办进程正在运行，但尚未响应窗口恢复请求。请关闭旧版进程后重新启动。", "桌面待办", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CornerTodo-Wake-" + key);
                wakeAcknowledged = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CornerTodo-Ack-" + key);
                store = new TodoStore(dataDirectory);
                var state = preview ? PreviewState() : store.Load();
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                    e.Handled = true;
                    Diagnostics.Log("Unhandled dispatcher exception: " + e.Exception);
                    MessageBox.Show("操作未能完成：" + e.Exception.Message, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Warning);
                    quitting = true; app.Shutdown(1);
                };
                wakeWait = ThreadPool.RegisterWaitForSingleObject(wakeEvent, delegate(object context, bool timeout) {
                    app.Dispatcher.BeginInvoke(new Action(delegate {
                        try { if (window != null && !quitting) window.RestoreToDesktop(); }
                        catch (Exception ex) { Diagnostics.Log("Restore failed: " + ex); }
                        finally { wakeAcknowledged.Set(); }
                    }));
                }, null, -1, false);
                DesktopHost.WindowHandleLost += delegate(object sender, EventArgs e) { Recover(sender as MainWindow); };
                CreateWindow(state);
                app.Run();
            }
            catch (Exception ex) { Diagnostics.Log("Startup failed: " + ex); MessageBox.Show("无法启动桌面待办：\n" + ex.Message, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { if (wakeWait != null) wakeWait.Unregister(null); if (wakeEvent != null) wakeEvent.Dispose(); if (wakeAcknowledged != null) wakeAcknowledged.Dispose(); if (mutex != null) { mutex.Dispose(); mutex = null; } }
        }

        private static void CreateWindow(TodoState state)
        {
            var created = new MainWindow(store, state, windowed, preview);
            window = created;
            app.MainWindow = created;
            created.Closing += delegate {
                if (!quitting && !recovering && !created.HostLost) {
                    created.SaveGeometry(); quitting = true;
                }
            };
            created.Closed += delegate {
                Diagnostics.Log("Main window closed, quitting=" + quitting + ", recovering=" + recovering);
                if (quitting) app.Shutdown();
                else if (!recovering) Recover(created);
            };
            Diagnostics.Log("Showing main window");
            created.Show();
        }
        private static void Recover(MainWindow lost)
        {
            if (quitting || recovering || lost == null || lost != window) return;
            recovering = true; lost.HostLost = true;
            TodoState state = lost.CurrentState;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate {
                timer.Stop();
                if (quitting) return;
                try { lost.Close(); } catch (InvalidOperationException) { }
                recovering = false;
                try { CreateWindow(state); }
                catch (Exception ex) { Diagnostics.Log("Recovery failed: " + ex); quitting = true; app.Shutdown(1); }
            };
            timer.Start();
        }
        public static void Exit()
        {
            if (quitting) return;
            if (EditorOpen) return;
            window.SaveGeometry(); quitting = true;
            DesktopHost.Detach(window); window.Close(); app.Shutdown();
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
}
