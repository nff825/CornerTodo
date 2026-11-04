using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CornerTodo
{
    /// <summary>One app-owned card; it never attaches to the desktop or activates on automatic show.</summary>
    internal sealed class ReminderWindow : Window
    {
        private readonly StackPanel rows;
        private readonly TextBlock count;
        private readonly TextBlock error;
        private readonly ScrollViewer scroll;
        private readonly Func<DateTime> now;
        private readonly Style secondaryStyle;
        private readonly List<TodoItem> renderedItems = new List<TodoItem>();
        private readonly List<TextBlock> remainingLabels = new List<TextBlock>();
        private Action<TodoItem> view, complete;
        internal event Action DismissRequested;
        internal ScrollViewer ReminderScroll { get { return scroll; } }
        internal StackPanel ReminderRows { get { return rows; } }
        internal TextBlock ErrorText { get { return error; } }

        internal ReminderWindow(Func<DateTime> now)
        {
            if (now == null) throw new ArgumentNullException("now");
            this.now = now;
            secondaryStyle = new PanelPalette(AppearanceSettings.CreateLight()).ButtonStyle;
            Title = "CornerTodo · 事项提醒"; Width = 400; SizeToContent = SizeToContent.Height;
            MinWidth = 260; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent;
            FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = Theme.Ink;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            var surface = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Brush("#F4F9FB"),
                BorderBrush = Theme.Brush("#9CB7C4"), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 14) };
            var root = new StackPanel(); surface.Child = root; Content = surface;
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = Theme.Text("CornerTodo · 事项提醒", 13, Theme.Ink); heading.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(heading);
            var close = new Button { Content = "×", Width = 28, Height = 28, FontSize = 20, Padding = new Thickness(0),
                Style = Theme.FlatButton(), ToolTip = "关闭本次提示，不改变事项状态", Tag = "reminder-dismiss" };
            AutomationProperties.SetName(close, "关闭提醒，不改变事项状态");
            close.Click += delegate { Action handler = DismissRequested; if (handler != null) handler(); };
            Grid.SetColumn(close, 1); header.Children.Add(close); root.Children.Add(header);
            count = Theme.Text("", 12, Theme.Muted); count.Margin = new Thickness(0, 4, 0, 2);
            root.Children.Add(count);
            rows = new StackPanel();
            scroll = new ScrollViewer { Content = rows, MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Style = Theme.PanelScrollViewer(), Foreground = Theme.Muted };
            AutomationProperties.SetName(scroll, "提醒事项列表"); root.Children.Add(scroll);
            error = Theme.Text("", 12, Theme.Warning); error.Visibility = Visibility.Collapsed; error.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(error);
        }

        internal void UpdateItems(IList<TodoItem> items, Action<TodoItem> view, Action<TodoItem> complete)
        {
            if (items == null) throw new ArgumentNullException("items");
            this.view = view; this.complete = complete;
            // Keep active click targets and scroll position stable on timer-only refreshes.
            bool unchanged = items.Count == renderedItems.Count;
            for (int index = 0; unchanged && index < items.Count; index++) {
                TodoItem next = items[index], previous = renderedItems[index];
                unchanged = next.Id == previous.Id && next.ReminderPlanId == previous.ReminderPlanId
                    && next.Text == previous.Text && next.DueDate == previous.DueDate;
            }
            if (unchanged) {
                for (int index = 0; index < items.Count; index++) remainingLabels[index].Text = Remaining(items[index], now());
                return;
            }
            double offset = scroll.VerticalOffset;
            renderedItems.Clear(); remainingLabels.Clear();
            rows.Children.Clear();
            count.Text = items.Count > 1 ? "共 " + items.Count + " 项待处理提醒" : "";
            count.Visibility = items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            for (int index = 0; index < items.Count; index++) {
                if (index > 0) rows.Children.Add(new Border { Height = 1, Background = Theme.Brush("#D4E2E8"), Margin = new Thickness(0, 13, 0, 10) });
                TodoItem item = TodoStateCopy.Copy(items[index]);
                renderedItems.Add(item);
                var row = new StackPanel { Margin = new Thickness(0, 5, 0, 0), Tag = item.Id };
                var title = Theme.Text(item.Text, 17, Theme.Ink); title.FontWeight = FontWeights.SemiBold; row.Children.Add(title);
                var remaining = Theme.Text(Remaining(item, now()), 13, Theme.Muted); remaining.Margin = new Thickness(0, 8, 0, 0); row.Children.Add(remaining);
                remainingLabels.Add(remaining);
                DateTime due = item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
                var deadline = Theme.Text(due.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture) + " 截止", 12, Theme.Muted);
                deadline.Margin = new Thickness(0, 3, 0, 10); row.Children.Add(deadline);
                var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); actions.ColumnDefinitions.Add(new ColumnDefinition());
                var open = new Button { Content = "查看事项", Style = secondaryStyle, Background = Brushes.White, Padding = new Thickness(7, 6, 7, 6),
                    BorderBrush = Theme.Brush("#9CB7C4"), BorderThickness = new Thickness(1), Tag = "reminder-view" };
                open.Click += delegate { if (this.view != null) this.view(item); };
                var finish = new Button { Content = "标记完成", Style = Theme.PrimaryButton(), Padding = new Thickness(7, 6, 7, 6), Tag = "reminder-complete" };
                finish.Click += delegate { if (this.complete != null) this.complete(item); };
                actions.Children.Add(open); Grid.SetColumn(finish, 2); actions.Children.Add(finish); row.Children.Add(actions);
                rows.Children.Add(row);
            }
            scroll.ScrollToVerticalOffset(offset);
        }

        internal void ShowFailure(string text) { error.Text = text; error.Visibility = Visibility.Visible; }
        internal void FitTo(Rect area)
        {
            double availableWidth = Math.Max(1, area.Width - 24), availableHeight = Math.Max(1, area.Height - 24);
            MinWidth = Math.Min(260, availableWidth); MaxWidth = availableWidth; Width = Math.Min(400, availableWidth);
            MaxHeight = availableHeight; scroll.MaxHeight = Math.Max(40, Math.Min(390, availableHeight - 110));
            Left = Math.Max(area.Left + 12, area.Right - Width - 16);
            UpdateLayout();
            double height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
            if (height <= 0) height = Math.Min(240, availableHeight);
            Top = Math.Max(area.Top + 12, area.Bottom - Math.Min(height, availableHeight) - 16);
        }

        internal static string Remaining(TodoItem item, DateTime stamp)
        {
            TimeSpan remaining = ReminderRules.ToUtc(item.DueDate) - ReminderRules.ToUtc(stamp);
            if (remaining <= TimeSpan.Zero) return remaining > TimeSpan.FromMinutes(-1) ? "已到截止时间" : "已逾期" + Duration((long)Math.Ceiling(-remaining.TotalMinutes));
            if (remaining.TotalMinutes < 1) return "距截止不足1分钟";
            return "距截止还有" + Duration((long)Math.Ceiling(remaining.TotalMinutes));
        }
        private static string Duration(long minutes)
        {
            long days = minutes / 1440, hours = (minutes % 1440) / 60, rest = minutes % 60;
            return (days > 0 ? days + "天" : "") + (hours > 0 ? hours + "小时" : "") + (rest > 0 ? rest + "分钟" : "");
        }
    }

    internal sealed class ReminderWindowPresenter : IReminderPresenter
    {
        private readonly Func<Rect> workArea;
        private readonly Func<DateTime> now;
        private ReminderWindow window;
        private Action dismissed;
        private bool clearing, disposed;
        internal ReminderWindowPresenter(Func<Rect> workArea, Func<DateTime> now) { this.workArea = workArea; this.now = now; }
        public void Present(IList<TodoItem> items, Action<TodoItem> view, Action<TodoItem> complete, Action dismissed)
        {
            if (disposed) return;
            this.dismissed = dismissed;
            if (window == null) {
                window = new ReminderWindow(now);
                window.DismissRequested += UserDismissed;
                window.Closed += WindowClosed;
            }
            window.UpdateItems(items, view, complete); window.FitTo(workArea());
            if (!window.IsVisible) window.Show();
            window.FitTo(workArea());
        }
        private void UserDismissed() { Action handler = dismissed; Clear(); if (handler != null) handler(); }
        private void WindowClosed(object sender, EventArgs e)
        {
            if (!Object.ReferenceEquals(window, sender)) return;
            window = null;
            if (!clearing) { Action handler = dismissed; if (handler != null) handler(); }
        }
        public void Hide() { if (window != null && window.IsVisible) window.Hide(); }
        public void Clear()
        {
            ReminderWindow existing = window;
            if (existing == null) return;
            window = null; clearing = true;
            try { existing.DismissRequested -= UserDismissed; existing.Closed -= WindowClosed; existing.Close(); }
            finally { clearing = false; }
        }
        public void ShowError(string text) { if (window != null) window.ShowFailure(text); }
        public void Dispose() { if (disposed) return; disposed = true; Clear(); dismissed = null; }
    }
}
