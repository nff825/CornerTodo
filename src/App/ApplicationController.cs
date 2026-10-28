using System;
using System.ComponentModel;
using System.Windows.Threading;

namespace CornerTodo
{
    internal interface IApplicationWindow : IDisposable
    {
        event CancelEventHandler Closing;
        event EventHandler Closed;
        event EventHandler HandleLost;
        bool ShowActivated { get; set; }
        bool HostLost { get; set; }
        TodoState CurrentState { get; }
        void Show();
        void Close();
        void SaveGeometry();
        void RestoreToDesktop();
    }

    internal interface IRecoveryScheduler
    {
        IDisposable Schedule(TimeSpan delay, Action callback);
    }

    /// <summary>Owns one current window, recovery and shutdown. All calls use the UI thread.</summary>
    internal sealed class ApplicationController : IDisposable
    {
        private readonly Func<TodoState, IApplicationWindow> createWindow;
        private readonly Action<IApplicationWindow> setMainWindow;
        private readonly Action<int> shutdown;
        private readonly ApplicationInteraction interaction;
        private readonly IRecoveryScheduler scheduler;
        private IApplicationWindow window;
        private IDisposable recovery;
        private int recoveryGeneration;
        private bool started, startup, quitting, recovering, disposed, shutdownRequested, restorePending;
        private int exitCode;

        internal ApplicationController(Func<TodoState, IApplicationWindow> createWindow,
            Action<IApplicationWindow> setMainWindow, Action<int> shutdown,
            ApplicationInteraction interaction, IRecoveryScheduler scheduler)
        {
            if (createWindow == null) throw new ArgumentNullException("createWindow");
            if (setMainWindow == null) throw new ArgumentNullException("setMainWindow");
            if (shutdown == null) throw new ArgumentNullException("shutdown");
            if (interaction == null) throw new ArgumentNullException("interaction");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            this.createWindow = createWindow; this.setMainWindow = setMainWindow;
            this.shutdown = shutdown; this.interaction = interaction; this.scheduler = scheduler;
            interaction.ExitRequested += ExitRequested;
        }

        internal void Start(TodoState state, bool startup)
        {
            if (disposed) throw new ObjectDisposedException("ApplicationController");
            if (started) throw new InvalidOperationException("The application has already started.");
            if (state == null) throw new ArgumentNullException("state");
            started = true; this.startup = startup;
            CreateWindow(state);
        }

        private void CreateWindow(TodoState state)
        {
            IApplicationWindow created = createWindow(state);
            if (created == null) throw new InvalidOperationException("The window factory returned no window.");
            window = created;
            created.Closing += WindowClosing; created.Closed += WindowClosed; created.HandleLost += WindowHandleLost;
            try {
                if (startup) created.ShowActivated = false;
                setMainWindow(created);
                Diagnostics.Log("Showing main window");
                created.Show();
            }
            catch {
                Unwire(created); window = null;
                ReleaseWindow(created);
                throw;
            }
        }

        internal void Restore()
        {
            if (disposed || quitting || window == null) return;
            if (recovering) { restorePending = true; return; }
            window.RestoreToDesktop();
        }

        internal void Exit()
        {
            if (disposed || quitting || interaction.IsBusy) return;
            if (window != null && !window.HostLost) window.SaveGeometry();
            quitting = true;
            CancelRecovery();
            if (window != null) {
                IApplicationWindow closing = window; window = null;
                Unwire(closing); ReleaseWindow(closing);
            }
            Shutdown();
        }

        internal void Fail()
        {
            if (disposed || shutdownRequested) return;
            quitting = true; exitCode = 1;
            CancelRecovery();
            Shutdown();
        }

        private void ExitRequested(object sender, EventArgs e) { Exit(); }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            IApplicationWindow closing = sender as IApplicationWindow;
            if (disposed || closing != window || quitting || recovering || closing.HostLost || e.Cancel) return;
            if (interaction.IsBusy) { e.Cancel = true; return; }
            closing.SaveGeometry(); quitting = true; CancelRecovery();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            IApplicationWindow closed = sender as IApplicationWindow;
            if (disposed || closed != window) return;
            Diagnostics.Log("Main window closed, quitting=" + quitting + ", recovering=" + recovering);
            if (quitting) {
                Unwire(closed); window = null;
                closed.Dispose();
                Shutdown();
            }
            else if (!recovering) Recover(closed);
        }

        private void WindowHandleLost(object sender, EventArgs e) { Recover(sender as IApplicationWindow); }

        private void Recover(IApplicationWindow lost)
        {
            if (disposed || quitting || recovering || lost == null || lost != window) return;
            recovering = true; lost.HostLost = true;
            ScheduleRecovery(lost);
        }

        private void ScheduleRecovery(IApplicationWindow lost)
        {
            int generation = ++recoveryGeneration;
            recovery = scheduler.Schedule(TimeSpan.FromSeconds(2), delegate {
                if (disposed || quitting || generation != recoveryGeneration || lost != window) return;
                recovery = null;
                // Modal dialogs run a nested dispatcher. Wait until their edits
                // commit, then read the latest state rather than a stale snapshot.
                if (interaction.IsBusy) { ScheduleRecovery(lost); return; }
                try {
                    TodoState state = lost.CurrentState;
                    Unwire(lost); window = null;
                    ReleaseWindow(lost);
                    recovering = false;
                    CreateWindow(state);
                    if (restorePending) { restorePending = false; Restore(); }
                }
                catch (Exception ex) { Diagnostics.Log("Recovery failed: " + ex); Fail(); }
            });
        }

        private void CancelRecovery()
        {
            recoveryGeneration++; restorePending = false;
            if (recovery != null) { recovery.Dispose(); recovery = null; }
        }

        private void Shutdown()
        {
            if (shutdownRequested) return;
            shutdownRequested = true;
            shutdown(exitCode);
        }

        private void Unwire(IApplicationWindow target)
        {
            target.Closing -= WindowClosing; target.Closed -= WindowClosed; target.HandleLost -= WindowHandleLost;
        }

        private static void ReleaseWindow(IApplicationWindow target)
        {
            try { target.Dispose(); }
            catch (Exception ex) { Diagnostics.Log("Window cleanup failed: " + ex); }
            try { target.Close(); }
            catch (InvalidOperationException) { /* WPF has already closed this source. */ }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; quitting = true; CancelRecovery();
            interaction.ExitRequested -= ExitRequested;
            if (window != null) {
                IApplicationWindow released = window; window = null;
                Unwire(released); ReleaseWindow(released);
            }
        }
    }

    internal sealed class DispatcherRecoveryScheduler : IRecoveryScheduler
    {
        private readonly Dispatcher dispatcher;
        internal DispatcherRecoveryScheduler(Dispatcher dispatcher) { this.dispatcher = dispatcher; }
        public IDisposable Schedule(TimeSpan delay, Action callback) { return new ScheduledRecovery(dispatcher, delay, callback); }

        private sealed class ScheduledRecovery : IDisposable
        {
            private readonly DispatcherTimer timer;
            private Action callback;
            internal ScheduledRecovery(Dispatcher dispatcher, TimeSpan delay, Action callback)
            {
                this.callback = callback;
                timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
                timer.Tick += Tick; timer.Start();
            }
            private void Tick(object sender, EventArgs e)
            {
                Action action = callback;
                Dispose();
                if (action != null) action();
            }
            public void Dispose() { timer.Stop(); timer.Tick -= Tick; callback = null; }
        }
    }
}
