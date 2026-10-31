using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CornerTodo.MacDesktop;

/// <summary>Mac preview surface. Task mutations remain in the shared transactional session.</summary>
public sealed class TodoWindow : Window
{
    private readonly TodoSession session;
    private readonly string dataDirectory;
    private readonly Action requestExit;
    private TodoState state;
    private readonly Border surface;
    private readonly Border toolbarSurface;
    private readonly Grid root;
    private readonly Grid header;
    private readonly TextBlock title, date, attention, status;
    private readonly Button collapse, more, add, edge;
    private readonly TextBlock edgeCount, edgeOverdue;
    private readonly StackPanel rows;
    private readonly ScrollViewer scroll;
    private readonly Grid footer;
    private readonly Thumb grip;
    private readonly List<TaskRow> taskRows = new();
    private readonly DispatcherTimer timer;
    private AppearanceSettings? appearancePreview;
    private AppearanceSettings palette = AppearanceSettings.CreateLight();
    private IBrush ink = Ui.Ink, muted = Ui.Muted, accent = Ui.Accent, warning = Ui.Warning, urgent = Brush.Parse("#98651D");
    private bool ready, stopped, pointerInside, moving, resizing, edgeHidden, docked, hideArmed, waitsForEntry;
    private int dialogs, menus;
    private DateTime? hideAt;
    private DateTime lastTimeRefresh = DateTime.MinValue;
    private double baseWidth, baseHeight, anchorRight, anchorTop;
    private PixelPoint dragStart, dragPosition;
    private PanelBounds animationStart, animationTarget;
    private DateTime animationAt;
    private bool animating;
    private PanelBounds displayed;

    public bool IsBusy => dialogs != 0 || moving || resizing;
    public bool IsEdgeHidden => edgeHidden;
    internal Control ContentSurface => surface;
    internal bool PreviewMode { get; set; }
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
    internal PanelBounds BaselineBounds => new(anchorRight - baseWidth, anchorTop, baseWidth, baseHeight);
    private bool InteractionBlocked => IsBusy || menus != 0 || pointerInside;

    internal TodoWindow(TodoSession taskSession, string directory, Action exit)
    {
        session = taskSession; dataDirectory = directory; requestExit = exit; state = session.Snapshot();
        Title = "CornerTodo · 桌面待办（Mac 内测版）";
        Width = state.Settings.Width; Height = state.Settings.Height; MinWidth = 1; MinHeight = 1;
        CanResize = false; SystemDecorations = SystemDecorations.None;
        Topmost = false; ShowInTaskbar = false; ShowActivated = false;
        Ui.Style(this); Background = Brushes.Transparent;
        // Subpixel text assumes an opaque pixel background. The desktop panel
        // is translucent, so grayscale antialiasing keeps its glyph edges clean.
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        surface = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(19, 15, 17, 12) };
        RenderOptions.SetTextRenderingMode(surface, TextRenderingMode.Antialias);
        root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") }; surface.Child = root; Content = surface;
        header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
        var heading = new StackPanel { Spacing = 4 };
        title = Ui.Text("待办清单", 25, true); date = Ui.Text("", 12); attention = Ui.Text("", 12);
        heading.Children.Add(title); heading.Children.Add(date); heading.Children.Add(attention); header.Children.Add(heading);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        collapse = Ui.Button("⌃", ToggleCollapsed); collapse.Width = 34; collapse.Height = 32; collapse.Padding = new Thickness(4);
        more = Ui.Button("⋯", ShowMenu); more.Width = 34; more.Height = 32; more.Padding = new Thickness(4);
        AutomationProperties.SetName(collapse, "收起面板"); AutomationProperties.SetName(more, "更多操作");
        ToolTip.SetTip(more, "更多操作"); toolbar.Children.Add(collapse); toolbar.Children.Add(more);
        toolbarSurface = new Border { Child = toolbar, Background = Brush.Parse("#F4F9FB"), CornerRadius = new CornerRadius(6) };
        Grid.SetColumn(toolbarSurface, 1); header.Children.Add(toolbarSurface); root.Children.Add(header);
        rows = new StackPanel { Spacing = 11, Margin = new Thickness(0, 0, 12, 0) };
        scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        add = Ui.Button("＋ 添加待办", () => Edit(null)); add.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(add);
        grip = new Thumb { Width = 20, Height = 20, Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Background = Brush.Parse("#40628697"), VerticalAlignment = VerticalAlignment.Bottom };
        ToolTip.SetTip(grip, "调整常态大小"); Grid.SetColumn(grip, 1); footer.Children.Add(grip); Grid.SetRow(footer, 2); root.Children.Add(footer);
        status = Ui.Text("", 11, brush: Ui.Warning); status.IsVisible = false; status.Margin = new Thickness(0, 5, 0, 0);
        Grid.SetRow(status, 3); root.Children.Add(status);
        var edgeContent = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var edgeTitle = Ui.Text("待办", 12); edgeTitle.HorizontalAlignment = HorizontalAlignment.Center;
        edgeCount = Ui.Text("0", 24, true); edgeCount.HorizontalAlignment = HorizontalAlignment.Center;
        edgeOverdue = Ui.Text("逾期 0", 10); edgeOverdue.HorizontalAlignment = HorizontalAlignment.Center;
        edgeContent.Children.Add(edgeTitle); edgeContent.Children.Add(edgeCount);
        edgeContent.Children.Add(new Border { Height = 1, Background = Brush.Parse("#40628697"), Margin = new Thickness(5, 2) });
        edgeContent.Children.Add(edgeOverdue);
        edge = new Button { Content = edgeContent, Padding = new Thickness(2, 10), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
        edge.Click += (_, _) => RevealEdge();
        PointerEntered += (_, _) => ObservePointer(true);
        PointerMoved += (_, _) => { if (!edgeHidden) waitsForEntry = false; };
        PointerExited += (_, _) => ObservePointer(false);
        header.PointerPressed += HeaderPressed; header.PointerMoved += HeaderMoved; header.PointerReleased += HeaderReleased;
        header.PointerCaptureLost += (_, _) => { if (moving) FinishMove(); };
        grip.DragStarted += (_, _) => { if (!grip.IsEnabled || edgeHidden) return; resizing = true; animating = false; hideAt = null; };
        grip.DragDelta += (_, e) => ResizeBy(e.Vector.X, e.Vector.Y);
        grip.DragCompleted += (_, _) => { if (!resizing) return; resizing = false; SaveGeometry(); };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; timer.Tick += OnTick;
        Opened += (_, _) => { InitializeGeometry(); timer.Start(); };
        Closed += (_, _) => StopTimers();
        KeyDown += (_, e) => {
            if (e.Key == Key.N && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { Edit(null); e.Handled = true; }
        };
        surface.ContextMenu = CreateMenu();
        Render();
    }

    private void InitializeGeometry()
    {
        var area = Ui.WorkAreaDip(this); var settings = state.Settings;
        var baseline = GrowthLayout.Target(area, settings.Left.HasValue ? settings.Left.Value + settings.Width : area.Right - 24,
            settings.Top ?? area.Top + 18, settings.Width, settings.Height, 0, false);
        baseWidth = baseline.Width; baseHeight = baseline.Height; anchorRight = baseline.Right; anchorTop = baseline.Top;
        docked = settings.EdgeAutoHide && Math.Abs(area.Right - anchorRight) <= 32;
        if (docked) anchorRight = area.Right - 8;
        ready = true; ApplyGeometry(true);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (stopped || !ready) return;
        var clock = UtcNow();
        if ((clock - lastTimeRefresh).TotalSeconds >= 1)
        {
            lastTimeRefresh = clock; UpdateTimes(DateTime.Now);
        }
        if (edgeHidden) { ApplyCapsule(); return; }
        if (state.Settings.EdgeAutoHide && docked && hideArmed && !waitsForEntry && !InteractionBlocked)
        {
            hideAt ??= clock.AddMilliseconds(800);
            if (clock >= hideAt.Value) { SetEdgeHidden(true); ApplyCapsule(); return; }
        }
        else hideAt = null;
        ApplyGeometry(false);
    }

    private void ObservePointer(bool inside)
    {
        if (stopped) return;
        pointerInside = inside;
        if (inside)
        {
            hideAt = null; animating = false;
            if (!edgeHidden) waitsForEntry = false;
        }
        else if (state.Settings.EdgeAutoHide && docked && !edgeHidden && !waitsForEntry)
        { hideArmed = true; hideAt = UtcNow().AddMilliseconds(800); }
    }

    private void ObserveMenu(bool opened)
    {
        if (stopped) return;
        if (opened) { menus++; animating = false; if (edgeHidden) RevealEdge(); }
        else
        {
            menus = Math.Max(0, menus - 1); hideAt = null;
            // Enabling the option from a popup can leave the pointer outside the
            // main window already; closing that popup completes the leave.
            if (menus == 0 && state.Settings.EdgeAutoHide && docked && !edgeHidden && !pointerInside && !waitsForEntry)
            { hideArmed = true; hideAt = UtcNow().AddMilliseconds(800); }
        }
    }

    private double Growth => state.Settings.AutoExpand ? TodoRules.Expansion(state, DateTime.Now) : 0;

    private void ApplyGeometry(bool immediate)
    {
        if (!ready || stopped) return;
        if (edgeHidden) { ApplyCapsule(); return; }
        if (!immediate && InteractionBlocked) { animating = false; return; }
        var area = Ui.WorkAreaDip(this); if (docked && state.Settings.EdgeAutoHide) anchorRight = area.Right - 8;
        var target = GrowthLayout.Target(area, anchorRight, anchorTop, baseWidth, baseHeight, Growth, state.Settings.Collapsed);
        anchorRight = target.Right; anchorTop = target.Top;
        if (immediate || state.Settings.Collapsed) { animating = false; SetBounds(target); return; }
        if (!Near(displayed, target) && (!animating || !Near(target, animationTarget)))
        { animationStart = displayed; animationTarget = target; animationAt = UtcNow(); animating = true; }
        if (!animating) return;
        var fraction = Math.Clamp((UtcNow() - animationAt).TotalSeconds / 3, 0, 1);
        var eased = fraction * fraction * (3 - 2 * fraction);
        SetBounds(new PanelBounds(animationStart.Left + (target.Left - animationStart.Left) * eased,
            animationStart.Top + (target.Top - animationStart.Top) * eased,
            animationStart.Width + (target.Width - animationStart.Width) * eased,
            animationStart.Height + (target.Height - animationStart.Height) * eased));
        if (fraction >= 1) animating = false;
    }

    private void SetBounds(PanelBounds value)
    {
        displayed = value; Width = value.Width; Height = value.Height;
        var scale = Math.Max(0.1, RenderScaling);
        Position = new PixelPoint((int)Math.Round(value.Left * scale), (int)Math.Round(value.Top * scale));
    }

    private static bool Near(PanelBounds a, PanelBounds b) => Math.Abs(a.Left - b.Left) < .8 && Math.Abs(a.Top - b.Top) < .8 && Math.Abs(a.Width - b.Width) < .8 && Math.Abs(a.Height - b.Height) < .8;

    private void Render()
    {
        state = session.Snapshot(); palette = AppearanceSettings.Normalize(appearancePreview ?? state.Settings.Appearance);
        ink = Brush.Parse(palette.PrimaryTextColor); muted = Brush.Parse(palette.SecondaryTextColor);
        bool dark = Color.Parse(palette.BackgroundColor) is var bg && .2126 * bg.R + .7152 * bg.G + .0722 * bg.B < 127.5;
        accent = Brush.Parse(dark ? "#79D2E5" : "#2B8196"); warning = Brush.Parse(dark ? "#FFB4AB" : "#A1463C"); urgent = Brush.Parse(dark ? "#F6CA75" : "#98651D");
        title.Foreground = ink; date.Foreground = muted; attention.Foreground = muted; status.Foreground = warning;
        toolbarSurface.Background = Brush.Parse(dark ? "#F4182B38" : "#F4F4F9FB");
        collapse.Foreground = more.Foreground = add.Foreground = accent;
        var previousScroll = scroll.Offset;
        rows.Children.Clear(); taskRows.Clear();
        var pending = TodoRules.Pending(state).ToList(); var completed = TodoRules.Completed(state).ToList();
        var listHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2, 0, 4) };
        listHeading.Children.Add(Ui.Text("未完成 " + pending.Count, 14, true, ink));
        var sort = new ComboBox { ItemsSource = new[] { "截止时间优先", "优先级优先" }, SelectedIndex = (int)state.Settings.SortMode,
            FontSize = 11, MinWidth = 130, Foreground = muted, Background = Brushes.Transparent };
        AutomationProperties.SetName(sort, "排序方式");
        sort.SelectionChanged += (_, _) => {
            if (sort.SelectedIndex < 0 || sort.SelectedIndex == (int)state.Settings.SortMode) return;
            int selected = sort.SelectedIndex; sort.IsDropDownOpen = false;
            if (!Execute(() => session.SetSortMode((TaskSortMode)selected))) sort.SelectedIndex = (int)state.Settings.SortMode;
        };
        sort.PropertyChanged += (_, e) => { if (e.Property == ComboBox.IsDropDownOpenProperty) ObserveMenu(sort.IsDropDownOpen); };
        Grid.SetColumn(sort, 1); listHeading.Children.Add(sort); rows.Children.Add(listHeading);
        foreach (var item in pending) rows.Children.Add(BuildRow(item));
        if (pending.Count == 0) rows.Children.Add(Ui.Text("暂时没有待办，添加一条开始吧。", 12, brush: muted));
        rows.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(45, dark ? (byte)255 : (byte)25, dark ? (byte)255 : (byte)60, dark ? (byte)255 : (byte)80)), Margin = new Thickness(0, 7) });
        var completedToggle = Ui.Button((state.Settings.CompletedCollapsed ? "› " : "⌄ ") + "已完成 " + completed.Count,
            () => Execute(() => session.SetCompletedCollapsed(!state.Settings.CompletedCollapsed)));
        completedToggle.HorizontalAlignment = HorizontalAlignment.Left; completedToggle.Foreground = muted; completedToggle.Padding = new Thickness(0, 3);
        rows.Children.Add(completedToggle);
        if (!state.Settings.CompletedCollapsed)
        {
            foreach (var item in completed) rows.Children.Add(BuildRow(item));
            if (completed.Count == 0) rows.Children.Add(Ui.Text("完成的事项会保留在这里", 11, brush: muted));
        }
        bool collapsed = state.Settings.Collapsed;
        scroll.IsVisible = footer.IsVisible = !collapsed; attention.IsVisible = !collapsed;
        header.Margin = collapsed ? new Thickness(0) : new Thickness(0, 0, 0, 14);
        collapse.Content = collapsed ? "⌄" : "⌃";
        AutomationProperties.SetName(collapse, collapsed ? "展开面板" : "收起面板");
        grip.IsEnabled = !state.Settings.AutoExpand || Growth <= .00001;
        grip.Opacity = grip.IsEnabled ? .65 : .2;
        ToolTip.SetTip(grip, grip.IsEnabled ? "调整常态大小" : "当前按截止时间扩大；关闭自动扩大后可调整大小");
        RefreshSurface(); UpdateTimes(DateTime.Now); scroll.Offset = previousScroll; ApplyGeometry(false);
    }

    private Control BuildRow(TodoItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto"), ColumnSpacing = 5, Margin = new Thickness(0, 2) };
        var check = new CheckBox { IsChecked = item.IsCompleted, VerticalAlignment = VerticalAlignment.Top, MinWidth = 22, MinHeight = 27 };
        AutomationProperties.SetName(check, "完成：" + item.Text);
        bool changing = false;
        check.PropertyChanged += (_, e) => {
            if (e.Property != ToggleButton.IsCheckedProperty || changing) return;
            bool desired = check.IsChecked == true;
            if (desired == item.IsCompleted) return;
            if (!Execute(() => session.SetCompleted(item.Id, desired))) { changing = true; check.IsChecked = item.IsCompleted; changing = false; }
        };
        grid.Children.Add(check);
        var content = new StackPanel { Spacing = 4 };
        var label = Ui.Text(item.Text, item.IsCompleted ? 15 : TodoRules.IsOverdue(item, DateTime.Now) ? 21 : TodoRules.IsUrgent(item, DateTime.Now) ? 19 : 15,
            !item.IsCompleted && (TodoRules.IsOverdue(item, DateTime.Now) || TodoRules.IsUrgent(item, DateTime.Now)), item.IsCompleted ? muted : ink);
        if (item.IsCompleted) label.TextDecorations = TextDecorations.Strikethrough;
        label.PointerPressed += (_, e) => { if (e.ClickCount == 2 && e.GetCurrentPoint(label).Properties.IsLeftButtonPressed) { Edit(item); e.Handled = true; } };
        content.Children.Add(label);
        var due = Ui.Text("", 12, brush: muted); content.Children.Add(due);
        var progress = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = accent,
            HorizontalAlignment = HorizontalAlignment.Left, IsVisible = !item.IsCompleted };
        var track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Brush.Parse("#30628697"), Child = progress, IsVisible = !item.IsCompleted };
        content.Children.Add(track); Grid.SetColumn(content, 1); grid.Children.Add(content);
        var badges = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        badges.Children.Add(new Border { Child = Ui.Text(TodoRules.PriorityLabel(item.Priority), 10, brush: muted),
            Background = Brush.Parse("#18628697"), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 2) });
        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            var notes = Ui.Button("备注", () => Edit(item, true)); notes.FontSize = 10; notes.Padding = new Thickness(3, 1); notes.MinHeight = 24;
            AutomationProperties.SetName(notes, "查看备注：" + item.Text); badges.Children.Add(notes);
        }
        Grid.SetColumn(badges, 2); grid.Children.Add(badges);
        var menu = new ContextMenu { ItemsSource = new[] {
            Menu(item.IsCompleted ? "恢复为未完成" : "标记为已完成", () => Execute(() => session.SetCompleted(item.Id, !item.IsCompleted))),
            Menu("编辑", () => Edit(item)), Menu("查看／编辑备注", () => Edit(item, true)), Menu("删除", () => Delete(item)) } };
        TrackMenu(menu); grid.ContextMenu = menu;
        var row = new TaskRow(item, label, due, track, progress); taskRows.Add(row);
        track.SizeChanged += (_, _) => UpdateRow(row, DateTime.Now);
        UpdateRow(row, DateTime.Now); return grid;
    }

    private void UpdateTimes(DateTime now)
    {
        int pending = TodoRules.Pending(state).Count(), overdue = TodoRules.OverdueCount(state, now), soon = TodoRules.UrgentCount(state, now);
        string summary = "逾期 " + overdue + " 项 · 6小时内 " + soon + " 项";
        date.Text = state.Settings.Collapsed ? summary : now.ToString("M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN"));
        attention.Text = summary; attention.Foreground = overdue > 0 ? warning : soon > 0 ? urgent : muted;
        edgeCount.Text = pending.ToString(CultureInfo.InvariantCulture); edgeCount.Foreground = ink;
        edgeCount.FontSize = pending < 100 ? 24 : pending < 1000 ? 18 : 14;
        edgeOverdue.Text = "逾期 " + overdue; edgeOverdue.Foreground = overdue > 0 ? warning : soon > 0 ? urgent : muted;
        ((StackPanel)edge.Content!).Children.OfType<TextBlock>().First().Foreground = ink;
        string edgeTip = "未完成 " + pending + " 项，其中逾期 " + overdue + " 项；点击展开待办";
        ToolTip.SetTip(edge, edgeTip); AutomationProperties.SetName(edge, edgeTip);
        foreach (var row in taskRows) UpdateRow(row, now);
    }

    private void UpdateRow(TaskRow row, DateTime now)
    {
        var item = row.Item; bool overdue = TodoRules.IsOverdue(item, now), soon = TodoRules.IsUrgent(item, now);
        row.Due.Text = TodoRules.FormatAttention(item, now);
        row.Due.Foreground = overdue ? warning : soon ? urgent : muted;
        row.Progress.Background = overdue ? warning : soon ? urgent : accent;
        row.Progress.Width = Math.Max(0, row.Track.Bounds.Width * TodoRules.TimeProgress(item, now));
        if (!InteractionBlocked)
        {
            row.Label.FontSize = item.IsCompleted ? 15 : overdue ? 21 : soon ? 19 : 15;
            row.Label.FontWeight = !item.IsCompleted && (overdue || soon) ? FontWeight.SemiBold : FontWeight.Normal;
        }
        int percent = (int)Math.Round(TodoRules.TimeProgress(item, now) * 100);
        if (!overdue) percent = Math.Min(99, percent);
        ToolTip.SetTip(row.Track, "时间已用 " + percent + "%（不是完成进度）");
    }

    private void RefreshSurface()
    {
        var color = Color.Parse(palette.BackgroundColor);
        surface.Background = edgeHidden
            ? new SolidColorBrush(Color.FromArgb(244, color.R, color.G, color.B))
            : new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * (1 - palette.BackgroundTransparency / 100)), color.R, color.G, color.B));
        surface.Padding = edgeHidden ? new Thickness(3, 4) : state.Settings.Collapsed ? new Thickness(19, 10, 17, 8) : new Thickness(19, 15, 17, 12);
        surface.CornerRadius = edgeHidden ? new CornerRadius(16, 0, 0, 16) : new CornerRadius(12);
        surface.BorderThickness = edgeHidden ? new Thickness(1, 1, 0, 1) : new Thickness(0);
        surface.BorderBrush = Brush.Parse("#40628697");
        surface.Child = edgeHidden ? edge : root;
    }

    private bool Execute(Action operation, bool render = true)
    {
        if (stopped) return false;
        try { operation(); }
        catch (Exception exception) { ReportStatus("修改未保存：" + exception.Message); return false; }
        state = session.Snapshot(); status.IsVisible = false;
        if (render) Render(); return true;
    }

    internal void ReportStatus(string message) { status.Text = message; status.IsVisible = true; ToolTip.SetTip(status, message); }
    public void ShowStatus(string message) => ReportStatus(message);

    private async void Edit(TodoItem? item, bool focusNotes = false)
    {
        if (IsBusy || stopped) return;
        dialogs++; animating = false; RevealEdge();
        try
        {
            var editor = new TaskEditorWindow(item, draft => Execute(() => { if (item == null) session.Add(draft); else session.Edit(item.Id, draft); }, false), focusNotes);
            Ui.FitDialog(editor, this); await editor.ShowDialog<bool>(this); Render();
        }
        catch (Exception exception) { ReportStatus("编辑窗口未能完成操作：" + exception.Message); }
        finally { dialogs--; hideAt = null; }
    }

    private async void EditAppearance()
    {
        if (IsBusy || stopped) return;
        dialogs++; animating = false; RevealEdge();
        try
        {
            var editor = new AppearanceWindow(state.Settings.Appearance, value => { appearancePreview = value; Render(); },
                value => Execute(() => session.SetAppearance(value), false));
            Ui.FitDialog(editor, this); await editor.ShowDialog<bool>(this);
        }
        catch (Exception exception) { ReportStatus("外观窗口未能完成操作：" + exception.Message); }
        finally { appearancePreview = null; dialogs--; hideAt = null; Render(); }
    }

    private async void Delete(TodoItem item)
    {
        if (IsBusy || stopped) return;
        dialogs++; animating = false; RevealEdge();
        try
        {
            string text = item.Text.Length > 100 ? item.Text[..100] + "…" : item.Text;
            if (await Ui.Confirm(this, "确认删除", "删除这条待办？\n\n" + text)) Execute(() => session.Delete(item.Id));
        }
        catch (Exception exception) { ReportStatus("删除操作未完成：" + exception.Message); }
        finally { dialogs--; hideAt = null; }
    }

    private MenuItem Menu(string label, Action action) => new() { Header = label, Command = new Ui.ActionCommand(action) };

    private ContextMenu CreateMenu()
    {
        var panelToggle = Menu(state.Settings.Collapsed ? "展开面板" : "收起面板", ToggleCollapsed);
        var automatic = Menu((state.Settings.AutoExpand ? "✓ " : "") + "随截止时间扩大", () => { if (Execute(() => session.SetAutoExpand(!state.Settings.AutoExpand))) ApplyGeometry(true); });
        var hiding = Menu((state.Settings.EdgeAutoHide ? "✓ " : "") + "贴边自动收起", ToggleEdgeMode);
        var menu = new ContextMenu { ItemsSource = new object[] {
            Menu("添加待办", () => Edit(null)), panelToggle, new Separator(),
            Menu("外观设置…", EditAppearance), Menu("恢复清晰浅色", () => Execute(() => session.SetAppearance(AppearanceSettings.CreateLight()))),
            new Separator(), automatic, hiding, Menu("恢复右上角位置", RestorePanel), Menu("使用说明", ShowHelp), new Separator(), Menu("退出 CornerTodo", requestExit) } };
        menu.Opened += (_, _) => {
            automatic.Header = (state.Settings.AutoExpand ? "✓ " : "") + "随截止时间扩大";
            hiding.Header = (state.Settings.EdgeAutoHide ? "✓ " : "") + "贴边自动收起";
            panelToggle.Header = state.Settings.Collapsed ? "展开面板" : "收起面板";
        };
        TrackMenu(menu); return menu;
    }

    private void TrackMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => ObserveMenu(true);
        menu.Closed += (_, _) => ObserveMenu(false);
    }

    private void ShowMenu() { var menu = CreateMenu(); menu.Open(more); }

    private void ToggleCollapsed()
    {
        if (Execute(() => session.SetCollapsed(!state.Settings.Collapsed))) { ApplyGeometry(true); SaveGeometry(); }
    }

    private void ToggleEdgeMode()
    {
        if (!Execute(() => session.SetEdgeAutoHide(!state.Settings.EdgeAutoHide), false)) return;
        hideArmed = false; hideAt = null;
        var area = Ui.WorkAreaDip(this);
        docked = state.Settings.EdgeAutoHide && Math.Abs(area.Right - anchorRight) <= 32;
        if (docked) anchorRight = area.Right - 8;
        if (!state.Settings.EdgeAutoHide) SetEdgeHidden(false);
        Render(); ApplyGeometry(true); SaveGeometry();
    }

    private void SetEdgeHidden(bool hidden)
    {
        if (edgeHidden == hidden) return;
        edgeHidden = hidden; animating = false; hideAt = null; RefreshSurface();
        if (hidden) ApplyCapsule();
    }

    private void RevealEdge()
    {
        if (!edgeHidden || stopped) return;
        hideArmed = false; waitsForEntry = true; hideAt = null; SetEdgeHidden(false); ApplyGeometry(true);
    }

    private void ApplyCapsule()
    {
        var area = Ui.WorkAreaDip(this); double width = Math.Min(48, area.Width), height = Math.Min(152, area.Height);
        var next = new PanelBounds(area.Right - width, Math.Clamp(anchorTop, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);
        if (!Near(displayed, next)) SetBounds(next);
    }

    public void RestorePanel()
    {
        if (stopped || IsBusy) return;
        SetEdgeHidden(false); WindowState = WindowState.Normal; Show();
        if (state.Settings.Collapsed && !Execute(() => session.SetCollapsed(false), false)) { ApplyGeometry(true); return; }
        var area = Ui.WorkAreaDip(this);
        anchorRight = area.Right - (state.Settings.EdgeAutoHide ? 8 : 24); anchorTop = area.Top + 18;
        docked = state.Settings.EdgeAutoHide; hideArmed = false; waitsForEntry = true; hideAt = null;
        Render(); ApplyGeometry(true); SaveGeometry();
    }

    private void HeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsBusy || edgeHidden || !e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
        if (e.Source is Control control && (control is Button || control.FindAncestorOfType<Button>() != null)) return;
        moving = true; animating = false; hideArmed = false; hideAt = null;
        dragStart = this.PointToScreen(e.GetPosition(this)); dragPosition = Position; e.Pointer.Capture(header); e.Handled = true;
    }

    private void HeaderMoved(object? sender, PointerEventArgs e)
    {
        if (!moving) return;
        var current = this.PointToScreen(e.GetPosition(this)); var area = Ui.WorkArea(this); var scale = Math.Max(.1, RenderScaling);
        int width = (int)Math.Ceiling(Width * scale), height = (int)Math.Ceiling(Height * scale);
        int x = Math.Clamp(dragPosition.X + current.X - dragStart.X, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8));
        int y = Math.Clamp(dragPosition.Y + current.Y - dragStart.Y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - height - 8));
        Position = new PixelPoint(x, y); anchorRight = x / scale + Width; anchorTop = y / scale;
        displayed = new PanelBounds(x / scale, y / scale, Width, Height); e.Handled = true;
    }

    private void HeaderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!moving) return;
        FinishMove(); e.Pointer.Capture(null); e.Handled = true;
    }

    private void FinishMove()
    {
        moving = false; var area = Ui.WorkAreaDip(this);
        docked = state.Settings.EdgeAutoHide && Math.Abs(area.Right - anchorRight) <= 32;
        if (docked) { anchorRight = area.Right - 8; ApplyGeometry(true); }
        SaveGeometry();
    }

    private void ResizeBy(double dx, double dy)
    {
        if (!resizing || !grip.IsEnabled || edgeHidden) return;
        var area = Ui.WorkAreaDip(this); double left = displayed.Left;
        double maxWidth = Math.Min(area.Width * .5, Math.Max(1, area.Right - left - 8));
        double maxHeight = Math.Min(area.Height * (1100d / 1528), Math.Max(1, area.Bottom - displayed.Top - 8));
        baseWidth = Math.Clamp(Width + dx, Math.Min(320, maxWidth), maxWidth);
        baseHeight = Math.Clamp(Height + dy, Math.Min(280, maxHeight), maxHeight);
        anchorRight = left + baseWidth;
        SetBounds(new PanelBounds(left, anchorTop, baseWidth, baseHeight));
    }

    private bool SaveGeometry()
    {
        if (!ready || stopped) return true;
        return Execute(() => session.SaveGeometry(anchorRight - baseWidth, anchorTop, baseWidth, baseHeight), false);
    }

    public bool SaveBeforeExit()
    {
        if (stopped) return true;
        if (IsBusy) { ReportStatus("请先完成或取消当前编辑，再退出。"); return false; }
        return SaveGeometry();
    }

    public void StopTimers()
    {
        if (stopped) return;
        stopped = true; animating = false; hideAt = null; timer.Stop(); timer.Tick -= OnTick;
    }

    internal void SetEdgeModeForPreview(bool enabled)
    {
        if (!PreviewMode) throw new InvalidOperationException("Preview control requires preview mode.");
        if (state.Settings.EdgeAutoHide != enabled) Execute(() => session.SetEdgeAutoHide(enabled), false);
        docked = enabled; hideArmed = false; hideAt = null;
        if (!enabled) SetEdgeHidden(false);
        Render(); ApplyGeometry(true);
    }

    internal void SetEdgeHiddenForPreview(bool hidden)
    {
        if (!PreviewMode) throw new InvalidOperationException("Preview control requires preview mode.");
        SetEdgeHidden(hidden); if (!hidden) ApplyGeometry(true);
    }

    internal void ObservePointerForPreview(bool inside)
    {
        if (!PreviewMode) throw new InvalidOperationException("Preview control requires preview mode.");
        ObservePointer(inside);
    }

    internal void ObserveMenuForPreview(bool opened)
    {
        if (!PreviewMode) throw new InvalidOperationException("Preview control requires preview mode.");
        ObserveMenu(opened);
    }

    internal void TickForPreview()
    {
        if (!PreviewMode) throw new InvalidOperationException("Preview control requires preview mode.");
        if (stopped) return;
        if (!ready) InitializeGeometry();
        OnTick(null, EventArgs.Empty);
    }

    private async void ShowHelp()
    {
        if (IsBusy || stopped) return;
        dialogs++; animating = false; RevealEdge();
        try
        {
            var dialog = new Window { Title = "使用说明", Width = 470, Height = 440, MinWidth = 300, MinHeight = 250,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            Ui.Style(dialog); Ui.FitDialog(dialog, this);
            var root = new DockPanel { Margin = new Thickness(22) };
            var close = Ui.Button("关闭", () => dialog.Close()); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
            root.Children.Add(new ScrollViewer { Content = Ui.Text("点击勾选框完成／恢复事项，双击文字或右击编辑。\n小时和分钟可分别点选，也支持键盘输入。\n细条表示经过的时间，到期满格；它不代表任务完成度。\n\n贴边自动收起默认关闭。开启后拖到主屏右侧，鼠标离开约0.8秒收起；点击胶囊展开。\n编辑与菜单打开时不收起，收起后不会因到期自动弹出。\n可用菜单栏或“恢复右上角位置”找回。\n\n这是实验性Mac内测版，窗口层级、Spaces、全屏、睡眠唤醒及多屏表现需要真机验证。\n\n数据保存在：\n" + dataDirectory) });
            dialog.Content = root; await dialog.ShowDialog(this);
        }
        catch (Exception exception) { ReportStatus("使用说明未能打开：" + exception.Message); }
        finally { dialogs--; hideAt = null; }
    }

    private sealed record TaskRow(TodoItem Item, TextBlock Label, TextBlock Due, Border Track, Border Progress);
}
