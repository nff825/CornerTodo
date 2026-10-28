using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CornerTodo;

internal static class LifecycleUiTests
{
    private static int assertions;

    internal static void Run()
    {
        Application application = null;
        MainWindow window = null;
        ApplicationController controller = null;
        try
        {
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var state = new TodoState();
            state.Settings.Width = 430; state.Settings.Height = 475; state.Settings.Left = 1170; state.Settings.Top = 18;
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "isolated-lifecycle-ui-" + Guid.NewGuid().ToString("N"));
            var session = new TodoSession(new TodoStore(dataPath), state, true);
            var interaction = new ApplicationInteraction();
            var startup = new ForbiddenStartup();
            var host = new FakePanelHost();
            window = new MainWindow(new TodoStore(dataPath), session, true, true, startup, host, interaction);
            var facade = new UnshownWindow(window);
            int closed = 0;
            var shutdown = new List<int>();
            window.Closed += delegate { closed++; };
            controller = new ApplicationController(delegate(TodoState ignored) { return facade; },
                delegate(IApplicationWindow assigned) { application.MainWindow = window; },
                delegate(int code) { shutdown.Add(code); }, interaction, new ForbiddenScheduler());
            controller.Start(state, false);
            Assert(facade.ShowCalls == 1 && NoHandle(window), "The integration fixture showed or created a native main window.");
            window.Interaction.Initialize(session.Snapshot().Settings);
            window.Interaction.BeginMove(new Point(0, 0));
            window.Interaction.MoveTo(new Point(-50, 12));
            window.Interaction.EndMove();
            Assert(session.Snapshot().Settings.Left == 1170, "The interaction controller saved without the lifecycle connection.");
            DispatcherOperation queuedResume = (DispatcherOperation)typeof(MainWindow).GetField("resumeOperation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            Assert(queuedResume != null && queuedResume.Status == DispatcherOperationStatus.Pending, "The test did not queue attention resumption before exit.");

            MenuItem exit = ((ContextMenu)typeof(MainWindow).GetMethod("BuildMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null))
                .Items.OfType<MenuItem>().Single(item => Object.Equals(item.Header, "退出待办"));
            interaction.EditorOpen = true; Click(exit);
            Assert(closed == 0 && facade.SaveCalls == 0 && shutdown.Count == 0, "The real menu bypassed the shared editor guard.");
            interaction.EditorOpen = false;
            IDisposable outer = interaction.EnterModal();
            IDisposable inner = interaction.EnterModal();
            outer.Dispose(); Click(exit);
            Assert(closed == 0 && facade.SaveCalls == 0 && shutdown.Count == 0, "The real menu bypassed a remaining modal scope.");
            inner.Dispose(); Click(exit);

            WidgetSettings saved = session.Snapshot().Settings;
            Assert(saved.Left == 1120 && saved.Top == 30 && saved.Width == 430 && saved.Height == 475,
                "Lifecycle exit did not pass the real controller baseline to the injected task session.");
            Assert(closed == 1 && facade.SaveCalls == 1 && facade.DisposeCalls == 1 && facade.CloseCalls == 1,
                "The real main window did not save and close exactly once.");
            Assert(shutdown.SequenceEqual(new[] { 0 }), "The real menu did not request exactly one successful shutdown.");
            Assert(queuedResume.Status == DispatcherOperationStatus.Aborted, "Closing did not cancel queued attention resumption.");
            int boundsCalls = host.SetCalls;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            Click(exit); controller.Exit();
            Assert(host.SetCalls == boundsCalls && closed == 1 && shutdown.Count == 1, "A late attention callback or exit changed a closed window.");
            Assert(NoHandle(window) && !Directory.Exists(dataPath), "Unshown preview wiring created an HWND or application data.");
            Assert(startup.Calls == 0, "Lifecycle wiring inspected or changed Windows startup state.");
            Console.WriteLine("PASS: " + assertions + " main-window lifecycle wiring assertions (unshown, preview session, fake screen and startup boundary).");
        }
        finally
        {
            if (controller != null) controller.Dispose();
            if (window != null) try { window.Close(); } catch (InvalidOperationException) { }
            if (application != null) application.Shutdown();
        }
    }

    private static void Click(MenuItem item) { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
    private static bool NoHandle(Window window) { return new WindowInteropHelper(window).Handle == IntPtr.Zero; }
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }

    // Only Show is suppressed: state, geometry saving, disposal, Close and both
    // lifecycle events use the real MainWindow paths exercised by production.
    private sealed class UnshownWindow : IApplicationWindow
    {
        private readonly MainWindow window;
        public int ShowCalls, CloseCalls, SaveCalls, DisposeCalls;
        internal UnshownWindow(MainWindow window) { this.window = window; }
        public event CancelEventHandler Closing { add { window.Closing += value; } remove { window.Closing -= value; } }
        public event EventHandler Closed { add { window.Closed += value; } remove { window.Closed -= value; } }
        public event EventHandler HandleLost { add { window.HandleLost += value; } remove { window.HandleLost -= value; } }
        public bool ShowActivated { get { return window.ShowActivated; } set { window.ShowActivated = value; } }
        public bool HostLost { get { return window.HostLost; } set { window.HostLost = value; } }
        public TodoState CurrentState { get { return window.CurrentState; } }
        public void Show() { ShowCalls++; }
        public void Close() { CloseCalls++; window.Close(); }
        public void SaveGeometry() { SaveCalls++; window.SaveGeometry(); }
        public void RestoreToDesktop() { throw new InvalidOperationException("This fixture must never restore or Show a native window."); }
        public void Dispose() { DisposeCalls++; window.Dispose(); }
    }

    private sealed class FakePanelHost : IPanelHost
    {
        private Rect bounds = new Rect(1170, 18, 430, 475);
        public int SetCalls;
        public Rect GetBounds() { return bounds; }
        public void SetBounds(Rect value) { SetCalls++; bounds = value; }
        public Rect WorkArea() { return new Rect(0, 0, 2560, 1528); }
    }

    private sealed class ForbiddenStartup : IStartupManager
    {
        public int Calls;
        public StartupRegistration Inspect() { Calls++; throw new InvalidOperationException("Unexpected startup inspection."); }
        public void Enable() { Calls++; throw new InvalidOperationException("Unexpected startup registration."); }
        public void Disable() { Calls++; throw new InvalidOperationException("Unexpected startup removal."); }
    }

    private sealed class ForbiddenScheduler : IRecoveryScheduler
    {
        public IDisposable Schedule(TimeSpan delay, Action callback) { throw new InvalidOperationException("An explicit menu exit scheduled recovery."); }
    }
}
