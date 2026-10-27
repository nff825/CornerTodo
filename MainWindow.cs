using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CornerTodo
{
    internal sealed class MainWindow : Window
    {
        private readonly TodoStore store;
        private TodoState state;
        private readonly bool windowed;
        private readonly bool preview;
        private readonly IStartupManager startupManager;
        private readonly Border surface;
        private readonly Grid root;
        private readonly ScrollViewer scroll;
        private readonly StackPanel list;
        private readonly TextBlock dateText;
        private readonly TextBlock title;
        private readonly TextBlock attentionText;
        private readonly TextBlock status;
        private readonly Button collapse;
        private readonly Button more;
        private readonly Border toolbarSurface;
        private readonly Button add;
        private readonly Thumb grip;
        private readonly DispatcherTimer saveTimer;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer hostTimer;
        private readonly DispatcherTimer animationTimer;
        private bool ready;
        private bool moving;
        private Point dragStart;
        private Rect dragBounds;
        private double baseWidth;
        private double baseHeight;
        private double anchorRight;
        private double anchorTop;
        private Rect animationStart;
        private Rect animationTarget;
        private DateTime animationStarted;
        private bool resizing;
        private bool menuOpen;
        private bool dialogOpen;
        private bool deferredRender;
        private bool resumeQueued;
        private AppearanceSettings appearancePreview;
        private PanelPalette palette;
        private string dateStamp;
        private bool attachScheduled;
        private bool waitingForDesktop;
        private const string DesktopPendingMessage = "暂时无法连接桌面层。当前以普通窗口显示。";
        public bool HostLost { get; set; }

        public MainWindow(TodoStore store, TodoState state, bool windowed, bool preview)
            : this(store, state, windowed, preview, null)
        {
        }

        internal MainWindow(TodoStore store, TodoState state, bool windowed, bool preview, IStartupManager startupManager)
        {
            this.store = store; this.state = state; this.windowed = windowed; this.preview = preview;
            if (startupManager != null) this.startupManager = startupManager;
            else {
                try {
                    this.startupManager = new StartupManager(System.Reflection.Assembly.GetExecutingAssembly().Location,
                        System.IO.Path.GetDirectoryName(store.DataFilePath));
                }
                catch (Exception ex) { this.startupManager = new UnavailableStartupManager(ex); }
            }
            Title = "桌面待办"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = windowed;
            ShowActivated = windowed; Topmost = false;
            Width = state.Settings.Width; Height = state.Settings.Height;
            baseWidth = Width; baseHeight = Height;
            FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = Theme.Ink;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            surface = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(19, 15, 17, 12) };
            Content = surface;
            root = new Grid(); surface.Child = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid { Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            title = Theme.Text("待办清单", 25, Theme.Ink); title.FontWeight = FontWeights.Bold;
            dateText = Theme.Text("", 12, Theme.Muted); dateText.Margin = new Thickness(0, 4, 0, 0);
            attentionText = Theme.Text("", 12, Theme.Muted); attentionText.Margin = new Thickness(0, 7, 0, 0);
            titles.Children.Add(title); titles.Children.Add(dateText); titles.Children.Add(attentionText); header.Children.Add(titles);
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            collapse = new Button { Content = "⌃", FontSize = 21, Style = Theme.FlatButton(), Width = 34, Height = 32, ToolTip = "收起面板" };
            collapse.Click += delegate { TogglePanel(); };
            more = new Button { Content = "⋯", FontSize = 22, Style = Theme.FlatButton(), Width = 34, Height = 32, ToolTip = "更多" };
            AutomationProperties.SetName(more, "更多操作");
            more.Click += delegate { var menu = BuildMenu(); menu.PlacementTarget = more; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
            toolbar.Children.Add(collapse); toolbar.Children.Add(more);
            toolbarSurface = new Border { Child = toolbar, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(toolbarSurface, 1); header.Children.Add(toolbarSurface);
            header.MouseLeftButtonDown += HeaderDown; header.MouseMove += HeaderMove; header.MouseLeftButtonUp += HeaderUp;
            header.LostMouseCapture += delegate { moving = false; ResumeAttention(); };
            root.Children.Add(header);
            list = new StackPanel();
            scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            add = new Button { Content = "+  添加待办", FontSize = 15, Style = Theme.FlatButton(), HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0, 7, 12, 7) };
            add.Click += delegate { Edit(null); }; footer.Children.Add(add);
            grip = new Thumb { Width = 20, Height = 20, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Path Data='M 10,17 L 17,10 M 14,17 L 17,14' Stroke='#708797' StrokeThickness='1.2' Opacity='0.55'/></Grid></ControlTemplate>") };
            grip.DragStarted += delegate { resizing = true; StopAnimation(); };
            grip.DragDelta += Resize;
            grip.DragCompleted += delegate { resizing = false; ScheduleSave(); ResumeAttention(); }; footer.Children.Add(grip);
            Grid.SetRow(footer, 2); root.Children.Add(footer);
            status = Theme.Text("", 11, Theme.Warning); status.Visibility = Visibility.Collapsed; status.Margin = new Thickness(0, 5, 0, 0);
            Grid.SetRow(status, 3); root.Children.Add(status);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveGeometry(); };
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            refreshTimer.Tick += delegate {
                if (dateStamp != DateTime.Now.ToString("yyyyMMddHHmm")) {
                    if (InteractionBlocked) { deferredRender = true; UpdateHeader(DateTime.Now); }
                    else Render();
                }
                else UpdateExpansion(false, false);
            };
            hostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            hostTimer.Tick += delegate {
                if (!windowed) {
                    DesktopHost.ReattachIfNeeded(this);
                    if (waitingForDesktop && DesktopHost.IsAttached(this)) ClearDesktopPending();
                }
            };
            animationTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
            animationTimer.Tick += AnimationTick;
            MouseEnter += delegate { StopAnimation(); };
            MouseLeave += delegate { ResumeAttention(); };
            Loaded += LoadedWindow;
            ContentRendered += delegate {
                Diagnostics.Log("ContentRendered " + DesktopHost.Diagnostics(this) + ", WPF-visible=" + IsVisible + ", content=" + root.ActualWidth + "x" + root.ActualHeight);
                if (!windowed && !attachScheduled) {
                    attachScheduled = true;
                    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(AttachAfterRender));
                }
            };
            Closed += delegate { ready = false; saveTimer.Stop(); refreshTimer.Stop(); hostTimer.Stop(); animationTimer.Stop(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Edit(null); e.Handled = true; }
            };
            surface.ContextMenu = BuildMenu();
            Render();
        }

        private void LoadedWindow(object sender, RoutedEventArgs e)
        {
            Diagnostics.Log("Loaded main window");
            Rect area = DesktopHost.WorkArea(this);
            baseWidth = state.Settings.Width; baseHeight = state.Settings.Height;
            anchorRight = state.Settings.Left.HasValue ? state.Settings.Left.Value + baseWidth : area.Right - 24;
            anchorTop = state.Settings.Top ?? (area.Top + 18);
            PanelBounds baseline = GrowthLayout.Target(ToPanel(area), anchorRight, anchorTop, baseWidth, baseHeight, 0, false);
            baseWidth = baseline.Width; baseHeight = baseline.Height;
            anchorRight = baseline.Right; anchorTop = baseline.Top;
            ready = true; ApplyCollapsed(); UpdateExpansion(true, true); refreshTimer.Start(); hostTimer.Start();
            if (!String.IsNullOrEmpty(store.LoadWarning))
            {
                ShowStatus("已恢复数据，请查看提示。");
                MessageBox.Show(store.LoadWarning, "待办数据恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AttachAfterRender()
        {
            Rect bounds = DesktopHost.GetBounds(this);
            bool attached = DesktopHost.Attach(this);
            DesktopHost.SetBounds(this, bounds);
            Diagnostics.Log("Attach after render: " + attached + ", bounds=" + DesktopHost.GetBounds(this) + ", error=" + DesktopHost.LastError);
            if (!attached) { waitingForDesktop = true; ShowStatus(DesktopPendingMessage); }
            else ClearDesktopPending();
        }

        private void ClearDesktopPending()
        {
            waitingForDesktop = false;
            if (status.Text == DesktopPendingMessage) status.Visibility = Visibility.Collapsed;
        }

        public void RestoreToDesktop()
        {
            Diagnostics.Log("Existing instance restore requested");
            if (state.Settings.Collapsed) TogglePanel();
            Show(); WindowState = WindowState.Normal; ResetPosition();
            if (!windowed) AttachAfterRender();
            InvalidateVisual(); UpdateLayout();
        }

        private void Render()
        {
            DateTime now = DateTime.Now;
            dateStamp = now.ToString("yyyyMMddHHmm"); deferredRender = false;
            ApplyPanelPalette();
            UpdateHeader(now);
            double previousScroll = scroll.VerticalOffset;
            list.Children.Clear();
            var pending = TodoRules.Pending(state).ToList();
            var completed = TodoRules.Completed(state).ToList();
            var heading = Theme.Text("未完成  " + pending.Count, 14, palette.Ink); heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 3, 0, 7); list.Children.Add(heading);
            if (pending.Count == 0)
            {
                var empty = Theme.Text(state.Items.Count == 0 ? "把接下来要做的事，记在这里。" : "当前待办都已完成。", 14, palette.Muted);
                empty.Margin = new Thickness(0, 15, 0, 22); list.Children.Add(empty);
            }
            foreach (var item in pending) list.Children.Add(TaskRow(item, now));
            list.Children.Add(new Border { Height = 1, Background = palette.Separator, Margin = new Thickness(0, 14, 0, 11) });
            var completedToggle = new Button { Content = (state.Settings.CompletedCollapsed ? "›  " : "⌄  ") + "已完成  " + completed.Count,
                Style = palette.ButtonStyle, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = palette.Muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 3, 10, 7) };
            AutomationProperties.SetName(completedToggle, state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成");
            completedToggle.Click += delegate { var next = Clone(); next.Settings.CompletedCollapsed = !next.Settings.CompletedCollapsed; Commit(next); };
            list.Children.Add(completedToggle);
            if (!state.Settings.CompletedCollapsed)
            {
                foreach (var item in completed) list.Children.Add(TaskRow(item, now));
                if (completed.Count == 0)
                {
                    var empty = Theme.Text("完成的事项会保留在这里", 12, palette.Muted); empty.Margin = new Thickness(0, 5, 0, 10); list.Children.Add(empty);
                }
            }
            ApplyCollapsed();
            scroll.ScrollToVerticalOffset(previousScroll);
            UpdateExpansion(false, false);
        }

        private FrameworkElement TaskRow(TodoItem item, DateTime now)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 7), Background = Brushes.Transparent, Tag = item.Id };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var check = new CheckBox { Style = palette.CheckStyle, IsChecked = item.IsCompleted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0) };
            AutomationProperties.SetName(check, (item.IsCompleted ? "恢复未完成：" : "完成：") + item.Text);
            check.Click += delegate {
                var next = Clone(); var changed = next.Items.First(t => t.Id == item.Id);
                changed.IsCompleted = !item.IsCompleted; changed.CompletedAt = changed.IsCompleted ? (DateTime?)DateTime.Now : null;
                if (!Commit(next)) check.IsChecked = item.IsCompleted;
            };
            row.Children.Add(check);
            var textArea = new StackPanel { Margin = new Thickness(0, 2, 5, 0) };
            bool overdue = TodoRules.IsOverdue(item, now);
            bool urgent = TodoRules.IsUrgent(item, now);
            var text = Theme.Text(item.Text, overdue ? 21 : urgent ? 19 : 15, item.IsCompleted ? palette.Muted : palette.Ink);
            if (overdue || urgent) text.FontWeight = FontWeights.SemiBold;
            if (item.IsCompleted) text.TextDecorations = TextDecorations.Strikethrough;
            textArea.Children.Add(text);
            var due = Theme.Text(TodoRules.FormatAttention(item, now), overdue || urgent ? 13 : 12, overdue ? palette.Warning : urgent ? palette.Urgent : palette.Muted);
            due.Margin = new Thickness(0, 4, 0, 0);
            textArea.Children.Add(due); Grid.SetColumn(textArea, 1); row.Children.Add(textArea);
            DateTime localDue = item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
            row.ToolTip = item.Text + "\n截止：" + localDue.ToString(item.HasTime ? "yyyy年M月d日 HH:mm" : "yyyy年M月d日");
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "编辑" }; edit.Click += delegate { Edit(item); };
            var toggle = new MenuItem { Header = item.IsCompleted ? "恢复为未完成" : "标记为已完成" };
            toggle.Click += delegate { var next = Clone(); var changed = next.Items.First(t => t.Id == item.Id); changed.IsCompleted = !item.IsCompleted; changed.CompletedAt = changed.IsCompleted ? (DateTime?)DateTime.Now : null; Commit(next); };
            var delete = new MenuItem { Header = "删除" }; delete.Click += delegate { Delete(item); };
            menu.Items.Add(edit); menu.Items.Add(toggle); menu.Items.Add(new Separator()); menu.Items.Add(delete); TrackMenu(menu); row.ContextMenu = menu;
            textArea.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) { Edit(item); e.Handled = true; } };
            return row;
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var loginStartup = new MenuItem { Header = "开机自启（登录后）", IsCheckable = true, Tag = "login-startup" };
            var repairStartup = new MenuItem { Header = "更新自启位置为当前程序", Tag = "repair-startup", Visibility = Visibility.Collapsed };
            loginStartup.Click += delegate {
                bool requested = loginStartup.IsChecked;
                if (!TrySetStartup(requested)) loginStartup.IsChecked = !requested;
                RefreshStartupMenu(loginStartup, repairStartup);
            };
            repairStartup.Click += delegate { TrySetStartup(true); RefreshStartupMenu(loginStartup, repairStartup); };
            menu.Opened += delegate {
                foreach (var menuItem in menu.Items.OfType<MenuItem>()) {
                    if (Object.Equals(menuItem.Tag, "auto-expand")) menuItem.IsChecked = state.Settings.AutoExpand;
                    if (Object.Equals(menuItem.Tag, "completed-toggle")) menuItem.Header = state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成";
                }
                RefreshStartupMenu(loginStartup, repairStartup);
            };
            var create = new MenuItem { Header = "添加待办" }; create.Click += delegate { Edit(null); }; menu.Items.Add(create);
            var showCompleted = new MenuItem { Header = state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成", Tag = "completed-toggle" };
            showCompleted.Click += delegate { var next = Clone(); next.Settings.CompletedCollapsed = !next.Settings.CompletedCollapsed; Commit(next); }; menu.Items.Add(showCompleted);
            menu.Items.Add(new Separator());
            var appearance = new MenuItem { Header = "外观设置…" }; appearance.Click += delegate { EditAppearance(); }; menu.Items.Add(appearance);
            var readable = new MenuItem { Header = "恢复清晰浅色" }; readable.Click += delegate { TrySaveAppearance(AppearanceSettings.CreateLight()); }; menu.Items.Add(readable);
            menu.Items.Add(new Separator());
            var automatic = new MenuItem { Header = "随截止时间扩大", IsCheckable = true, IsChecked = state.Settings.AutoExpand, Tag = "auto-expand" };
            automatic.Click += delegate {
                var next = Clone(); next.Settings.AutoExpand = automatic.IsChecked;
                if (Commit(next)) UpdateExpansion(true, true);
            };
            menu.Items.Add(automatic);
            menu.Items.Add(loginStartup); menu.Items.Add(repairStartup);
            var reset = new MenuItem { Header = "恢复右上角位置" }; reset.Click += delegate { ResetPosition(); }; menu.Items.Add(reset);
            var help = new MenuItem { Header = "使用说明" }; help.Click += delegate {
                ShowModal("点击圆圈切换完成状态；双击事项或右键编辑。\n拖动标题调整位置；关闭菜单中的‘随截止时间扩大’后，可调整常态大小。\n紧急事项：距截止不超过6小时；逾期事项持续保持最大面板。\n自动扩展以右上角为锚点，最大占桌面宽度的一半、高度约72%。\n仅日期的事项到当天结束后才逾期。\n开机自启默认关闭，可在菜单自行开启，仅当前账号登录后启动。\n退出仅关闭本次运行；移动程序后请更新自启位置。\n\n数据自动保存在：\n" + store.DataFilePath, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
            }; menu.Items.Add(help);
            menu.Items.Add(new Separator()); var quit = new MenuItem { Header = "退出待办" }; quit.Click += delegate { Program.Exit(); }; menu.Items.Add(quit);
            TrackMenu(menu); return menu;
        }

        private void RefreshStartupMenu(MenuItem enabled, MenuItem repair)
        {
            repair.Visibility = Visibility.Collapsed;
            if (preview) {
                enabled.IsChecked = false; enabled.IsEnabled = false;
                enabled.ToolTip = "示例模式不会更改 Windows 自启动设置。";
                return;
            }
            try {
                StartupRegistration registration = startupManager.Inspect();
                enabled.IsChecked = registration.Exists && registration.IsManaged;
                enabled.IsEnabled = !registration.Exists || registration.IsManaged;
                if (registration.Exists && !registration.IsManaged)
                    enabled.ToolTip = "自启动文件被其他内容占用，未作修改。";
                else if (registration.Exists && !registration.MatchesCurrent) {
                    enabled.ToolTip = "已登记另一位置。可关闭此项，或更新为当前程序；不会自动迁移。";
                    repair.Visibility = Visibility.Visible;
                }
                else enabled.ToolTip = "仅当前账号登录后启动；勾选表示快捷方式已登记。Windows 中禁用后需自行重新启用。";
            }
            catch (Exception ex) {
                enabled.IsEnabled = false;
                enabled.ToolTip = "无法检查开机自启：" + ex.Message;
            }
        }

        internal bool TrySetStartup(bool enabled)
        {
            if (preview) return false;
            try {
                if (enabled) startupManager.Enable(); else startupManager.Disable();
                ShowStatus(enabled ? "已登记当前位置，下次登录后由 Windows 启动。" : "开机自启已关闭，本次运行不受影响。");
                return true;
            }
            catch (Exception ex) {
                ShowStatus("开机自启设置未更改：" + ex.Message);
                return false;
            }
        }

        private sealed class UnavailableStartupManager : IStartupManager
        {
            private readonly Exception reason;
            public UnavailableStartupManager(Exception reason) { this.reason = reason; }
            public StartupRegistration Inspect() { throw Error(); }
            public void Enable() { throw Error(); }
            public void Disable() { throw Error(); }
            private Exception Error() { return new InvalidOperationException("当前无法访问 Windows 登录启动目录。", reason); }
        }

        private void Edit(TodoItem item)
        {
            if (Program.EditorOpen) return;
            Program.EditorOpen = true; StopAnimation();
            try
            {
                var editor = new TaskEditor(item);
                Rect area = DesktopHost.WorkArea(this);
                editor.WindowStartupLocation = WindowStartupLocation.Manual;
                editor.Left = Math.Max(area.Left + 12, area.Right - editor.Width - 26);
                editor.Top = Math.Max(area.Top + 20, Math.Min(DesktopHost.GetBounds(this).Top + 55, area.Bottom - editor.Height - 20));
                if (editor.ShowDialog() == true)
                {
                    var next = Clone();
                    if (item == null) next.Items.Add(editor.Result);
                    else { int index = next.Items.FindIndex(t => t.Id == item.Id); if (index >= 0) next.Items[index] = editor.Result; }
                    Commit(next);
                }
            }
            finally { Program.EditorOpen = false; ResumeAttention(); }
        }

        private void EditAppearance()
        {
            if (Program.EditorOpen) return;
            Program.EditorOpen = true; StopAnimation();
            try {
                var editor = new AppearanceEditor(state.Settings.Appearance, PreviewAppearance, TrySaveAppearance);
                Rect area = DesktopHost.WorkArea(this);
                editor.WindowStartupLocation = WindowStartupLocation.Manual;
                editor.MinWidth = Math.Min(editor.MinWidth, Math.Max(1, area.Width - 24));
                editor.MinHeight = Math.Min(editor.MinHeight, Math.Max(1, area.Height - 24));
                editor.MaxWidth = Math.Max(editor.MinWidth, area.Width - 24);
                editor.MaxHeight = Math.Max(editor.MinHeight, area.Height - 24);
                editor.Width = Math.Min(editor.Width, editor.MaxWidth);
                editor.Height = Math.Min(editor.Height, editor.MaxHeight);
                editor.Left = Math.Max(area.Left + 12, area.Right - editor.Width - 26);
                editor.Top = Math.Max(area.Top + 12, Math.Min(DesktopHost.GetBounds(this).Top + 35, area.Bottom - editor.Height - 12));
                editor.ShowDialog();
            }
            finally {
                CancelAppearancePreview();
                Program.EditorOpen = false; ResumeAttention();
            }
        }

        internal void PreviewAppearance(AppearanceSettings appearance)
        {
            appearancePreview = AppearanceSettings.Normalize(appearance);
            Render();
        }

        internal void CancelAppearancePreview()
        {
            appearancePreview = null;
            Render();
        }

        internal bool TrySaveAppearance(AppearanceSettings appearance)
        {
            var next = Clone(); next.Settings.Appearance = AppearanceSettings.Normalize(appearance);
            try {
                if (!preview) store.Save(next);
                state = next; appearancePreview = null;
                status.Visibility = Visibility.Collapsed; Render();
                return true;
            }
            catch (Exception ex) {
                appearancePreview = null; Render();
                ShowStatus("外观未保存，已恢复原来的外观。" + ex.Message);
                return false;
            }
        }

        private void ApplyPanelPalette()
        {
            palette = new PanelPalette(appearancePreview ?? state.Settings.Appearance);
            surface.Background = palette.Background;
            Foreground = title.Foreground = palette.Ink;
            dateText.Foreground = palette.Muted;
            status.Foreground = palette.Warning;
            collapse.Style = more.Style = add.Style = palette.ButtonStyle;
            toolbarSurface.Background = palette.ToolbarBackground;
            grip.Template = palette.ResizeTemplate;
        }

        private void Delete(TodoItem item)
        {
            string description = item.Text.Length > 100 ? item.Text.Substring(0, 100) + "…" : item.Text;
            if (ShowModal("删除这条待办？\n\n" + description, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var next = Clone(); next.Items.RemoveAll(t => t.Id == item.Id); Commit(next);
        }

        private TodoState Clone()
        {
            return new TodoState { Version = state.Version, Settings = new WidgetSettings { Left = state.Settings.Left, Top = state.Settings.Top,
                Width = state.Settings.Width, Height = state.Settings.Height, Collapsed = state.Settings.Collapsed, CompletedCollapsed = state.Settings.CompletedCollapsed, AutoExpand = state.Settings.AutoExpand,
                Appearance = AppearanceSettings.Normalize(state.Settings.Appearance) },
                Items = state.Items.Select(t => new TodoItem { Id = t.Id, Text = t.Text, IsCompleted = t.IsCompleted, CreatedAt = t.CreatedAt,
                    CompletedAt = t.CompletedAt, DueDate = t.DueDate, HasTime = t.HasTime, DeadlineSetAt = t.DeadlineSetAt }).ToList() };
        }
        private bool Commit(TodoState next)
        {
            try { if (!preview) store.Save(next); state = next; status.Visibility = Visibility.Collapsed; Render(); return true; }
            catch (Exception ex) { ShowStatus("保存失败，修改尚未保存。" + ex.Message); return false; }
        }
        private void ShowStatus(string text) { status.Text = text; status.Visibility = Visibility.Visible; status.ToolTip = text; }
        private void ScheduleSave() { if (!ready) return; saveTimer.Stop(); saveTimer.Start(); }
        public void SaveGeometry()
        {
            if (!ready || HostLost) return;
            var next = Clone();
            next.Settings.Left = anchorRight - baseWidth; next.Settings.Top = anchorTop;
            next.Settings.Width = baseWidth; next.Settings.Height = baseHeight;
            try { if (!preview) store.Save(next); state = next; }
            catch (Exception ex) { ShowStatus("位置尚未保存。" + ex.Message); }
        }
        public TodoState CurrentState { get { return state; } }
        private void TogglePanel()
        {
            StopAnimation();
            var next = Clone(); next.Settings.Collapsed = !next.Settings.Collapsed;
            if (!Commit(next)) return;
            UpdateExpansion(true, true); ScheduleSave();
        }
        private void ApplyCollapsed()
        {
            bool collapsed = state.Settings.Collapsed;
            scroll.Visibility = add.Visibility = grip.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            attentionText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            grip.IsEnabled = !state.Settings.AutoExpand || TodoRules.Expansion(state, DateTime.Now) <= 0.00001;
            grip.Opacity = grip.IsEnabled ? 1 : 0.3;
            grip.ToolTip = grip.IsEnabled ? "调整常态大小" : "当前按截止时间扩大；关闭自动扩展后可调整常态大小";
            collapse.Content = collapsed ? "⌄" : "⌃"; collapse.ToolTip = collapsed ? "展开面板" : "收起面板";
            AutomationProperties.SetName(collapse, collapsed ? "展开面板" : "收起面板");
        }
        private void ResetPosition()
        {
            Rect area = DesktopHost.WorkArea(this);
            StopAnimation(); anchorRight = area.Right - 24; anchorTop = area.Top + 18;
            UpdateExpansion(true, true); ScheduleSave();
        }
        private Point ScreenPointer(MouseEventArgs e)
        {
            Point pixels = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            return source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice.Transform(pixels) : pixels;
        }
        private void HeaderDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit != sender) { if (hit is Button) return; hit = VisualTreeHelper.GetParent(hit); }
            moving = true; StopAnimation(); dragStart = ScreenPointer(e); dragBounds = DesktopHost.GetBounds(this);
            ((UIElement)sender).CaptureMouse(); e.Handled = true;
        }
        private void HeaderMove(object sender, MouseEventArgs e)
        {
            if (!moving || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = ScreenPointer(e); Rect bounds = dragBounds;
            bounds.X += current.X - dragStart.X; bounds.Y += current.Y - dragStart.Y;
            bounds = Clamp(bounds, DesktopHost.WorkArea(this));
            DesktopHost.SetBounds(this, bounds); anchorRight = bounds.Right; anchorTop = bounds.Top;
        }
        private void HeaderUp(object sender, MouseButtonEventArgs e)
        {
            if (!moving) return; moving = false; ((UIElement)sender).ReleaseMouseCapture(); ScheduleSave(); ResumeAttention(); e.Handled = true;
        }
        private void Resize(object sender, DragDeltaEventArgs e)
        {
            if (state.Settings.Collapsed || !grip.IsEnabled) return;
            Rect bounds = DesktopHost.GetBounds(this); Rect area = DesktopHost.WorkArea(this);
            bounds.Width = Math.Min(Math.Max(320, bounds.Width + e.HorizontalChange), Math.Min(area.Width * 0.5, area.Right - bounds.Left - 8));
            bounds.Height = Math.Min(Math.Max(280, bounds.Height + e.VerticalChange), area.Bottom - bounds.Top - 8);
            bounds.Height = Math.Min(bounds.Height, area.Height * (1100.0 / 1528.0));
            baseWidth = bounds.Width; baseHeight = bounds.Height; anchorRight = bounds.Right; anchorTop = bounds.Top;
            DesktopHost.SetBounds(this, bounds);
        }

        private bool InteractionBlocked
        {
            get { return IsMouseOver || moving || resizing || menuOpen || dialogOpen || Program.EditorOpen; }
        }

        private MessageBoxResult ShowModal(string text, string title, MessageBoxButton buttons,
            MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            dialogOpen = true; StopAnimation();
            try { return MessageBox.Show(text, title, buttons, icon, defaultResult); }
            finally { dialogOpen = false; ResumeAttention(); }
        }

        private void UpdateHeader(DateTime now)
        {
            int overdue = TodoRules.OverdueCount(state, now);
            int urgent = TodoRules.UrgentCount(state, now);
            string summary = "逾期 " + overdue + " 项 · 6小时内 " + urgent + " 项";
            attentionText.Text = summary;
            attentionText.Foreground = overdue > 0 ? palette.Warning : urgent > 0 ? palette.Urgent : palette.Muted;
            dateText.Text = state.Settings.Collapsed ? summary : now.ToString("M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN")) + (preview ? " · 示例" : "");
        }

        private void TrackMenu(ContextMenu menu)
        {
            menu.Opened += delegate { menuOpen = true; StopAnimation(); };
            menu.Closed += delegate { menuOpen = false; ResumeAttention(); };
        }

        private static PanelBounds ToPanel(Rect rect)
        {
            return new PanelBounds(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        private static Rect ToRect(PanelBounds bounds)
        {
            return new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }

        private void UpdateExpansion(bool immediate, bool allowDuringInteraction)
        {
            if (!ready || HostLost) return;
            if (!allowDuringInteraction && InteractionBlocked) { StopAnimation(); return; }
            Rect area = DesktopHost.WorkArea(this);
            double progress = state.Settings.AutoExpand ? TodoRules.Expansion(state, DateTime.Now) : 0;
            PanelBounds target = GrowthLayout.Target(ToPanel(area), anchorRight, anchorTop, baseWidth, baseHeight, progress, state.Settings.Collapsed);
            anchorRight = target.Right; anchorTop = target.Top;
            Rect next = ToRect(target);
            if (immediate || state.Settings.Collapsed)
            {
                StopAnimation(); DesktopHost.SetBounds(this, next); return;
            }
            Rect current = DesktopHost.GetBounds(this);
            if (Near(current, next)) { StopAnimation(); return; }
            if (animationTimer.IsEnabled && Near(animationTarget, next)) return;
            animationStart = current; animationTarget = next; animationStarted = DateTime.UtcNow;
            animationTimer.Start();
            Diagnostics.Log("Growth progress=" + progress.ToString("0.000", CultureInfo.InvariantCulture) + ", target=" + next + ", baseline=" + baseWidth + "x" + baseHeight);
        }

        private static bool Near(Rect left, Rect right)
        {
            return Math.Abs(left.Left - right.Left) < 0.8 && Math.Abs(left.Top - right.Top) < 0.8 &&
                Math.Abs(left.Width - right.Width) < 0.8 && Math.Abs(left.Height - right.Height) < 0.8;
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (!ready || HostLost || InteractionBlocked) { StopAnimation(); return; }
            double time = Math.Max(0, Math.Min(1, (DateTime.UtcNow - animationStarted).TotalSeconds / 3.0));
            double blend = time * time * (3 - 2 * time);
            Rect next = new Rect(
                animationStart.Left + (animationTarget.Left - animationStart.Left) * blend,
                animationStart.Top + (animationTarget.Top - animationStart.Top) * blend,
                animationStart.Width + (animationTarget.Width - animationStart.Width) * blend,
                animationStart.Height + (animationTarget.Height - animationStart.Height) * blend);
            DesktopHost.SetBounds(this, next);
            if (time >= 1) StopAnimation();
        }

        private void StopAnimation()
        {
            animationTimer.Stop();
        }

        private void ResumeAttention()
        {
            if (!ready || HostLost || resumeQueued) return;
            resumeQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate {
                resumeQueued = false;
                if (!ready || HostLost || InteractionBlocked) return;
                if (deferredRender || dateStamp != DateTime.Now.ToString("yyyyMMddHHmm")) Render();
                else UpdateExpansion(false, false);
            }));
        }

        private static Rect Clamp(Rect bounds, Rect area)
        {
            bounds.Width = Math.Min(bounds.Width, area.Width - 16); bounds.Height = Math.Min(bounds.Height, area.Height - 16);
            bounds.X = Math.Max(area.Left + 8, Math.Min(bounds.Left, area.Right - bounds.Width - 8));
            bounds.Y = Math.Max(area.Top + 8, Math.Min(bounds.Top, area.Bottom - bounds.Height - 8)); return bounds;
        }
    }
}
