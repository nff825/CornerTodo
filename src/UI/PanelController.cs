using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CornerTodo
{
    internal interface IPanelHost
    {
        Rect GetBounds();
        void SetBounds(Rect bounds);
        Rect WorkArea();
    }

    /// <summary>Only adapts the existing desktop implementation to one window.</summary>
    internal sealed class DesktopPanelHost : IPanelHost
    {
        private readonly Window window;
        public DesktopPanelHost(Window window)
        {
            if (window == null) throw new ArgumentNullException("window");
            this.window = window;
        }
        public Rect GetBounds() { return DesktopHost.GetBounds(window); }
        public void SetBounds(Rect bounds) { DesktopHost.SetBounds(window, bounds); }
        public Rect WorkArea() { return DesktopHost.WorkArea(window); }
    }

    /// <summary>
    /// Owns panel geometry and pointer/animation state. It never writes task data,
    /// changes desktop z-order, or chooses urgency; those remain caller concerns.
    /// </summary>
    internal sealed class PanelController : IDisposable
    {
        private readonly Window window;
        private readonly UIElement header;
        private readonly Thumb grip;
        private readonly IPanelHost host;
        private readonly Func<bool> externallyBlocked;
        private readonly Func<bool> hostLost;
        private readonly Action scheduleSave;
        private readonly Action resumeAttention;
        private readonly Func<DateTime> utcNow;
        private readonly DispatcherTimer animationTimer;
        private readonly DispatcherTimer edgeTimer;
        private const double EdgeThreshold = 32;
        private static readonly TimeSpan EdgeHideDelay = TimeSpan.FromMilliseconds(800);
        private bool disposed;
        private bool initialized;
        private bool moving;
        private bool resizing;
        private bool collapsed;
        private double baseWidth;
        private double baseHeight;
        private double anchorRight;
        private double anchorTop;
        private Point dragStart;
        private Rect dragBounds;
        private Rect animationStart;
        private Rect animationTarget;
        private DateTime animationStarted;
        private bool edgeAutoHide;
        private bool edgeDocked;
        private bool edgeHidden;
        private bool edgeHideArmed;
        private bool revealWaitsForEntry;
        private bool pointerInside;
        private DateTime? edgeHideDue;
        private double latestProgress;

        public PanelController(Window window, UIElement header, Thumb grip, IPanelHost host,
            Func<bool> externallyBlocked, Func<bool> hostLost, Action scheduleSave, Action resumeAttention)
            : this(window, header, grip, host, externallyBlocked, hostLost, scheduleSave, resumeAttention,
                delegate { return DateTime.UtcNow; }) { }

        internal PanelController(Window window, UIElement header, Thumb grip, IPanelHost host,
            Func<bool> externallyBlocked, Func<bool> hostLost, Action scheduleSave, Action resumeAttention,
            Func<DateTime> utcNow)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (header == null) throw new ArgumentNullException("header");
            if (grip == null) throw new ArgumentNullException("grip");
            if (host == null) throw new ArgumentNullException("host");
            if (externallyBlocked == null) throw new ArgumentNullException("externallyBlocked");
            if (hostLost == null) throw new ArgumentNullException("hostLost");
            if (scheduleSave == null) throw new ArgumentNullException("scheduleSave");
            if (resumeAttention == null) throw new ArgumentNullException("resumeAttention");
            if (utcNow == null) throw new ArgumentNullException("utcNow");
            this.window = window; this.header = header; this.grip = grip; this.host = host;
            this.externallyBlocked = externallyBlocked; this.hostLost = hostLost;
            this.scheduleSave = scheduleSave; this.resumeAttention = resumeAttention; this.utcNow = utcNow;
            animationTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
            animationTimer.Interval = TimeSpan.FromMilliseconds(40);
            animationTimer.Tick += AnimationTick;
            edgeTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
            edgeTimer.Interval = TimeSpan.FromMilliseconds(100);
            edgeTimer.Tick += EdgeTick;
            header.MouseLeftButtonDown += HeaderDown;
            header.MouseMove += HeaderMove;
            header.MouseLeftButtonUp += HeaderUp;
            header.LostMouseCapture += LostCapture;
            grip.DragStarted += GripStarted;
            grip.DragDelta += GripDelta;
            grip.DragCompleted += GripCompleted;
            window.MouseEnter += WindowEntered;
            window.MouseLeave += WindowLeft;
            window.MouseMove += WindowPointerMoved;
        }

        public bool IsReady { get { return initialized && !disposed; } }
        public bool IsAnimating { get { return !disposed && animationTimer.IsEnabled; } }
        public event Action<bool> EdgeHiddenChanged;
        public bool IsEdgeHidden { get { return !disposed && edgeHidden; } }
        internal bool IsEdgeDocked { get { return !disposed && edgeAutoHide && edgeDocked; } }
        internal bool IsEdgeHidePending { get { return !disposed && edgeHideDue.HasValue; } }
        internal bool IsEdgeTimerRunning { get { return !disposed && edgeTimer.IsEnabled; } }
        public bool IsInteractionBlocked
        {
            get { return !disposed && (window.IsMouseOver || pointerInside || moving || resizing || externallyBlocked()); }
        }
        public Rect BaselineBounds
        {
            get { return initialized ? new Rect(anchorRight - baseWidth, anchorTop, baseWidth, baseHeight) : Rect.Empty; }
        }

        public void Initialize(WidgetSettings settings)
        {
            if (disposed || hostLost()) return;
            if (settings == null) throw new ArgumentNullException("settings");
            StopAnimation();
            StopEdgeWatch(); edgeHideArmed = false; revealWaitsForEntry = false;
            Rect area = host.WorkArea();
            baseWidth = settings.Width; baseHeight = settings.Height;
            anchorRight = settings.Left.HasValue ? settings.Left.Value + baseWidth : area.Right - 24;
            anchorTop = settings.Top ?? (area.Top + 18);
            PanelBounds baseline = GrowthLayout.Target(ToPanel(area), anchorRight, anchorTop, baseWidth, baseHeight, 0, false);
            baseWidth = baseline.Width; baseHeight = baseline.Height;
            anchorRight = baseline.Right; anchorTop = baseline.Top;
            collapsed = settings.Collapsed;
            latestProgress = 0;
            edgeAutoHide = settings.EdgeAutoHide;
            edgeDocked = edgeAutoHide && NearRightEdge(area);
            if (edgeDocked) anchorRight = area.Right - 8;
            initialized = true;
            ChangeEdgeHidden(false);
            StartEdgeWatch();
        }

        public void UpdateExpansion(double progress, bool collapsed, bool immediate, bool allowDuringInteraction)
        {
            if (!CanUseHost) { StopAnimation(); StopEdgeWatch(); return; }
            latestProgress = progress;
            this.collapsed = collapsed;
            if (edgeHidden)
            {
                StopAnimation(); ApplyCapsuleBounds(host.WorkArea()); return;
            }
            if (!allowDuringInteraction && IsInteractionBlocked) { StopAnimation(); return; }
            Rect area = host.WorkArea();
            if (edgeAutoHide && edgeDocked) anchorRight = area.Right - 8;
            PanelBounds target = GrowthLayout.Target(ToPanel(area), anchorRight, anchorTop,
                baseWidth, baseHeight, progress, collapsed);
            anchorRight = target.Right; anchorTop = target.Top;
            Rect next = ToRect(target);
            if (immediate || collapsed)
            {
                StopAnimation(); host.SetBounds(next); return;
            }
            Rect current = host.GetBounds();
            if (Near(current, next)) { StopAnimation(); return; }
            if (animationTimer.IsEnabled && Near(animationTarget, next)) return;
            animationStart = current; animationTarget = next; animationStarted = utcNow();
            animationTimer.Start();
            Diagnostics.Log("Growth progress=" + progress.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                ", target=" + next + ", baseline=" + baseWidth + "x" + baseHeight);
        }

        public void ResetPosition(double progress, bool collapsed)
        {
            if (!CanUseHost) return;
            Rect area = host.WorkArea();
            StopAnimation(); anchorRight = area.Right - 24; anchorTop = area.Top + 18;
            edgeHideArmed = false; edgeHideDue = null; revealWaitsForEntry = true;
            edgeDocked = edgeAutoHide;
            ChangeEdgeHidden(false);
            UpdateExpansion(progress, collapsed, true, true);
            StartEdgeWatch();
            scheduleSave();
        }

        public void StopAnimation() { animationTimer.Stop(); }

        /// <summary>Enables the optional edge mode without changing task urgency or saved baseline dimensions.</summary>
        public void SetEdgeAutoHide(bool enabled)
        {
            if (disposed || edgeAutoHide == enabled) return;
            edgeAutoHide = enabled;
            edgeHideArmed = false; revealWaitsForEntry = false; StopEdgeWatch();
            if (!CanUseHost) return;
            bool wasHidden = edgeHidden;
            Rect area = host.WorkArea();
            edgeDocked = enabled && NearRightEdge(area);
            if (edgeDocked) anchorRight = area.Right - 8;
            if (!enabled) ChangeEdgeHidden(false);
            if (wasHidden || edgeDocked) UpdateExpansion(latestProgress, collapsed, true, true);
            StartEdgeWatch();
            if (edgeDocked) scheduleSave();
        }

        /// <summary>Explicit reveal. Merely hovering over the capsule never calls this.</summary>
        public void RevealFromEdge(double progress, bool collapsed)
        {
            if (!CanUseHost) return;
            edgeHideArmed = false; edgeHideDue = null; revealWaitsForEntry = true;
            ChangeEdgeHidden(false);
            UpdateExpansion(progress, collapsed, true, true);
            StartEdgeWatch();
        }

        /// <summary>Called by the short timer and tests; only a real leave event arms automatic hiding.</summary>
        public void PollEdgeAutoHide()
        {
            if (!CanUseHost) { StopAnimation(); StopEdgeWatch(); return; }
            if (!edgeAutoHide || !edgeDocked) { StopEdgeWatch(); return; }
            if (edgeHidden) { ApplyCapsuleBounds(host.WorkArea()); return; }
            if (!edgeHideArmed || revealWaitsForEntry || window.IsMouseOver || pointerInside || moving || resizing ||
                externallyBlocked() || (window.IsKeyboardFocusWithin && BlocksEdgeHideForKeyboard(Keyboard.FocusedElement as DependencyObject)))
            { edgeHideDue = null; return; }
            DateTime now = utcNow();
            if (!edgeHideDue.HasValue) { edgeHideDue = now.Add(EdgeHideDelay); return; }
            if (now < edgeHideDue.Value) return;
            StopAnimation(); edgeHideDue = null;
            ChangeEdgeHidden(true);
            ApplyCapsuleBounds(host.WorkArea());
        }

        internal static bool BlocksEdgeHideForKeyboard(DependencyObject focused)
        {
            while (focused != null)
            {
                if (focused is TextBoxBase || focused is ComboBox || focused is DatePicker) return true;
                focused = ParentOf(focused);
            }
            return false;
        }

        private bool NearRightEdge(Rect area) { return Math.Abs(area.Right - anchorRight) <= EdgeThreshold; }
        private void StartEdgeWatch()
        { if (CanUseHost && edgeAutoHide && edgeDocked) edgeTimer.Start(); }
        private void StopEdgeWatch() { edgeTimer.Stop(); edgeHideDue = null; }
        private void ChangeEdgeHidden(bool hidden)
        {
            if (edgeHidden == hidden) return;
            edgeHidden = hidden;
            Action<bool> changed = EdgeHiddenChanged;
            if (changed != null) changed(hidden);
        }
        private void ApplyCapsuleBounds(Rect area)
        {
            double width = Math.Min(48, Math.Max(1, area.Width));
            double height = Math.Min(152, Math.Max(1, area.Height));
            double top = Math.Max(area.Top, Math.Min(anchorTop, area.Bottom - height));
            Rect next = new Rect(area.Right - width, top, width, height);
            if (!Near(host.GetBounds(), next)) host.SetBounds(next);
        }

        internal void BeginMove(Point screenPosition)
        {
            if (!CanUseHost || edgeHidden || externallyBlocked()) return;
            edgeHideArmed = false; edgeHideDue = null;
            moving = true; StopAnimation(); dragStart = screenPosition; dragBounds = host.GetBounds();
        }

        internal void MoveTo(Point screenPosition)
        {
            if (!CanUseHost || !moving) return;
            Rect bounds = dragBounds;
            bounds.X += screenPosition.X - dragStart.X;
            bounds.Y += screenPosition.Y - dragStart.Y;
            bounds = Clamp(bounds, host.WorkArea());
            host.SetBounds(bounds); anchorRight = bounds.Right; anchorTop = bounds.Top;
        }

        internal void EndMove()
        {
            bool wasMoving = moving; moving = false;
            if (!wasMoving || !CanUseHost) return;
            if (edgeAutoHide)
            {
                Rect area = host.WorkArea();
                edgeDocked = NearRightEdge(area);
                if (edgeDocked) UpdateExpansion(latestProgress, collapsed, true, true);
                else StopEdgeWatch();
                StartEdgeWatch();
            }
            scheduleSave(); resumeAttention();
        }

        internal void BeginResize()
        {
            if (!CanUseHost || edgeHidden || collapsed || !grip.IsEnabled || externallyBlocked()) return;
            edgeHideArmed = false; edgeHideDue = null;
            resizing = true; StopAnimation();
        }

        internal void ResizeBy(double horizontal, double vertical)
        {
            if (!CanUseHost || !resizing || collapsed || !grip.IsEnabled) return;
            Rect bounds = host.GetBounds(); Rect area = host.WorkArea();
            bounds.Width = Math.Min(Math.Max(320, bounds.Width + horizontal), Math.Min(area.Width * 0.5, area.Right - bounds.Left - 8));
            bounds.Height = Math.Min(Math.Max(280, bounds.Height + vertical), area.Bottom - bounds.Top - 8);
            bounds.Height = Math.Min(bounds.Height, area.Height * (1100.0 / 1528.0));
            baseWidth = bounds.Width; baseHeight = bounds.Height;
            anchorRight = bounds.Right; anchorTop = bounds.Top;
            host.SetBounds(bounds);
        }

        internal void EndResize()
        {
            bool wasResizing = resizing; resizing = false;
            if (!wasResizing || !CanUseHost) return;
            if (edgeAutoHide)
            {
                edgeDocked = NearRightEdge(host.WorkArea());
                if (!edgeDocked) StopEdgeWatch();
                StartEdgeWatch();
            }
            scheduleSave(); resumeAttention();
        }

        internal void AdvanceAnimation()
        {
            if (!IsAnimating) return;
            if (!CanUseHost || IsInteractionBlocked) { StopAnimation(); return; }
            double time = Math.Max(0, Math.Min(1, (utcNow() - animationStarted).TotalSeconds / 3.0));
            double blend = time * time * (3 - 2 * time);
            Rect next = new Rect(
                animationStart.Left + (animationTarget.Left - animationStart.Left) * blend,
                animationStart.Top + (animationTarget.Top - animationStart.Top) * blend,
                animationStart.Width + (animationTarget.Width - animationStart.Width) * blend,
                animationStart.Height + (animationTarget.Height - animationStart.Height) * blend);
            host.SetBounds(next);
            if (time >= 1) StopAnimation();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; moving = false; resizing = false; edgeHidden = false;
            StopEdgeWatch(); edgeTimer.Tick -= EdgeTick;
            EdgeHiddenChanged = null;
            animationTimer.Stop(); animationTimer.Tick -= AnimationTick;
            header.MouseLeftButtonDown -= HeaderDown; header.MouseMove -= HeaderMove;
            header.MouseLeftButtonUp -= HeaderUp; header.LostMouseCapture -= LostCapture;
            grip.DragStarted -= GripStarted; grip.DragDelta -= GripDelta; grip.DragCompleted -= GripCompleted;
            window.MouseEnter -= WindowEntered; window.MouseLeave -= WindowLeft; window.MouseMove -= WindowPointerMoved;
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
        }

        private bool CanUseHost { get { return !disposed && initialized && !hostLost(); } }
        private void AnimationTick(object sender, EventArgs e) { AdvanceAnimation(); }
        private void EdgeTick(object sender, EventArgs e) { PollEdgeAutoHide(); }
        private void GripStarted(object sender, DragStartedEventArgs e) { BeginResize(); }
        private void GripDelta(object sender, DragDeltaEventArgs e) { ResizeBy(e.HorizontalChange, e.VerticalChange); }
        private void GripCompleted(object sender, DragCompletedEventArgs e) { EndResize(); }
        private void WindowEntered(object sender, MouseEventArgs e)
        {
            pointerInside = true; edgeHideDue = null; StopAnimation();
            if (!edgeHidden) revealWaitsForEntry = false;
        }
        private void WindowPointerMoved(object sender, MouseEventArgs e)
        {
            // A revealed panel may already contain the pointer, so no enter
            // event is guaranteed. A move inside it also confirms actual use.
            if (!edgeHidden) revealWaitsForEntry = false;
        }
        private void WindowLeft(object sender, MouseEventArgs e)
        {
            pointerInside = false;
            if (!CanUseHost) return;
            if (edgeAutoHide && edgeDocked && !edgeHidden && !revealWaitsForEntry)
            { edgeHideArmed = true; edgeHideDue = null; PollEdgeAutoHide(); }
            resumeAttention();
        }
        private void LostCapture(object sender, MouseEventArgs e)
        {
            if (moving && edgeAutoHide) { EndMove(); return; }
            bool wasMoving = moving; moving = false;
            if (wasMoving && CanUseHost) resumeAttention();
        }

        private void HeaderDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit != header)
            {
                if (hit is ButtonBase) return;
                hit = ParentOf(hit);
            }
            if (!CanUseHost || externallyBlocked()) return;
            BeginMove(ScreenPointer(e));
            if (moving) { header.CaptureMouse(); e.Handled = true; }
        }
        private void HeaderMove(object sender, MouseEventArgs e)
        {
            if (!moving || e.LeftButton != MouseButtonState.Pressed || !CanUseHost) return;
            MoveTo(ScreenPointer(e));
        }
        private void HeaderUp(object sender, MouseButtonEventArgs e)
        {
            if (!moving) return;
            EndMove();
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
            e.Handled = true;
        }
        private Point ScreenPointer(MouseEventArgs e)
        {
            Point pixels = window.PointToScreen(e.GetPosition(window));
            PresentationSource source = PresentationSource.FromVisual(window);
            return source != null && source.CompositionTarget != null ?
                source.CompositionTarget.TransformFromDevice.Transform(pixels) : pixels;
        }
        private static DependencyObject ParentOf(DependencyObject item)
        {
            if (item is Visual || item is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(item);
            ContentElement content = item as ContentElement;
            return content != null ? ContentOperations.GetParent(content) : LogicalTreeHelper.GetParent(item);
        }
        private static bool Near(Rect left, Rect right)
        {
            return Math.Abs(left.Left - right.Left) < 0.8 && Math.Abs(left.Top - right.Top) < 0.8 &&
                Math.Abs(left.Width - right.Width) < 0.8 && Math.Abs(left.Height - right.Height) < 0.8;
        }
        private static PanelBounds ToPanel(Rect value) { return new PanelBounds(value.Left, value.Top, value.Width, value.Height); }
        private static Rect ToRect(PanelBounds value) { return new Rect(value.Left, value.Top, value.Width, value.Height); }
        private static Rect Clamp(Rect bounds, Rect area)
        {
            bounds.Width = Math.Min(bounds.Width, area.Width - 16); bounds.Height = Math.Min(bounds.Height, area.Height - 16);
            bounds.X = Math.Max(area.Left + 8, Math.Min(bounds.Left, area.Right - bounds.Width - 8));
            bounds.Y = Math.Max(area.Top + 8, Math.Min(bounds.Top, area.Bottom - bounds.Height - 8));
            return bounds;
        }
    }
}
