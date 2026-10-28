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
            header.MouseLeftButtonDown += HeaderDown;
            header.MouseMove += HeaderMove;
            header.MouseLeftButtonUp += HeaderUp;
            header.LostMouseCapture += LostCapture;
            grip.DragStarted += GripStarted;
            grip.DragDelta += GripDelta;
            grip.DragCompleted += GripCompleted;
            window.MouseEnter += WindowEntered;
            window.MouseLeave += WindowLeft;
        }

        public bool IsReady { get { return initialized && !disposed; } }
        public bool IsAnimating { get { return !disposed && animationTimer.IsEnabled; } }
        public bool IsInteractionBlocked
        {
            get { return !disposed && (window.IsMouseOver || moving || resizing || externallyBlocked()); }
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
            Rect area = host.WorkArea();
            baseWidth = settings.Width; baseHeight = settings.Height;
            anchorRight = settings.Left.HasValue ? settings.Left.Value + baseWidth : area.Right - 24;
            anchorTop = settings.Top ?? (area.Top + 18);
            PanelBounds baseline = GrowthLayout.Target(ToPanel(area), anchorRight, anchorTop, baseWidth, baseHeight, 0, false);
            baseWidth = baseline.Width; baseHeight = baseline.Height;
            anchorRight = baseline.Right; anchorTop = baseline.Top;
            collapsed = settings.Collapsed;
            initialized = true;
        }

        public void UpdateExpansion(double progress, bool collapsed, bool immediate, bool allowDuringInteraction)
        {
            if (!CanUseHost) { StopAnimation(); return; }
            this.collapsed = collapsed;
            if (!allowDuringInteraction && IsInteractionBlocked) { StopAnimation(); return; }
            Rect area = host.WorkArea();
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
            UpdateExpansion(progress, collapsed, true, true);
            scheduleSave();
        }

        public void StopAnimation() { animationTimer.Stop(); }

        internal void BeginMove(Point screenPosition)
        {
            if (!CanUseHost || externallyBlocked()) return;
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
            scheduleSave(); resumeAttention();
        }

        internal void BeginResize()
        {
            if (!CanUseHost || collapsed || !grip.IsEnabled || externallyBlocked()) return;
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
            disposed = true; moving = false; resizing = false;
            animationTimer.Stop(); animationTimer.Tick -= AnimationTick;
            header.MouseLeftButtonDown -= HeaderDown; header.MouseMove -= HeaderMove;
            header.MouseLeftButtonUp -= HeaderUp; header.LostMouseCapture -= LostCapture;
            grip.DragStarted -= GripStarted; grip.DragDelta -= GripDelta; grip.DragCompleted -= GripCompleted;
            window.MouseEnter -= WindowEntered; window.MouseLeave -= WindowLeft;
            if (header.IsMouseCaptured) header.ReleaseMouseCapture();
        }

        private bool CanUseHost { get { return !disposed && initialized && !hostLost(); } }
        private void AnimationTick(object sender, EventArgs e) { AdvanceAnimation(); }
        private void GripStarted(object sender, DragStartedEventArgs e) { BeginResize(); }
        private void GripDelta(object sender, DragDeltaEventArgs e) { ResizeBy(e.HorizontalChange, e.VerticalChange); }
        private void GripCompleted(object sender, DragCompletedEventArgs e) { EndResize(); }
        private void WindowEntered(object sender, MouseEventArgs e) { StopAnimation(); }
        private void WindowLeft(object sender, MouseEventArgs e)
        { if (CanUseHost) resumeAttention(); }
        private void LostCapture(object sender, MouseEventArgs e)
        {
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
