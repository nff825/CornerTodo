using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CornerTodo;

internal static class OffscreenUiTests
{
    private static readonly StringBuilder Report = new StringBuilder();
    private static string outputDirectory;
    private static int assertions;
    private const double Scale = 1.5;
    private const double Dpi = 144;

    [STAThread]
    public static int Main(string[] args)
    {
        Application application = null;
        try
        {
            outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual-qa"));
            Directory.CreateDirectory(outputDirectory);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Report.AppendLine("WPF offscreen UI validation at 144 dpi (150%): " + DateTime.Now.ToString("O"));
            Report.AppendLine("No Window.Show, native desktop attachment, or production data directory is used.");
            DateTime now = DateTime.Now;
            RenderScenario("normal", Scenario(now, false, false), now);
            RenderScenario("urgent", Scenario(now, true, false), now);
            RenderScenario("overdue", Scenario(now, true, true), now);
            ValidateBaselineSave();
            ValidateLegacyUtcEditor();
            ValidateSortPersistence();
            RenderOptionalDeadlinePreviews(now);
            RenderRecurringPreviews(now);
            EdgeUiTests.Run(outputDirectory);
            Report.AppendLine("PASS: " + assertions + " assertions across 3 deadline scenarios and optional-deadline theme/editor renders.");
            Report.AppendLine("Scope: control tree, layout, typography, counts, completed decoration, settings state, render pixels and fake-host baseline persistence.");
            Report.AppendLine("Live desktop z-order, animations and native input are intentionally outside this offscreen check.");
            File.WriteAllText(Path.Combine(outputDirectory, "ui-validation.txt"), Report.ToString(), new UTF8Encoding(false));
            Console.Write(Report.ToString());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            if (outputDirectory != null)
                File.WriteAllText(Path.Combine(outputDirectory, "ui-validation-failure.txt"),
                    Report.ToString() + Environment.NewLine + error, new UTF8Encoding(false));
            return 1;
        }
        finally
        {
            if (application != null) application.Shutdown();
        }
    }

    private static TodoState Scenario(DateTime now, bool urgent, bool overdue)
    {
        TodoState state = new TodoState();
        state.Settings.Width = 430; state.Settings.Height = 475;
        state.Settings.AutoExpand = true;
        state.Items.Add(Item("normal-one", "向李老师汇报研究进展", now.AddDays(2), now.AddDays(-1), false));
        state.Items.Add(Item("normal-two", "整理实验数据并核对下周测量方案", now.AddDays(3), now.AddDays(-2), false));
        state.Items[0].Notes = "汇总本周实验结果\n准备下阶段研究计划";
        state.Items[0].Priority = TaskPriority.High;
        state.Items[1].Priority = TaskPriority.Low;
        if (urgent) state.Items.Add(Item("urgent", "与王同学确认下午的实验方案", now.AddHours(2), now.AddHours(-22), false));
        if (overdue) state.Items.Add(Item("overdue", "给张老师发送论文修改稿", now.AddHours(-3), now.AddDays(-2), false));
        state.Items.Add(Item("completed", "完成阶段实验记录归档", now.AddHours(-6), now.AddDays(-2), true));
        return state;
    }

    private static TodoItem Item(string id, string text, DateTime deadline, DateTime scheduled, bool complete)
    {
        return new TodoItem { Id = id, Text = text, DueDate = deadline, HasTime = true,
            CreatedAt = scheduled, DeadlineSetAt = scheduled, IsCompleted = complete,
            CompletedAt = complete ? (DateTime?)deadline.AddHours(-1) : null };
    }

    private static void RenderScenario(string name, TodoState state, DateTime now)
    {
        string isolatedData = Path.Combine(outputDirectory, "isolated-data", name);
        TodoStore store = new TodoStore(isolatedData);
        MainWindow window = new MainWindow(store, state, true, true);
        try
        {
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "An unshown window unexpectedly has an HWND.");
            Border surface = window.Panel.Surface;
            window.Content = null;
            // Detachment removes inherited Window properties; reproduce those
            // properties explicitly so these are the actual production fonts.
            TextElement.SetFontFamily(surface, window.FontFamily);
            TextElement.SetForeground(surface, window.Foreground);
            surface.Language = window.Language;
            surface.UseLayoutRounding = window.UseLayoutRounding;
            surface.SnapsToDevicePixels = window.SnapsToDevicePixels;

            PanelBounds workArea = new PanelBounds(0, 0, 2560 / Scale, 1528 / Scale);
            double progress = TodoRules.Expansion(state, now);
            PanelBounds target = GrowthLayout.Target(workArea, workArea.Right - 24, 18,
                state.Settings.Width, state.Settings.Height, progress, false);
            Grid canvas = new Grid { Width = target.Width, Height = target.Height,
                Background = new LinearGradientBrush(Color.FromRgb(183, 219, 226), Color.FromRgb(136, 190, 201), 90) };
            canvas.Children.Add(surface);
            canvas.Measure(new Size(target.Width, target.Height));
            canvas.Arrange(new Rect(0, 0, target.Width, target.Height));
            canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            canvas.UpdateLayout();

            ValidateControls(window, state, surface, now);
            ScrollViewer scroll = window.Panel.Scroll;
            Assert(scroll.ExtentHeight <= scroll.ViewportHeight + 0.5,
                name + ": these sample tasks should fit without clipping or scrolling.");

            int pixelWidth = (int)Math.Ceiling(target.Width * Scale);
            int pixelHeight = (int)Math.Ceiling(target.Height * Scale);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, Dpi, Dpi, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            Assert(HasTextPixels(bitmap), name + ": rendered output lacks non-background text pixels.");
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string imagePath = Path.Combine(outputDirectory, name + ".png");
            using (FileStream output = File.Create(imagePath)) encoder.Save(output);
            if (name == "normal") Assert(pixelWidth == 645 && pixelHeight == 713, "Normal dimensions should be 645x713 physical pixels.");
            if (name == "overdue") Assert(pixelWidth == 1280 && pixelHeight == 1100, "Overdue dimensions should be the physical maximum.");
            Report.AppendLine(name + ": " + pixelWidth + "x" + pixelHeight + " px; " +
                target.Width.ToString("0.###", CultureInfo.InvariantCulture) + "x" +
                target.Height.ToString("0.###", CultureInfo.InvariantCulture) + " DIP; progress=" +
                progress.ToString("0.###", CultureInfo.InvariantCulture) + "; extent=" +
                scroll.ExtentHeight.ToString("0.###", CultureInfo.InvariantCulture) + "; viewport=" +
                scroll.ViewportHeight.ToString("0.###", CultureInfo.InvariantCulture));
            ValidateMenuAndCollapsedState(window, state, now);
            Assert(!Directory.Exists(isolatedData), "Preview UI unexpectedly wrote data files.");
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "Offscreen checks unexpectedly created an HWND.");
        }
        finally { window.Close(); }
    }

    private static void RenderOptionalDeadlinePreviews(DateTime now)
    {
        TodoState state = new TodoState(); state.Settings.AutoExpand = false;
        state.Items.Add(Item("dated-one", "向李老师汇报研究进展", now.Date.AddDays(2).AddHours(17), now.AddDays(-1), false));
        state.Items[0].Priority = TaskPriority.High;
        TodoItem second = Item("dated-two", "提交本月研究小结", now.Date.AddDays(5), now.AddDays(-2), false);
        second.HasTime = false; state.Items.Add(second);
        string[] titles = { "整理实验数据", "阅读新方向文献", "整理书架", "补充研究参考资料" };
        for (int index = 0; index < titles.Length; index++) state.Items.Add(new TodoItem {
            Id = "undated-" + index, Text = titles[index], HasDeadline = false,
            Priority = index == 0 ? TaskPriority.Normal : TaskPriority.Low, CreatedAt = now.AddDays(-7 + index)
        });
        state.Items.Add(Item("done", "完成阶段实验记录归档", now.AddHours(-1), now.AddDays(-3), true));
        AppearanceSettings[] themes = { AppearanceSettings.CreateLight(), AppearanceSettings.CreateDark(), AppearanceSettings.CreateDark() };
        themes[2].BackgroundTransparency = 100;
        string[] names = { "no-deadline-panel", "no-deadline-panel-dark", "no-deadline-panel-transparent" };
        for (int index = 0; index < themes.Length; index++)
        {
            TodoPanelView view = new TodoPanelView(new TodoViewActions());
            TextElement.SetFontFamily(view.Surface, new FontFamily("Microsoft YaHei UI"));
            view.Surface.UseLayoutRounding = true; view.Surface.SnapsToDevicePixels = true;
            view.Render(state, now, true, themes[index]);
            Grid canvas = new Grid { Background = index == 0 ? Theme.Brush("#AFCFD9") : Theme.Brush("#132631") };
            canvas.Children.Add(view.Surface); WritePreview(canvas, 430, 475, names[index]);
            Assert(view.Scroll.ScrollableHeight > 0 && view.Scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                "Optional-deadline fixture must overflow so its slim scrollbar is a real viewport indicator.");
            foreach (Grid row in view.List.Children.OfType<Grid>().Where(row => (row.Tag as string ?? "").StartsWith("undated-", StringComparison.Ordinal)))
            {
                Assert(!Descendants(row).OfType<Grid>().Any(grid => Object.Equals(grid.Tag, "time-progress")), "Undated preview row displayed an elapsed bar.");
                Assert(Descendants(row).OfType<TextBlock>().Any(text => text.Text == "未设截止"), "Undated preview row lost its status label.");
            }
            Assert(view.AttentionText.Text == "逾期 0 项 · 6小时内 0 项", "Undated preview fixture contributed to attention statistics.");
            view.Scroll.ScrollToBottom(); canvas.UpdateLayout();
            Assert(Math.Abs(view.Scroll.VerticalOffset - view.Scroll.ScrollableHeight) < 1, "Optional-deadline preview cannot reach its final row.");
        }
        TaskEditor editor = new TaskEditor(state.Items.First(item => !item.HasDeadline));
        try
        {
            FrameworkElement content = (FrameworkElement)editor.Content; editor.Content = null;
            TextElement.SetFontFamily(content, editor.FontFamily); TextElement.SetFontSize(content, editor.FontSize);
            var canvas = new Grid { Background = editor.Background }; canvas.Children.Add(content);
            WritePreview(canvas, editor.Width, editor.Height, "no-deadline-editor");
            Assert(Field<CheckBox>(editor, "useDeadline").IsChecked == false && Field<StackPanel>(editor, "deadlineFields").Visibility == Visibility.Collapsed,
                "Undated editor preview should collapse date/time controls while keeping its enable option.");
            Button save = Descendants(canvas).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "保存待办");
            Rect bounds = save.TransformToAncestor(canvas).TransformBounds(new Rect(new Point(), save.RenderSize));
            Assert(bounds.Bottom <= canvas.ActualHeight && bounds.Right <= canvas.ActualWidth, "Undated editor preview clipped its save action.");
            Assert(new WindowInteropHelper(editor).Handle == IntPtr.Zero, "Optional-deadline rendering created a native editor window.");
        }
        finally { editor.Close(); }
        Report.AppendLine("  optional deadline previews: light/dark/transparent overflow lists and undated editor rendered with production controls.");
    }

    private static void RenderRecurringPreviews(DateTime now)
    {
        // Keep all recurrence examples on the same processed Monday, independent
        // of the machine clock used by the other deadline scenarios.
        now = new DateTime(2026, 11, 2, 10, 0, 0);
        TaskEditor compact = new TaskEditor(new TodoItem { Text = "整理研究记录", HasDeadline = false });
        try {
            RenderRecurringWindow(compact, "recurring-editor-default");
            Assert(!Field<CheckBox>(compact, "useRecurrence").IsChecked.Value && Field<StackPanel>(compact, "recurrenceFields").Visibility == Visibility.Collapsed,
                "Recurring default preview must be compact and unchecked.");
        } finally { compact.Close(); }
        TaskEditor expanded = new TaskEditor(new TodoItem { Text = "整理研究记录", HasDeadline = false });
        try {
            Field<CheckBox>(expanded, "useRecurrence").IsChecked = true;
            Field<ComboBox>(expanded, "recurrenceFrequency").SelectedIndex = 1;
            CheckBox[] days = Field<CheckBox[]>(expanded, "weekDays");
            for (int index = 0; index < days.Length; index++) days[index].IsChecked = index == 1 || index == 5;
            Field<DatePicker>(expanded, "recurrenceStart").SelectedDate = now.Date.AddDays(((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7);
            RenderRecurringWindow(expanded, "recurring-editor-weekly");
            Assert(Field<StackPanel>(expanded, "recurrenceFields").Visibility == Visibility.Visible && expanded.Height > compact.Height,
                "Enabled recurring preview did not expand.");
        } finally { expanded.Close(); }
        var state = new TodoState(); state.Settings.AutoExpand = false;
        var weekly = new RecurringTaskRule { Id = "preview-weekly", Text = "整理研究记录", HasDeadline = false,
            Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Weekly, WeekDays = 34, StartDate = now.Date.AddDays(-7) }, ProcessedThrough = now.Date };
        var daily = new RecurringTaskRule { Id = "preview-daily", Text = "阅读新方向文献", HasDeadline = false, IsPaused = true,
            Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Daily, StartDate = now.Date.AddDays(-7) }, ProcessedThrough = now.Date };
        var monthly = new RecurringTaskRule { Id = "preview-monthly", Text = "核对月度报销", HasDeadline = false,
            Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 10, StartDate = now.Date }, ProcessedThrough = now.Date };
        state.RecurringRules.Add(weekly); state.RecurringRules.Add(daily); state.RecurringRules.Add(monthly);
        state.Items.Add(Item("recurring-dated", "向李老师汇报研究进展", now.Date.AddHours(17), now.AddDays(-2), false));
        state.Items[0].Priority = TaskPriority.High;
        state.Items.Add(Item("recurring-report", "提交本月研究小结", new DateTime(2026, 11, 10), now.AddDays(-3), false));
        DateTime occurrence = now.Date;
        while ((weekly.Recurrence.WeekDays & (1 << (int)occurrence.DayOfWeek)) == 0) occurrence = occurrence.AddDays(-1);
        state.Items.Add(new TodoItem { Id = "recurring-weekly-item", Text = weekly.Text, HasDeadline = false, CreatedAt = occurrence,
            RecurringRuleId = weekly.Id, OccurrenceDate = occurrence, RepeatLabel = RecurringTasks.Label(weekly.Recurrence) });
        state.Items.Add(new TodoItem { Id = "recurring-daily-item", Text = daily.Text, HasDeadline = false, CreatedAt = now.Date,
            RecurringRuleId = daily.Id, OccurrenceDate = now.Date, RepeatLabel = RecurringTasks.Label(daily.Recurrence), Priority = TaskPriority.Low });
        state.Items.Add(new TodoItem { Id = "recurring-completed", Text = "归档阶段实验记录", HasDeadline = false, CreatedAt = now.AddDays(-2), IsCompleted = true, CompletedAt = now.AddHours(-1) });
        var panel = new TodoPanelView(new TodoViewActions());
        TextElement.SetFontFamily(panel.Surface, new FontFamily("Microsoft YaHei UI")); panel.Render(state, now, true, null);
        var canvas = new Grid { Background = Theme.Brush("#AFCFD9") }; canvas.Children.Add(panel.Surface);
        WritePreview(canvas, 430, 570, "recurring-panel");
        Assert(Descendants(canvas).OfType<TextBlock>().Any(text => text.Text.Contains("↻ 每周")), "Recurring panel omitted its compact loop label.");
        var manager = new RecurringTasksWindow(delegate { return TodoStateCopy.Copy(state); }, null, null, null, null, delegate { return now; });
        try {
            RenderRecurringWindow(manager, "recurring-management");
            manager.Width = 320; manager.Height = 280;
            RenderRecurringWindow(manager, "recurring-management-small");
        }
        finally { manager.Close(); }
        var catchUp = new RecurrenceCatchUpWindow(8, 2, delegate { return false; });
        try { RenderRecurringWindow(catchUp, "recurring-catch-up"); }
        finally { catchUp.Close(); }
        Report.AppendLine("  recurring previews: compact/weekly editors, mixed list, rules management and catch-up choices rendered with production controls.");
    }

    private static void RenderRecurringWindow(Window window, string name)
    {
        FrameworkElement content = (FrameworkElement)window.Content; window.Content = null;
        TextElement.SetFontFamily(content, window.FontFamily); TextElement.SetFontSize(content, window.FontSize);
        // Preserve inherited layout rounding when moving the real content out of
        // its unshown Window; otherwise fractional button positions can look cut
        // in a 150% bitmap even though the native Window rounds its layout.
        var canvas = new Grid { Background = window.Background, UseLayoutRounding = window.UseLayoutRounding,
            SnapsToDevicePixels = window.SnapsToDevicePixels }; canvas.Children.Add(content);
        try {
            WritePreview(canvas, window.Width, window.Height, name);
            foreach (Button button in Descendants(canvas).OfType<Button>().Where(button => AutomationProperties.GetName(button) == "保存待办" || AutomationProperties.GetName(button) == "关闭重复任务管理")) {
                Rect bounds = button.TransformToAncestor(canvas).TransformBounds(new Rect(button.RenderSize));
                Assert(bounds.Bottom <= canvas.ActualHeight + 1 && bounds.Right <= canvas.ActualWidth + 1, name + ": footer action clipped.");
            }
            if (window is RecurringTasksWindow) {
                var scroll = Descendants(canvas).OfType<ScrollViewer>().Single(view => AutomationProperties.GetName(view) == "重复规则列表");
                ValidateRecurringActionLayout(canvas, scroll, name, false);
                if (scroll.ScrollableHeight > 0) {
                    scroll.ScrollToBottom(); canvas.UpdateLayout();
                    ValidateRecurringActionLayout(canvas, scroll, name, true);
                    WritePreview(canvas, window.Width, window.Height, name + "-bottom");
                }
            }
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, name + ": created a native window.");
        }
        finally { canvas.Children.Clear(); window.Content = content; }
    }

    private static void ValidateRecurringActionLayout(FrameworkElement canvas, ScrollViewer scroll, string name, bool bottom)
    {
        Button[] commands = Descendants(scroll).OfType<Button>().Where(button =>
            AutomationProperties.GetName(button).StartsWith("编辑后续规则：", StringComparison.Ordinal) ||
            AutomationProperties.GetName(button).StartsWith("切换重复规则：", StringComparison.Ordinal) ||
            AutomationProperties.GetName(button).StartsWith("停止后续重复：", StringComparison.Ordinal)).ToArray();
        Assert(commands.Length == 9, name + ": each of three rules must retain all three actions.");
        Button[] row = bottom ? commands.Skip(commands.Length - 3).ToArray() : scroll.ScrollableHeight <= 0 ? commands : commands.Take(3).ToArray();
        Rect viewport = scroll.TransformToAncestor(canvas).TransformBounds(new Rect(scroll.RenderSize));
        double previousBottom = Double.NegativeInfinity;
        foreach (Button button in row) {
            Rect bounds = button.TransformToAncestor(canvas).TransformBounds(new Rect(button.RenderSize));
            Assert(bounds.Width > 0 && bounds.Height >= 20 && bounds.Top >= previousBottom - 0.1,
                name + ": action buttons overlap or collapse.");
            previousBottom = bounds.Bottom;
            Assert(bounds.Left >= viewport.Left - 1 && bounds.Right <= viewport.Right + 1 && bounds.Top >= viewport.Top - 1 && bounds.Bottom <= viewport.Bottom + 1,
                name + ": top/bottom rule actions were clipped by their scrolling viewport.");
            var presenter = Descendants(button).OfType<ContentPresenter>().Single();
            Assert(presenter.ActualHeight >= 16 && presenter.ActualWidth > 0 && presenter.Clip == null,
                name + ": caption presenter clips its text.");
            Report.AppendLine("  " + name + " " + button.Content + ": y=" + bounds.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                ", h=" + bounds.Height.ToString("0.###", CultureInfo.InvariantCulture) + ", caption=" + presenter.ActualHeight.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    private static void WritePreview(Grid canvas, double width, double height, string name)
    {
        canvas.Width = width; canvas.Height = height;
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale), Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream output = File.Create(Path.Combine(outputDirectory, name + ".png"))) encoder.Save(output);
    }

    private static void ValidateSortPersistence()
    {
        string isolatedData = Path.Combine(outputDirectory, "isolated-data", "sort-persistence");
        TodoStore store = new TodoStore(isolatedData);
        DateTime now = DateTime.Now;
        TodoState initial = new TodoState(); initial.Settings.AutoExpand = false;
        TodoItem near = Item("near", "近期的中优先级事项", now.AddDays(1), now.AddDays(-1), false);
        TodoItem high = Item("high", "远期的高优先级事项", now.AddDays(30), now.AddDays(-5), false);
        high.Priority = TaskPriority.High; high.Notes = "保存后重启仍保留\n第二行";
        initial.Items.Add(near); initial.Items.Add(high); store.Save(initial);
        MainWindow window = new MainWindow(store, initial, true, false);
        try {
            Button sorting = Descendants(window.Panel.List).OfType<Button>().Single(button =>
                System.Windows.Automation.AutomationProperties.GetName(button) == "排序方式");
            MenuItem priority = sorting.ContextMenu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, TaskSortMode.Priority));
            priority.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, priority));
            Assert(window.CurrentState.Settings.SortMode == TaskSortMode.Priority, "Main window did not commit the requested sort mode.");
            Assert(window.Panel.List.Children.OfType<Grid>().First(row => row.Tag is string).Tag.Equals("high"), "Saved sort did not repaint rows in priority order.");
            TodoState reloaded = new TodoStore(isolatedData).Load();
            Assert(reloaded.Settings.SortMode == TaskSortMode.Priority && reloaded.Items.Single(item => item.Id == "high").Notes == high.Notes,
                "Restart did not preserve sorting together with multiline notes.");
            DateTime? epoch = window.CurrentState.Items.Single(item => item.Id == "high").DeadlineSetAt;
            sorting = Descendants(window.Panel.List).OfType<Button>().Single(button =>
                System.Windows.Automation.AutomationProperties.GetName(button) == "排序方式");
            MenuItem deadline = sorting.ContextMenu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, TaskSortMode.Deadline));
            using (FileStream locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                deadline.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, deadline));
            }
            Assert(window.CurrentState.Settings.SortMode == TaskSortMode.Priority && window.Panel.StatusText.Visibility == Visibility.Visible,
                "A failed sort save changed committed state or hid its failure message.");
            Assert(sorting.ContextMenu.Items.OfType<MenuItem>().Single(item => item.IsChecked).Tag.Equals(TaskSortMode.Priority),
                "A failed main-window sort save left the wrong menu selection.");
            Assert(window.CurrentState.Items.Single(item => item.Id == "high").DeadlineSetAt == epoch,
                "Sorting reset the time-progress origin.");
            deadline.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, deadline));
            Assert(new TodoStore(isolatedData).Load().Settings.SortMode == TaskSortMode.Deadline,
                "Retrying after a released lock did not persist deadline order.");
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "Sorting validation unexpectedly showed a window.");
        }
        finally { window.Close(); }
        Report.AppendLine("  sort integration: persistence/reload, locked-file rollback and retry passed in isolated data.");
    }

    private static void ValidateControls(MainWindow window, TodoState state, Border surface, DateTime now)
    {
        List<TextBlock> texts = Descendants(surface).OfType<TextBlock>().ToList();
        TextBlock title = texts.Single(t => t.Text == "待办清单");
        Near(25, title.FontSize, "Title font size");
        foreach (TodoItem item in state.Items)
        {
            TextBlock text = texts.Single(t => t.Text == item.Text);
            double expectedSize = item.Id == "overdue" ? 21 : item.Id == "urgent" ? 19 : 15;
            Near(expectedSize, text.FontSize, item.Id + " task font size");
            Assert(text.FontFamily.Source == window.FontFamily.Source, item.Id + ": Window font was not preserved.");
            Assert(text.ActualHeight > 0 && text.ActualWidth > 0, item.Id + ": task text has zero layout dimensions.");
            if (item.IsCompleted)
                Assert(text.TextDecorations != null && text.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough),
                    "Completed task does not have a strikethrough.");
            else
                Assert(text.TextDecorations == null || text.TextDecorations.Count == 0, "Pending task was struck through.");
            if (item.Id == "urgent" || item.Id == "overdue")
                Assert(text.FontWeight == FontWeights.SemiBold, item.Id + ": highlighted task is not semibold.");
            Grid row = Descendants(surface).OfType<Grid>().Single(g => Object.Equals(g.Tag, item.Id));
            TextBlock deadline = Descendants(row).OfType<TextBlock>().Single(t => Object.Equals(t.Tag, "deadline"));
            Near(item.Id == "urgent" || item.Id == "overdue" ? 13 : 12, deadline.FontSize, item.Id + " deadline font size");
            if (item.Id == "urgent") Assert(deadline.Text.Contains("剩余"), "Urgent task lacks its remaining-time label.");
            if (item.Id == "overdue") Assert(deadline.Text.Contains("已逾期"), "Overdue task lacks its elapsed-time label.");
            CheckBox check = Descendants(row).OfType<CheckBox>().Single();
            Assert(check.IsChecked == item.IsCompleted, item.Id + ": completion checkbox has the wrong state.");
        }
        int pending = state.Items.Count(t => !t.IsCompleted);
        Assert(texts.Any(t => t.Text == "未完成  " + pending), "Pending section count is incorrect.");
        string summary = "逾期 " + state.Items.Count(t => t.Id == "overdue") + " 项 · 6小时内 " +
            state.Items.Count(t => t.Id == "urgent") + " 项";
        Assert(window.Panel.AttentionText.Text == summary, "Urgency summary is incorrect.");
        Assert(Descendants(surface).OfType<Button>().Any(b => (b.Content as string ?? "").Contains("已完成  1")),
            "Completed section count is incorrect.");
        Report.AppendLine("  typography: normal/completed=15, urgent=19, overdue=21; deadlines=12/13; " + summary);
    }

    private static void ValidateMenuAndCollapsedState(MainWindow window, TodoState state, DateTime now)
    {
        ContextMenu menu = (ContextMenu)Invoke(window, "BuildMenu");
        MenuItem automatic = menu.Items.OfType<MenuItem>().Single(i => Object.Equals(i.Header, "随截止时间扩大"));
        Assert(automatic.IsCheckable && automatic.IsChecked, "Automatic-growth menu is not initially checked.");
        automatic.IsChecked = false;
        automatic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, automatic));
        Assert(!window.CurrentState.Settings.AutoExpand, "The automatic-growth switch did not update state.");
        Assert(window.Panel.Grip.IsEnabled, "Manual sizing must be available when automatic growth is off.");
        automatic.IsChecked = true;
        automatic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, automatic));
        Assert(window.CurrentState.Settings.AutoExpand, "Automatic growth could not be re-enabled.");
        Assert(window.Panel.Grip.IsEnabled == (TodoRules.Expansion(state, now) <= 0.00001),
            "Sizing-grip availability does not reflect attention-driven growth.");
        Invoke(window, "TogglePanel");
        Assert(window.CurrentState.Settings.Collapsed, "Collapsed state was not saved in the UI model.");
        Assert(window.Panel.Scroll.Visibility == Visibility.Collapsed, "Collapsed panel still displays its task list.");
        Assert(window.Panel.AddButton.Visibility == Visibility.Collapsed, "Collapsed panel still displays its add button.");
        Assert(window.Panel.DateText.Text.Contains("6小时内"), "Collapsed header lacks its attention summary.");
        Invoke(window, "TogglePanel");
        Assert(!window.CurrentState.Settings.Collapsed, "Collapsed panel could not be expanded.");
        Report.AppendLine("  interaction state: auto off/on and collapse/expand passed without showing UI.");
    }

    private static void ValidateBaselineSave()
    {
        // Inject only a fake screen host. Exercise the real controller ->
        // MainWindow.SaveGeometry -> preview session connection without HWNDs.
        var host = new BaselineHost();
        string dataPath = Path.Combine(outputDirectory, "isolated-data", "baseline-controller");
        var state = new TodoState { Settings = new WidgetSettings { Width = 430, Height = 475, Left = 1170, Top = 18 } };
        var window = new MainWindow(new TodoStore(dataPath), state, true, true, null, host);
        try
        {
            window.Interaction.Initialize(window.CurrentState.Settings);
            Assert(window.Interaction.IsReady && host.SetCalls == 0, "Initialization should calculate the baseline without moving a native window.");
            window.Interaction.UpdateExpansion(1, false, true, true);
            Assert(host.Bounds.Width > 430 && host.Bounds.Height > 475, "The fake host did not receive expanded panel bounds.");
            Near(1600, host.Bounds.Right, "Expanded right anchor"); Near(18, host.Bounds.Top, "Expanded top anchor");
            Rect baseline = window.Interaction.BaselineBounds;
            Near(430, baseline.Width, "Controller baseline width after expansion");
            Near(475, baseline.Height, "Controller baseline height after expansion");
            Near(1170, baseline.Left, "Controller baseline left"); Near(18, baseline.Top, "Controller baseline top");
            // A changed anchor makes this fail if the window forgets to call its
            // session: unchanged constructor settings alone would hide a no-op save.
            window.Interaction.BeginMove(new Point(0, 0));
            window.Interaction.MoveTo(new Point(-40, 12));
            window.Interaction.EndMove();
            Near(1170, window.CurrentState.Settings.Left.Value, "Uncommitted movement left");
            Near(18, window.CurrentState.Settings.Top.Value, "Uncommitted movement top");
            window.SaveGeometry();
            WidgetSettings saved = window.CurrentState.Settings;
            Near(430, saved.Width, "Saved baseline width"); Near(475, saved.Height, "Saved baseline height");
            Near(1130, saved.Left.Value, "Saved moved baseline left"); Near(30, saved.Top.Value, "Saved moved baseline top");
            Assert(!Directory.Exists(dataPath), "Preview baseline persistence wrote application data.");
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "The fake-host baseline check unexpectedly created an HWND.");
            Report.AppendLine("  baseline persistence: fake-host expansion retains 430x475; movement updates the anchor; MainWindow.SaveGeometry commits moved baseline bounds to its preview session.");
        }
        finally { window.Close(); }
    }

    private sealed class BaselineHost : IPanelHost
    {
        public Rect Bounds = new Rect(1170, 18, 430, 475);
        public int SetCalls;
        public Rect GetBounds() { return Bounds; }
        public void SetBounds(Rect bounds) { Bounds = bounds; SetCalls++; }
        public Rect WorkArea() { return new Rect(0, 0, 2560, 1528); }
    }

    private static void ValidateLegacyUtcEditor()
    {
        TodoItem original = new TodoItem {
            Id = "legacy-utc-with-seconds", Text = "旧版 UTC 事项",
            DueDate = new DateTime(2026, 10, 21, 5, 47, 29, DateTimeKind.Utc).AddTicks(1234),
            HasTime = true, CreatedAt = new DateTime(2026, 10, 19, 7, 10, 11, DateTimeKind.Utc),
            DeadlineSetAt = new DateTime(2026, 10, 19, 8, 11, 12, DateTimeKind.Utc).AddTicks(5678)
        };
        DateTime local = original.DueDate.ToLocalTime();
        TaskEditor textOnly = new TaskEditor(original);
        try
        {
            Assert(Field<DatePicker>(textOnly, "date").SelectedDate == local.Date,
                "Legacy UTC deadline does not display the local calendar date.");
            Assert(Field<ComboBox>(textOnly, "hour").Text == local.ToString("HH") &&
                Field<ComboBox>(textOnly, "minute").Text == local.ToString("mm"),
                "Legacy UTC deadline does not display the local clock time.");
            Field<TextBox>(textOnly, "taskText").Text = "仅修改文字，保留原截止精度";
            SaveUnshownEditor(textOnly);
            TaskDraft draft = textOnly.Result;
            Assert(draft != null && draft.Text == "仅修改文字，保留原截止精度", "Text-only editor did not produce the expected draft.");
            Assert(draft.DueDate.Ticks == original.DueDate.Ticks, "Text-only draft changed seconds or subsecond precision.");
            Assert(draft.DueDate.Kind == DateTimeKind.Utc, "Text-only draft changed the UTC deadline kind.");
            Assert(draft.HasTime == original.HasTime, "Text-only draft changed the deadline precision mode.");
            string isolatedData = Path.Combine(outputDirectory, "isolated-data", "editor-text-only");
            TodoState state = new TodoState(); state.Items.Add(original);
            TodoSession session = new TodoSession(new TodoStore(isolatedData), state, true);
            session.Edit(original.Id, draft);
            TodoItem saved = session.Snapshot().Items.Single(item => item.Id == original.Id);
            Assert(saved.Text == draft.Text && saved.DueDate.Ticks == original.DueDate.Ticks && saved.DueDate.Kind == original.DueDate.Kind,
                "Committing a text-only draft changed its text or exact deadline.");
            Assert(saved.DeadlineSetAt.HasValue &&
                saved.DeadlineSetAt.Value.Ticks == original.DeadlineSetAt.Value.Ticks &&
                saved.DeadlineSetAt.Value.Kind == original.DeadlineSetAt.Value.Kind,
                "Committing a text-only draft restarted the original scheduling interval.");
            Assert(saved.Id == original.Id && saved.CreatedAt == original.CreatedAt && saved.CreatedAt.Kind == original.CreatedAt.Kind,
                "Committing a text-only draft changed task identity or creation time.");
            Assert(saved.IsCompleted == original.IsCompleted && saved.CompletedAt == original.CompletedAt,
                "Committing a text-only draft changed completion state.");
            Assert(!Directory.Exists(isolatedData), "A preview session wrote editor data files.");
            Assert(new WindowInteropHelper(textOnly).Handle == IntPtr.Zero,
                "Text-only editor regression unexpectedly created an HWND.");
        }
        finally { textOnly.Close(); }

        TaskEditor deadlineEdit = new TaskEditor(original);
        try
        {
            Field<ComboBox>(deadlineEdit, "hour").Text = local.AddMinutes(1).ToString("HH");
            Field<ComboBox>(deadlineEdit, "minute").Text = local.AddMinutes(1).ToString("mm");
            SaveUnshownEditor(deadlineEdit);
            TaskDraft draft = deadlineEdit.Result;
            DateTime expected = local.Date.AddHours(local.Hour).AddMinutes(local.Minute + 1);
            Assert(draft != null && draft.DueDate.Ticks == expected.Ticks && draft.HasTime,
                "A real minute edit did not produce the entered local deadline draft.");
            Assert(draft.DueDate.Second == 0 && draft.DueDate.Ticks % TimeSpan.TicksPerSecond == 0,
                "A changed minute draft should use the minute precision supplied by the editor.");
            string isolatedData = Path.Combine(outputDirectory, "isolated-data", "editor-minute-edit");
            TodoState state = new TodoState(); state.Items.Add(original);
            TodoSession session = new TodoSession(new TodoStore(isolatedData), state, true);
            DateTime before = DateTime.Now;
            session.Edit(original.Id, draft);
            DateTime after = DateTime.Now;
            TodoItem saved = session.Snapshot().Items.Single(item => item.Id == original.Id);
            Assert(TodoRules.Deadline(saved).Ticks == expected.Ticks,
                "Committing a minute-change draft did not update the effective deadline.");
            Assert(saved.DeadlineSetAt.HasValue && saved.DeadlineSetAt.Value >= before &&
                saved.DeadlineSetAt.Value <= after && saved.DeadlineSetAt.Value.Kind == DateTimeKind.Local,
                "Committing a real deadline edit did not reset DeadlineSetAt to the session save time.");
            Assert(saved.DeadlineSetAt.Value.Ticks != original.DeadlineSetAt.Value.Ticks,
                "A real deadline edit kept the old scheduling interval.");
            Assert(saved.Id == original.Id && saved.CreatedAt == original.CreatedAt && saved.CreatedAt.Kind == original.CreatedAt.Kind &&
                saved.IsCompleted == original.IsCompleted && saved.CompletedAt == original.CompletedAt,
                "Committing a changed deadline altered identity, creation time or completion state.");
            Assert(original.DueDate.Second == 29 && original.DueDate.Kind == DateTimeKind.Utc,
                "Preparing or committing a draft mutated the original task.");
            Assert(!Directory.Exists(isolatedData), "A preview session wrote changed-deadline data files.");
            Assert(new WindowInteropHelper(deadlineEdit).Handle == IntPtr.Zero,
                "Deadline editor regression unexpectedly created an HWND.");
        }
        finally { deadlineEdit.Close(); }
        Report.AppendLine("editor/session UTC regression: local date/time display, exact draft mapping, text-only identity/timing preservation, genuine minute-change scheduling reset, and preview without writes passed.");
    }

    private static void SaveUnshownEditor(TaskEditor editor)
    {
        try { Invoke(editor, "Save"); }
        catch (TargetInvocationException error)
        {
            // Save assigns Result before setting DialogResult. An unshown test
            // window is deliberately outside ShowDialog; allow only that exact
            // framework setter failure, never a validation or parsing failure.
            Exception inner = error.InnerException;
            bool expected = inner is InvalidOperationException && editor.Result != null &&
                inner.StackTrace != null && inner.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) >= 0;
            if (!expected) throw;
        }
        Assert(editor.Result != null, "Editor validation returned no result: " + Field<TextBlock>(editor, "error").Text);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }

    private static bool HasTextPixels(RenderTargetBitmap bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int dark = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 200 && pixels[i] < 130 && pixels[i + 1] < 130 && pixels[i + 2] < 130) dark++;
        return dark > 400;
    }

    private static T Field<T>(object instance, string name)
    {
        return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
    private static object Invoke(object instance, string name)
    {
        return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    }
    private static void Near(double expected, double actual, string message)
    {
        Assert(Math.Abs(expected - actual) < 0.000001, message + ": expected " + expected + ", got " + actual);
    }
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
