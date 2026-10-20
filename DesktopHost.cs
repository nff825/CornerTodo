using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CornerTodo
{
    /// <summary>
    /// Keeps an ordinary layered WPF window in the desktop z-order band. It never
    /// reparents the HWND: .NET Framework's per-pixel opacity is top-level only.
    /// Desktop-state tracking follows Rainmeter's sentinel/anchor design, with an
    /// additional foreground check so another app immediately lowers the widget.
    /// All public methods must run on the window's Dispatcher thread.
    /// </summary>
    public static class DesktopHost
    {
        private const int GwlStyle = -16, GwlExStyle = -20, GwlOwner = -8;
        private const long WsChild = 0x40000000L, WsPopup = 0x80000000L;
        private const long WsDisabled = 0x08000000L, WsVisible = 0x10000000L;
        private const long WsMinimizeBox = 0x00020000L, WsMaximizeBox = 0x00010000L;
        private const long WsExTopmost = 0x8L, WsExToolWindow = 0x80L;
        private const long WsExAppWindow = 0x00040000L, WsExNoActivate = 0x08000000L;
        private const uint SwpNoSize = 1, SwpNoMove = 2, SwpNoZOrder = 4;
        private const uint SwpNoActivate = 0x10, SwpFrameChanged = 0x20, SwpShowWindow = 0x40;
        private const uint SwpNoOwnerZOrder = 0x0200, SwpNoSendChanging = 0x0400;
        private const uint ZFlags = SwpNoMove | SwpNoSize | SwpNoOwnerZOrder |
            SwpNoActivate | SwpNoSendChanging;
        private const int WmWindowPosChanging = 0x0046, WmMouseActivate = 0x0021;
        private const int WmNcDestroy = 0x0082;
        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNotTopmost = new IntPtr(-2);
        private static readonly Dictionary<Window, Attachment> Attachments = new Dictionary<Window, Attachment>();

        public static event EventHandler WindowHandleLost;
        public static string LastError { get; private set; }

        public static bool Attach(Window window)
        {
            if (window == null) return false;
            window.Dispatcher.VerifyAccess();
            LastError = null;
            Attachment state;
            if (!Attachments.TryGetValue(window, out state))
            {
                state = new Attachment();
                Attachments.Add(window, state);
                window.SourceInitialized += OnSourceInitialized;
                window.Closing += OnClosing;
                window.Closed += OnClosed;
            }
            state.Requested = true;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                LastError = "The window source has not been initialized.";
                return false;
            }
            IntPtr desktop = FindDesktopHost();
            if (desktop == IntPtr.Zero)
            {
                LastError = "Explorer's visible desktop host was not found.";
                return false;
            }
            if (IsAttached(window))
            {
                SyncDesktopBand(window, state, true);
                return true;
            }
            state.Handle = hwnd;
            state.Desktop = desktop;
            if (!state.SavedStyles)
            {
                state.OriginalStyle = ReadLong(hwnd, GwlStyle).ToInt64();
                state.OriginalExStyle = ReadLong(hwnd, GwlExStyle).ToInt64();
                state.OriginalOwner = GetWindow(hwnd, 4);
                state.LastBounds = GetBounds(window);
                state.SavedStyles = true;
            }
            StopObserving(state);
            if (state.Source != null && state.Hook != null)
            {
                try { state.Source.RemoveHook(state.Hook); }
                catch (InvalidOperationException) { }
            }
            state.Source = null; state.Hook = null;
            // Keep WPF's original application-managed layered rendering path.
            // No SetParent / WS_CHILD / SetLayeredWindowAttributes is used.
            // An internal WPF hidden owner is unnecessary for a TOOLWINDOW and
            // would couple this window's show/hide and z-order to that owner.
            state.ManagingZ = true;
            try
            {
                long style = ReadLong(hwnd, GwlStyle).ToInt64();
                long extended = ReadLong(hwnd, GwlExStyle).ToInt64();
                if (!WriteLong(hwnd, GwlStyle, (style | WsPopup) & ~(WsChild | WsMinimizeBox | WsMaximizeBox)) ||
                    !WriteLong(hwnd, GwlExStyle,
                        (extended | WsExToolWindow | WsExNoActivate) & ~(WsExAppWindow | WsExTopmost)) ||
                    !WriteLong(hwnd, GwlOwner, 0))
                {
                    LastError = "Cannot configure the desktop window: " + Marshal.GetLastWin32Error();
                    Restore(window, state);
                    return false;
                }
            }
            finally { state.ManagingZ = false; }

            state.Source = HwndSource.FromHwnd(hwnd);
            if (state.Source == null)
            {
                LastError = "The WPF window source is unavailable.";
                Restore(window, state);
                return false;
            }
            state.Hook = delegate(IntPtr h, int message, IntPtr wp, IntPtr lp, ref bool handled)
            {
                if (message == WmNcDestroy && state.Requested && !state.Closing)
                    NotifyHandleLost(window, state);
                else if (message == WmMouseActivate && state.Requested)
                {
                    // Receive the click without activating/raising this desktop
                    // panel. Separate task editor windows retain normal focus.
                    handled = true;
                    return new IntPtr(3); // MA_NOACTIVATE, not MA_NOACTIVATEANDEAT
                }
                else if (message == WmWindowPosChanging && state.Requested && !state.ManagingZ)
                {
                    WindowPosition position = (WindowPosition)Marshal.PtrToStructure(lp, typeof(WindowPosition));
                    position.Flags |= SwpNoZOrder | SwpNoActivate;
                    Marshal.StructureToPtr(position, lp, false);
                }
                return IntPtr.Zero;
            };
            state.Source.AddHook(state.Hook);
            try
            {
                state.Sentinel = CreateHiddenWindow("CornerTodo.DesktopState");
                state.Anchor = CreateHiddenWindow("CornerTodo.DesktopAnchor");
                if (!Position(state.Sentinel.Handle, HwndBottom, 0, 0, 0, 0, ZFlags, "desktop sentinel") ||
                    !Position(state.Anchor.Handle, HwndBottom, 0, 0, 0, 0, ZFlags, "desktop anchor"))
                    throw new Win32Exception(LastError);
                state.WinEvent = delegate(IntPtr hook, uint ev, IntPtr foreground,
                    int objectId, int childId, uint thread, uint time)
                {
                    QueueSync(window, state);
                };
                // Include own-process foreground changes: an independently
                // activated TaskEditor must immediately lower the desktop panel.
                state.EventHook = SetWinEventHook(3, 3, IntPtr.Zero, state.WinEvent, 0, 0, 0);
                if (state.EventHook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot observe foreground changes.");
                state.Timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
                state.Timer.Interval = TimeSpan.FromMilliseconds(250);
                state.Timer.Tick += delegate { SyncDesktopBand(window, state, false); };
                state.Managed = true;
                if (!ApplyBounds(window, state.LastBounds, SwpFrameChanged))
                    throw new Win32Exception(LastError);
                SyncDesktopBand(window, state, true);
                if (!state.PositionHealthy) throw new Win32Exception(LastError);
                state.Timer.Start();
                return IsAttached(window);
            }
            catch (Exception ex)
            {
                StopObserving(state);
                Restore(window, state);
                LastError = "Cannot manage the desktop window: " + ex.Message;
                return false;
            }
        }

        public static bool IsAttached(Window window)
        {
            if (window == null) return false;
            window.Dispatcher.VerifyAccess();
            Attachment state;
            if (!Attachments.TryGetValue(window, out state) || !state.Requested || !state.Managed || !state.PositionHealthy) return false;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            return hwnd != IntPtr.Zero && hwnd == state.Handle && IsWindow(hwnd) &&
                (ReadLong(hwnd, GwlStyle).ToInt64() & WsChild) == 0 &&
                state.Sentinel != null && IsWindow(state.Sentinel.Handle) && state.EventHook != IntPtr.Zero;
        }

        public static void ReattachIfNeeded(Window window)
        {
            if (window == null) return;
            window.Dispatcher.VerifyAccess();
            Attachment state;
            if (!Attachments.TryGetValue(window, out state) || !state.Requested || state.Closing) return;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                if (state.Handle != IntPtr.Zero) NotifyHandleLost(window, state);
                return;
            }
            if (!IsAttached(window)) Attach(window);
            else SyncDesktopBand(window, state, false);
        }

        public static void Detach(Window window)
        {
            if (window == null) return;
            window.Dispatcher.VerifyAccess();
            Attachment state;
            if (!Attachments.TryGetValue(window, out state)) return;
            state.Requested = false;
            StopObserving(state);
            Restore(window, state);
            Forget(window, state);
        }

        public static string Diagnostics(Window window)
        {
            if (window == null) return "window=null";
            window.Dispatcher.VerifyAccess();
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return "hwnd=0; managedVisible=" + window.IsVisible;
            NativeRect rect; GetWindowRect(hwnd, out rect);
            int cloaked = -1;
            try { DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            Attachment state; Attachments.TryGetValue(window, out state);
            return String.Format("mode=top-level-desktop; hwnd={0}; parentOrOwner={1}; style=0x{2:X}; exStyle=0x{3:X}; nativeVisible={4}; managedVisible={5}; cloaked={6}; rect={7},{8},{9},{10}; boundsDip={11}; desktop={12}; showDesktop={13}; foreground={14}",
                hwnd, GetParent(hwnd), ReadLong(hwnd, GwlStyle).ToInt64(), ReadLong(hwnd, GwlExStyle).ToInt64(),
                IsWindowVisible(hwnd), window.IsVisible, cloaked, rect.Left, rect.Top, rect.Right, rect.Bottom,
                GetBounds(window), state == null ? IntPtr.Zero : state.Desktop,
                state != null && state.ShowDesktop, GetForegroundWindow());
        }

        public static Rect GetBounds(Window window)
        {
            if (window == null) return Rect.Empty;
            window.Dispatcher.VerifyAccess();
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            NativeRect rect;
            if (hwnd != IntPtr.Zero && IsWindow(hwnd) && !IsIconic(hwnd) && GetWindowRect(hwnd, out rect) &&
                rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                Matrix transform = DeviceTransform(window, false);
                return new Rect(transform.Transform(new Point(rect.Left, rect.Top)),
                    transform.Transform(new Point(rect.Right, rect.Bottom)));
            }
            Attachment state;
            if (Attachments.TryGetValue(window, out state) && !state.LastBounds.IsEmpty) return state.LastBounds;
            return new Rect(Finite(window.Left, 0), Finite(window.Top, 0),
                Math.Max(1, Finite(window.Width, 420)), Math.Max(1, Finite(window.Height, 480)));
        }

        public static void SetBounds(Window window, Rect screenBoundsInDip)
        {
            if (window == null || !ValidBounds(screenBoundsInDip)) return;
            window.Dispatcher.VerifyAccess();
            Attachment state;
            if (Attachments.TryGetValue(window, out state)) state.LastBounds = screenBoundsInDip;
            window.Width = screenBoundsInDip.Width; window.Height = screenBoundsInDip.Height;
            if (IsAttached(window))
            {
                if (!ApplyBounds(window, screenBoundsInDip, 0)) state.PositionHealthy = false;
            }
            else { window.Left = screenBoundsInDip.Left; window.Top = screenBoundsInDip.Top; }
        }

        public static Rect WorkArea(Window window)
        {
            if (window != null) window.Dispatcher.VerifyAccess();
            IntPtr monitor = MonitorFromPoint(new NativePoint(), 1);
            MonitorInfo info = new MonitorInfo(); info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                Matrix transform = DeviceTransform(window, false);
                return new Rect(transform.Transform(new Point(info.Work.Left, info.Work.Top)),
                    transform.Transform(new Point(info.Work.Right, info.Work.Bottom)));
            }
            return SystemParameters.WorkArea;
        }

        private static HwndSource CreateHiddenWindow(string name)
        {
            HwndSourceParameters parameters = new HwndSourceParameters(name);
            parameters.WindowStyle = unchecked((int)(WsPopup | WsDisabled));
            parameters.ExtendedWindowStyle = unchecked((int)(WsExToolWindow | WsExNoActivate));
            parameters.ParentWindow = IntPtr.Zero;
            parameters.Width = 1; parameters.Height = 1;
            return new HwndSource(parameters);
        }

        private static void SyncDesktopBand(Window window, Attachment state, bool force)
        {
            if (!state.Requested || !state.Managed || state.Closing || !IsWindow(state.Handle)) return;
            IntPtr desktop = FindDesktopHost();
            IntPtr foreground = GetForegroundWindow();
            bool foregroundIsDesktop = desktop != IntPtr.Zero && (foreground == desktop ||
                (foreground != IntPtr.Zero && GetAncestor(foreground, 2) == desktop));
            // A sentinel deliberately kept at the ordinary bottom ends up below
            // the shell when Show Desktop raises that shell over normal windows.
            // Looking at foreground alone would misclassify a plain desktop click.
            bool raised = foregroundIsDesktop && IsBelow(state.Sentinel.Handle, desktop);
            bool changed = raised != state.ShowDesktop || desktop != state.Desktop;
            state.Desktop = desktop; state.ShowDesktop = raised;
            if (!force && !changed && state.PositionHealthy && IsWindowVisible(state.Handle) && !IsIconic(state.Handle)) return;
            state.ManagingZ = true;
            bool success = true;
            try
            {
                if (window.IsVisible && (!IsWindowVisible(state.Handle) || IsIconic(state.Handle)))
                    ShowWindow(state.Handle, 4); // SW_SHOWNOACTIVATE
                if (raised)
                {
                    // This transient band is used only while the raised desktop
                    // itself is foreground. A foreground event from any app,
                    // including our TaskEditor, lowers both windows immediately.
                    success &= Position(state.Anchor.Handle, HwndTopmost, 0, 0, 0, 0, ZFlags, "raise desktop anchor");
                    IntPtr precedingTopmost = PreviousTopmost(desktop, state.Anchor.Handle);
                    if (precedingTopmost != IntPtr.Zero)
                        success &= Position(state.Anchor.Handle, precedingTopmost, 0, 0, 0, 0, ZFlags, "place desktop anchor");
                    IntPtr skinAfter = PreviousTopmost(state.Anchor.Handle, state.Handle);
                    if (skinAfter == IntPtr.Zero) skinAfter = HwndTopmost;
                    success &= Position(state.Handle, skinAfter, 0, 0, 0, 0, ZFlags | VisibleFlag(window), "show desktop widget");
                }
                else
                {
                    success &= Position(state.Anchor.Handle, HwndBottom, 0, 0, 0, 0, ZFlags, "lower desktop anchor");
                    success &= Position(state.Handle, HwndBottom, 0, 0, 0, 0, ZFlags | VisibleFlag(window), "lower desktop widget");
                }
            }
            finally { state.ManagingZ = false; state.PositionHealthy = success; }
        }

        private static uint VisibleFlag(Window window) { return window.IsVisible ? SwpShowWindow : 0; }

        private static bool IsBelow(IntPtr target, IntPtr start)
        {
            if (target == IntPtr.Zero || start == IntPtr.Zero) return false;
            IntPtr hwnd = GetWindow(start, 2);
            for (int count = 0; hwnd != IntPtr.Zero && count < 4096; count++, hwnd = GetWindow(hwnd, 2))
                if (hwnd == target) return true;
            return false;
        }

        private static IntPtr PreviousTopmost(IntPtr start, IntPtr exclude)
        {
            IntPtr hwnd = GetWindow(start, 3);
            for (int count = 0; hwnd != IntPtr.Zero && count < 4096; count++, hwnd = GetWindow(hwnd, 3))
                if (hwnd != exclude && (ReadLong(hwnd, GwlExStyle).ToInt64() & WsExTopmost) != 0) return hwnd;
            return IntPtr.Zero;
        }

        private static void QueueSync(Window window, Attachment state)
        {
            if (state.SyncQueued || window.Dispatcher.HasShutdownStarted) return;
            state.SyncQueued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
            {
                state.SyncQueued = false;
                Attachment current;
                if (Attachments.TryGetValue(window, out current) && current == state)
                    SyncDesktopBand(window, state, true);
            }));
        }

        private static bool ApplyBounds(Window window, Rect bounds, uint flags)
        {
            if (!ValidBounds(bounds)) return false;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
            Matrix transform = DeviceTransform(window, true);
            Point start = transform.Transform(bounds.TopLeft), end = transform.Transform(bounds.BottomRight);
            return Position(hwnd, IntPtr.Zero, (int)Math.Round(start.X), (int)Math.Round(start.Y),
                Math.Max(1, (int)Math.Round(end.X - start.X)), Math.Max(1, (int)Math.Round(end.Y - start.Y)),
                flags | SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate | SwpNoSendChanging, "position desktop widget");
        }

        private static bool Position(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags, string operation)
        {
            if (SetWindowPos(hwnd, after, x, y, width, height, flags)) return true;
            LastError = "Cannot " + operation + ": " + Marshal.GetLastWin32Error();
            return false;
        }

        private static void Restore(Window window, Attachment state)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (!state.SavedStyles || hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
            state.ManagingZ = true;
            try
            {
                SetWindowPos(hwnd, HwndNotTopmost, 0, 0, 0, 0, ZFlags);
                long visible = ReadLong(hwnd, GwlStyle).ToInt64() & WsVisible;
                WriteLong(hwnd, GwlStyle, (state.OriginalStyle & ~WsVisible) | visible);
                WriteLong(hwnd, GwlExStyle, state.OriginalExStyle & ~WsExTopmost);
                WriteLong(hwnd, GwlOwner, IsWindow(state.OriginalOwner) ? state.OriginalOwner.ToInt64() : 0);
                ApplyBounds(window, state.LastBounds, SwpFrameChanged);
            }
            finally { state.ManagingZ = false; }
        }

        private static void StopObserving(Attachment state)
        {
            state.Managed = false; state.PositionHealthy = false;
            if (state.Timer != null) { state.Timer.Stop(); state.Timer = null; }
            if (state.EventHook != IntPtr.Zero) { UnhookWinEvent(state.EventHook); state.EventHook = IntPtr.Zero; }
            state.WinEvent = null;
            if (state.Anchor != null) { state.Anchor.Dispose(); state.Anchor = null; }
            if (state.Sentinel != null) { state.Sentinel.Dispose(); state.Sentinel = null; }
        }

        private static IntPtr FindDesktopHost()
        {
            IntPtr shell = GetShellWindow(); uint shellPid;
            if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out shellPid) == 0) return IntPtr.Zero;
            if (IsWindowVisible(shell) && FindWindowEx(shell, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return shell;
            IntPtr desktop = IntPtr.Zero;
            EnumWindows(delegate(IntPtr candidate, IntPtr data)
            {
                if (!IsWindowVisible(candidate)) return true;
                uint pid; GetWindowThreadProcessId(candidate, out pid);
                if (pid != shellPid) return true;
                StringBuilder name = new StringBuilder(64); GetClassName(candidate, name, name.Capacity);
                if ((name.ToString() == "Progman" || name.ToString() == "WorkerW") &&
                    FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                { desktop = candidate; return false; }
                return true;
            }, IntPtr.Zero);
            return desktop;
        }

        private static Matrix DeviceTransform(Window window, bool toDevice)
        {
            if (window != null)
            {
                HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
                if (source != null && source.CompositionTarget != null)
                    return toDevice ? source.CompositionTarget.TransformToDevice : source.CompositionTarget.TransformFromDevice;
            }
            IntPtr dc = GetDC(IntPtr.Zero); double x = 1, y = 1;
            if (dc != IntPtr.Zero)
            {
                try { x = Math.Max(1, GetDeviceCaps(dc, 88)) / 96.0; y = Math.Max(1, GetDeviceCaps(dc, 90)) / 96.0; }
                finally { ReleaseDC(IntPtr.Zero, dc); }
            }
            return toDevice ? new Matrix(x, 0, 0, y, 0, 0) : new Matrix(1 / x, 0, 0, 1 / y, 0, 0);
        }

        private static bool ValidBounds(Rect b)
        {
            return !b.IsEmpty && !Double.IsNaN(b.X) && !Double.IsInfinity(b.X) &&
                !Double.IsNaN(b.Y) && !Double.IsInfinity(b.Y) &&
                !Double.IsNaN(b.Width) && !Double.IsInfinity(b.Width) && b.Width > 0 &&
                !Double.IsNaN(b.Height) && !Double.IsInfinity(b.Height) && b.Height > 0;
        }
        private static double Finite(double value, double fallback)
        { return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : value; }

        private static void OnSourceInitialized(object sender, EventArgs e)
        {
            Window window = (Window)sender; Attachment state;
            if (Attachments.TryGetValue(window, out state) && state.Requested) Attach(window);
        }
        private static void OnClosing(object sender, CancelEventArgs e)
        {
            Window window = (Window)sender; Attachment state;
            if (!Attachments.TryGetValue(window, out state)) return;
            state.Closing = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                Attachment current;
                if (Attachments.TryGetValue(window, out current) && current == state) current.Closing = false;
            }));
        }
        private static void OnClosed(object sender, EventArgs e)
        {
            Window window = (Window)sender; Attachment state;
            if (!Attachments.TryGetValue(window, out state)) return;
            if (state.Requested && !state.Closing) NotifyHandleLost(window, state);
            StopObserving(state); Forget(window, state);
        }
        private static void NotifyHandleLost(Window window, Attachment state)
        {
            if (state.LostRaised || window.Dispatcher.HasShutdownStarted) return;
            state.LostRaised = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                EventHandler handler = WindowHandleLost;
                if (handler != null) handler(window, EventArgs.Empty);
            }));
        }
        private static void Forget(Window window, Attachment state)
        {
            window.SourceInitialized -= OnSourceInitialized; window.Closing -= OnClosing; window.Closed -= OnClosed;
            if (state.Source != null && state.Hook != null)
            { try { state.Source.RemoveHook(state.Hook); } catch (InvalidOperationException) { } }
            Attachments.Remove(window);
        }
        private static IntPtr ReadLong(IntPtr hwnd, int index)
        { return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index)); }
        private static bool WriteLong(IntPtr hwnd, int index, long value)
        {
            SetLastError(0);
            IntPtr previous = IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, new IntPtr(value)) :
                new IntPtr(SetWindowLong32(hwnd, index, unchecked((int)value)));
            return previous != IntPtr.Zero || Marshal.GetLastWin32Error() == 0;
        }

        private sealed class Attachment
        {
            public bool Requested, Closing, LostRaised, SavedStyles, Managed, ManagingZ, SyncQueued, ShowDesktop, PositionHealthy;
            public IntPtr Handle, Desktop, OriginalOwner, EventHook;
            public long OriginalStyle, OriginalExStyle;
            public Rect LastBounds = Rect.Empty;
            public HwndSource Source, Sentinel, Anchor;
            public HwndSourceHook Hook;
            public WinEventProc WinEvent;
            public DispatcherTimer Timer;
        }
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
        { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
        { public IntPtr Hwnd, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    }
}
