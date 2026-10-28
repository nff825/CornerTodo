using System;
using System.Windows;
using System.Windows.Threading;

namespace CornerTodo
{
    /// <summary>The native desktop boundary, replaceable without creating an HWND in tests.</summary>
    internal interface IDesktopAttachmentAdapter
    {
        event EventHandler WindowHandleLost;
        string LastError { get; }
        bool Attach(Window window);
        bool IsAttached(Window window);
        void ReattachIfNeeded(Window window);
        Rect GetBounds(Window window);
        void SetBounds(Window window, Rect bounds);
        string Diagnostics(Window window);
        void Detach(Window window);
    }

    internal sealed class DesktopAttachmentAdapter : IDesktopAttachmentAdapter
    {
        public event EventHandler WindowHandleLost
        {
            add { DesktopHost.WindowHandleLost += value; }
            remove { DesktopHost.WindowHandleLost -= value; }
        }
        public string LastError { get { return DesktopHost.LastError; } }
        public bool Attach(Window window) { return DesktopHost.Attach(window); }
        public bool IsAttached(Window window) { return DesktopHost.IsAttached(window); }
        public void ReattachIfNeeded(Window window) { DesktopHost.ReattachIfNeeded(window); }
        public Rect GetBounds(Window window) { return DesktopHost.GetBounds(window); }
        public void SetBounds(Window window, Rect bounds) { DesktopHost.SetBounds(window, bounds); }
        public string Diagnostics(Window window) { return DesktopHost.Diagnostics(window); }
        public void Detach(Window window) { DesktopHost.Detach(window); }
    }

    /// <summary>
    /// Owns attachment timing and subscriptions for one main window. Native
    /// desktop discovery, rendering and z-order remain entirely in DesktopHost.
    /// All methods and native event callbacks run on the window's dispatcher.
    /// </summary>
    internal sealed class DesktopAttachmentController : IDisposable
    {
        private readonly Window window;
        private readonly bool windowed;
        private readonly Action<bool> pendingChanged;
        private readonly Func<bool> hostLost;
        private readonly IDesktopAttachmentAdapter desktop;
        private readonly DispatcherTimer retryTimer;
        private DispatcherOperation pendingAttach;
        private bool started;
        private bool attachAttempted;
        private bool renderObserved;
        private bool pendingKnown;
        private bool waitingForDesktop;
        private bool lost;
        private bool disposed;

        public DesktopAttachmentController(Window window, bool windowed,
            Action<bool> pendingChanged, Func<bool> hostLost)
            : this(window, windowed, pendingChanged, hostLost, new DesktopAttachmentAdapter()) { }

        internal DesktopAttachmentController(Window window, bool windowed,
            Action<bool> pendingChanged, Func<bool> hostLost, IDesktopAttachmentAdapter desktop)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (pendingChanged == null) throw new ArgumentNullException("pendingChanged");
            if (hostLost == null) throw new ArgumentNullException("hostLost");
            if (desktop == null) throw new ArgumentNullException("desktop");
            this.window = window; this.windowed = windowed;
            this.pendingChanged = pendingChanged; this.hostLost = hostLost; this.desktop = desktop;
            retryTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
            retryTimer.Interval = TimeSpan.FromSeconds(5);
            retryTimer.Tick += OnRetry;
            window.ContentRendered += OnContentRendered;
            window.Closed += OnClosed;
            if (!windowed) desktop.WindowHandleLost += OnHandleLost;
        }

        public event EventHandler HandleLost;

        /// <summary>Called after Loaded; the first attachment still waits for rendering.</summary>
        public void Start()
        {
            window.Dispatcher.VerifyAccess();
            if (!CanOperate || started) return;
            started = true;
            retryTimer.Start();
        }

        /// <summary>Attaches immediately for an explicit restore, preserving screen DIP bounds.</summary>
        public void AttachNow()
        {
            window.Dispatcher.VerifyAccess();
            if (!CanOperate) return;
            CancelPendingAttach();
            attachAttempted = true;
            Rect bounds = desktop.GetBounds(window);
            if (!CanOperate) return;
            bool attached = desktop.Attach(window);
            if (!CanOperate) return;
            desktop.SetBounds(window, bounds);
            if (!CanOperate) return;
            Diagnostics.Log("Attach after render: " + attached + ", bounds=" + desktop.GetBounds(window) + ", error=" + desktop.LastError);
            ReportPending(!attached);
        }

        private bool CanOperate { get { return !disposed && !windowed && !lost && !hostLost(); } }

        private void OnContentRendered(object sender, EventArgs e)
        {
            if (disposed || renderObserved) return;
            renderObserved = true;
            FrameworkElement content = window.Content as FrameworkElement;
            Diagnostics.Log("ContentRendered " + (windowed ? "windowed" : desktop.Diagnostics(window)) +
                ", WPF-visible=" + window.IsVisible + ", content=" +
                (content == null ? "none" : content.ActualWidth + "x" + content.ActualHeight));
            if (!CanOperate || attachAttempted || window.Dispatcher.HasShutdownStarted) return;
            pendingAttach = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate {
                pendingAttach = null;
                AttachNow();
            }));
        }

        private void OnRetry(object sender, EventArgs e) { RetryNow(); }

        /// <summary>Also used by deterministic tests of the real retry callback path.</summary>
        internal void RetryNow()
        {
            window.Dispatcher.VerifyAccess();
            if (!CanOperate || !started || !attachAttempted) return;
            desktop.ReattachIfNeeded(window);
            if (CanOperate && waitingForDesktop && desktop.IsAttached(window)) ReportPending(false);
        }

        private void ReportPending(bool pending)
        {
            if (!CanOperate || (pendingKnown && waitingForDesktop == pending)) return;
            waitingForDesktop = pending; pendingKnown = true;
            pendingChanged(pending);
        }

        private void OnHandleLost(object sender, EventArgs e)
        {
            if (disposed || windowed || lost || !Object.ReferenceEquals(sender, window)) return;
            lost = true;
            retryTimer.Stop();
            CancelPendingAttach();
            EventHandler handler = HandleLost;
            if (handler != null) handler(window, EventArgs.Empty);
        }

        private void OnClosed(object sender, EventArgs e) { Dispose(); }

        private void CancelPendingAttach()
        {
            DispatcherOperation operation = pendingAttach;
            pendingAttach = null;
            if (operation != null && operation.Status == DispatcherOperationStatus.Pending) operation.Abort();
        }

        public void Dispose()
        {
            window.Dispatcher.VerifyAccess();
            if (disposed) return;
            disposed = true;
            retryTimer.Stop();
            retryTimer.Tick -= OnRetry;
            CancelPendingAttach();
            window.ContentRendered -= OnContentRendered;
            window.Closed -= OnClosed;
            if (!windowed) {
                desktop.WindowHandleLost -= OnHandleLost;
                desktop.Detach(window);
            }
        }
    }
}
