using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CornerTodo;

internal static class RecurringUiTests
{
    private static int checks;
    internal static void Run()
    {
        checks = 0;
        EditorDefaultsAndDrafts();
        FrequencyChoicesAndInstanceScope();
        RowActionsAndHistory();
        ManagementCommandsAndFailure();
        CatchUpChoices();
        MainWindowSchedulingGuards();
        RuleDraftRecoveryAndIoFailure();
        DynamicEditorWorkAreaBounds();
        Console.WriteLine("PASS: " + checks + " recurring editor/list/management/catch-up UI assertions; unshown controls only.");
    }

    private static void EditorDefaultsAndDrafts()
    {
        TaskDraft captured = null;
        var editor = new TaskEditor(null, false, delegate(TaskDraft draft) { captured = draft; return false; });
        try {
            using (var host = new Host(editor)) {
                CheckBox enabled = host.Find<CheckBox>("重复任务");
                Assert(enabled.IsChecked == false && enabled.Visibility == Visibility.Visible, "New tasks must show only an unchecked repeat entry.");
                Assert(host.Find<StackPanel>("重复任务设置").Visibility == Visibility.Collapsed, "Default repeat configuration must be collapsed, not disabled.");
                Assert(host.Find<CheckBox>("设置截止日期").IsChecked == true, "Repeat default changed the independent deadline default.");
                double compactHeight = editor.Height;
                host.Find<TextBox>("待办事项").Text = "整理研究记录";
                host.Find<CheckBox>("设置截止日期").IsChecked = false;
                enabled.IsChecked = true;
                Assert(host.Find<StackPanel>("重复任务设置").Visibility == Visibility.Visible, "Checking repeat did not reveal configuration.");
                ComboBox frequency = host.Find<ComboBox>("重复方式"); frequency.SelectedIndex = 1;
                foreach (CheckBox day in host.All<CheckBox>().Where(box => AutomationProperties.GetName(box).StartsWith("重复星期"))) day.IsChecked = false;
                host.Find<DatePicker>("重复开始日期").SelectedDate = new DateTime(2026, 11, 2);
                Click(host.Find<Button>("保存待办"));
                Assert(captured == null && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("至少选择"), "Weekly mode accepted no selected days.");
                host.Find<CheckBox>("重复星期一").IsChecked = true;
                host.Find<CheckBox>("重复星期五").IsChecked = true;
                Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.Recurrence != null && captured.Recurrence.Frequency == RecurrenceFrequency.Weekly, "Weekly draft was not forwarded.");
                Assert(captured.Recurrence.WeekDays == ((1 << 1) | (1 << 5)), "Weekly selection did not preserve Monday and Friday.");
                Assert(captured.Recurrence.StartDate == new DateTime(2026, 11, 2) && !captured.HasDeadline && captured.DueDate == DateTime.MinValue, "Repeat plan and optional deadline are not independent.");
                Assert(editor.Result == null && enabled.IsChecked == true && host.Find<TextBox>("待办事项").Text == "整理研究记录", "A failed save lost the repeat draft.");
                enabled.IsChecked = false; host.Layout();
                Assert(host.Find<StackPanel>("重复任务设置").Visibility == Visibility.Collapsed, "Clearing repeat did not collapse its region.");
                Assert(editor.Height < compactHeight + 172, "Collapsed settings left an expanded-height hole.");
                Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.Recurrence == null, "A disabled repeat option leaked its hidden configuration into the draft.");
                enabled.IsChecked = true;
                Assert(frequency.SelectedIndex == 1 && host.Find<CheckBox>("重复星期五").IsChecked == true, "Toggling repeat discarded unsaved configuration.");
            }
        }
        finally { editor.Close(); }
    }

    private static void FrequencyChoicesAndInstanceScope()
    {
        foreach (int index in new[] { 0, 2 }) {
            TaskDraft draft = null;
            var editor = new TaskEditor(null, false, delegate(TaskDraft value) { draft = value; return false; });
            try { using (var host = new Host(editor)) {
                host.Find<TextBox>("待办事项").Text = "周期草稿";
                host.Find<CheckBox>("设置截止日期").IsChecked = false;
                host.Find<CheckBox>("重复任务").IsChecked = true;
                host.Find<ComboBox>("重复方式").SelectedIndex = index;
                host.Find<ComboBox>("每月生成日期").SelectedIndex = 31;
                Click(host.Find<Button>("保存待办"));
                Assert(draft != null && draft.Recurrence.Frequency == (RecurrenceFrequency)(index + 1), "Mode selection was not saved.");
                if (index == 2) Assert(draft.Recurrence.UseLastDay && draft.Recurrence.DayOfMonth == 31, "Monthly last-day option was lost.");
                host.Find<DatePicker>("重复开始日期").Text = "";
                draft = null; Click(host.Find<Button>("保存待办"));
                Assert(draft == null, "A missing start date was accepted.");
            } } finally { editor.Close(); }
        }
        var occurrence = new TodoItem { Id = "occurrence", Text = "本次事项", HasDeadline = false,
            RecurringRuleId = "rule-one", OccurrenceDate = new DateTime(2026, 11, 2), RepeatLabel = "每日" };
        TaskDraft instanceDraft = null;
        var instanceEditor = new TaskEditor(occurrence, false, delegate(TaskDraft value) { instanceDraft = value; return false; });
        try { using (var host = new Host(instanceEditor)) {
            Assert(host.Find<CheckBox>("重复任务").Visibility == Visibility.Collapsed, "Instance editor must not edit future rules.");
            host.Find<TextBox>("待办事项").Text = "只修改本次"; Click(host.Find<Button>("保存待办"));
            Assert(instanceDraft != null && instanceDraft.Recurrence == null && occurrence.Text == "本次事项", "Instance edit changed its future template or source.");
        } } finally { instanceEditor.Close(); }
        var options = new RecurrenceOptions { Frequency = RecurrenceFrequency.Weekly, WeekDays = 34, StartDate = new DateTime(2026, 11, 2) };
        var templateEditor = new TaskEditor(new TodoItem { Text = "后续规则", HasDeadline = false }, false,
            delegate(TaskDraft value) { return false; }, options, true);
        try { using (var host = new Host(templateEditor)) {
            Assert(host.Find<CheckBox>("重复任务").IsChecked == true && host.Find<ComboBox>("重复方式").SelectedIndex == 1, "Rule edit did not restore its existing settings.");
            host.Find<CheckBox>("重复任务").IsChecked = false; Click(host.Find<Button>("保存待办"));
            Assert(templateEditor.Result == null && options.WeekDays == 34, "Cancelled stop must retain the source rule.");
        } } finally { templateEditor.Close(); }
    }

    private static TodoState Fixture()
    {
        var state = new TodoState(); state.Settings.AutoExpand = false;
        state.RecurringRules.Add(new RecurringTaskRule { Id = "rule-one", Text = "整理研究记录", HasDeadline = false,
            Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Daily, StartDate = new DateTime(2026, 11, 2) }, ProcessedThrough = new DateTime(2026, 11, 2) });
        state.Items.Add(new TodoItem { Id = "occurrence", Text = "整理研究记录", HasDeadline = false, CreatedAt = new DateTime(2026, 11, 2),
            RecurringRuleId = "rule-one", OccurrenceDate = new DateTime(2026, 11, 2), RepeatLabel = "每日" });
        return state;
    }

    private static void RowActionsAndHistory()
    {
        TodoState state = Fixture(); int edited = 0, paused = 0, stopped = 0, deleted = 0;
        var view = new TodoPanelView(new TodoViewActions { EditRecurringRule = delegate { edited++; },
            SetRecurringPaused = delegate(TodoItem item, bool target) { if (target) paused++; },
            StopRecurringRule = delegate { stopped++; }, Delete = delegate { deleted++; } });
        view.Render(state, new DateTime(2026, 11, 2), false, null);
        Grid row = view.List.Children.OfType<Grid>().Single(grid => Object.Equals(grid.Tag, "occurrence"));
        Assert(All(row).OfType<TextBlock>().Any(text => text.Text == "未设截止 · ↻ 每日"), "Repeat row lost its compact label.");
        Assert(!All(row).OfType<Grid>().Any(grid => Object.Equals(grid.Tag, "time-progress")), "Undated repeated row displayed a time bar.");
        foreach (string caption in new[] { "修改后续重复规则", "暂停后续生成", "停止后续重复…", "删除本次事项…" })
            row.ContextMenu.Items.OfType<MenuItem>().Single(item => Convert.ToString(item.Header) == caption).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert(edited == 1 && paused == 1 && stopped == 1 && deleted == 1, "Repeat-specific commands were not routed independently.");
        state.RecurringRules[0].IsPaused = true; view.Render(state, DateTime.Now, false, null);
        row = view.List.Children.OfType<Grid>().Single(grid => Object.Equals(grid.Tag, "occurrence"));
        Assert(row.ContextMenu.Items.OfType<MenuItem>().Any(item => Convert.ToString(item.Header) == "恢复后续生成"), "Paused rule has no resume entry.");
        state.RecurringRules[0].IsStopped = true; state.Items[0].IsCompleted = true; view.Render(state, DateTime.Now, false, null);
        row = view.List.Children.OfType<Grid>().Single(grid => Object.Equals(grid.Tag, "occurrence"));
        Assert(!row.ContextMenu.Items.OfType<MenuItem>().Any(item => Convert.ToString(item.Header) == "修改后续重复规则"), "Stopped history should not offer future-rule editing.");
        Assert(All(row).OfType<TextBlock>().Any(text => text.Text == "整理研究记录" && text.TextDecorations == TextDecorations.Strikethrough), "Completed repeat history lost its strike-through.");
    }

    private static void ManagementCommandsAndFailure()
    {
        TodoState state = Fixture(); int edits = 0, catchUps = 0; bool accept = false;
        var manager = new RecurringTasksWindow(delegate { return TodoStateCopy.Copy(state); }, delegate { edits++; },
            delegate(string id, bool target) { if (!accept) return false; state.RecurringRules[0].IsPaused = target; return true; },
            delegate { if (!accept) return false; state.RecurringRules[0].IsStopped = true; return true; },
            delegate { catchUps++; }, delegate { return new DateTime(2026, 11, 2); }, delegate { return new RecurrenceSummary { MissedCount = 3 }; });
        try { using (var host = new Host(manager)) {
            Assert(host.All<TextBlock>().Any(text => text.Text.Contains("遗漏周期待处理")), "Management did not surface catch-up work.");
            Click(host.Find<Button>("编辑后续规则：整理研究记录")); Assert(edits == 1, "Management edit did not target its rule.");
            Click(host.Find<Button>("切换重复规则：整理研究记录"));
            Assert(!state.RecurringRules[0].IsPaused && host.Find<TextBlock>("重复规则操作提示").Text.Contains("遗漏"), "Failed management save looked committed or lost recovery guidance.");
            accept = true; Click(host.Find<Button>("切换重复规则：整理研究记录"));
            Assert(state.RecurringRules[0].IsPaused && host.All<TextBlock>().Any(text => text.Text == "已暂停"), "Management pause did not refresh its projection.");
            Click(host.Find<Button>("切换重复规则：整理研究记录")); Assert(!state.RecurringRules[0].IsPaused, "Paused rule could not resume.");
            Click(host.Find<Button>("处理遗漏周期")); Assert(catchUps == 1, "Management catch-up entry was not routed.");
            Click(host.Find<Button>("停止后续重复：整理研究记录"));
            Assert(state.Items.Count == 1 && state.RecurringRules[0].IsStopped && host.All<TextBlock>().Any(text => text.Text.Contains("勾选“重复任务”")), "Stopping removed existing items or left a live rule row.");
        } } finally { manager.Close(); }
    }

    private static void CatchUpChoices()
    {
        int calls = 0; bool? chosen = null;
        var dialog = new RecurrenceCatchUpWindow(8, 2, delegate(bool include) { calls++; chosen = include; return false; });
        try { using (var host = new Host(dialog)) {
            Click(host.Find<Button>("补建遗漏周期")); Assert(calls == 1 && chosen == true, "Catch-up did not send explicit include intent.");
            Assert(host.All<TextBlock>().Any(text => text.Text.Contains("保存失败")), "Failed catch-up discarded the decision window.");
            Click(host.Find<Button>("跳过遗漏周期")); Assert(calls == 2 && chosen == false, "Skip did not remain a separate explicit choice.");
            Assert(host.All<TextBlock>().Any(text => text.Text.Contains("366")), "Large catch-up batches were not explained.");
        } } finally { dialog.Close(); }
    }

    private static void MainWindowSchedulingGuards()
    {
        DateTime now = new DateTime(2026, 11, 2, 10, 0, 0);
        TodoState state = Fixture(); state.Items.Clear(); state.RecurringRules[0].ProcessedThrough = now.Date.AddDays(-1);
        string path = Path.Combine(Path.GetTempPath(), "CornerTodo-recurring-ui-" + Guid.NewGuid().ToString("N"));
        var store = new TodoStore(path);
        var session = new TodoSession(store, state, true, delegate { return now; });
        var interaction = new ApplicationInteraction();
        var window = new MainWindow(store, session, true, true, new UnavailableStartupManager(null), new FakePanelHost(), interaction);
        try {
            window.Interaction.Initialize(state.Settings);
            interaction.EditorOpen = true;
            Assert(window.ProcessRecurrences(false) == 0 && session.Snapshot().Items.Count == 0, "An open editor did not defer occurrence generation.");
            interaction.EditorOpen = false;
            using (interaction.EnterModal()) Assert(window.ProcessRecurrences(false) == 0, "A modal operation did not defer occurrence generation.");
            FieldInfo menu = typeof(MainWindow).GetField("menuOpen", BindingFlags.Instance | BindingFlags.NonPublic);
            menu.SetValue(window, true); Assert(window.ProcessRecurrences(false) == 0, "An open menu did not defer occurrence generation."); menu.SetValue(window, false);
            Assert(window.ProcessRecurrences(false) == 1 && window.CurrentState.Items.Count == 1, "Main-window scheduling did not publish the generated task.");
            Assert(window.ProcessRecurrences(false) == 0 && window.CurrentState.Items.Count == 1, "Repeated UI ticks duplicated an occurrence.");
            typeof(MainWindow).GetMethod("QueueRecurrenceCheck", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            var operation = (DispatcherOperation)typeof(MainWindow).GetField("recurrenceOperation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            Assert(operation != null && operation.Status == DispatcherOperationStatus.Pending, "Startup recurrence work was not queued for an idle boundary.");
            window.Dispose(); now = now.AddDays(1);
            Assert(operation.Status == DispatcherOperationStatus.Aborted && window.ProcessRecurrences(false) == 0, "Disposed windows retained scheduling callbacks.");
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero && !Directory.Exists(path), "Scheduling wiring created native windows or production data.");
        }
        finally { window.Dispose(); window.Close(); }
    }

    private static void RuleDraftRecoveryAndIoFailure()
    {
        DateTime now = new DateTime(2026, 11, 2, 10, 0, 0);
        TodoState state = Fixture(); state.Items.Clear(); state.RecurringRules[0].ProcessedThrough = now.Date.AddDays(-1);
        string path = Path.Combine(Path.GetTempPath(), "CornerTodo-rule-recovery-" + Guid.NewGuid().ToString("N"));
        var store = new TodoStore(path); store.Save(state);
        var session = new TodoSession(store, state, false, delegate { return now; });
        var window = new MainWindow(store, session, true, false, new UnavailableStartupManager(null), new FakePanelHost(), new ApplicationInteraction());
        TaskEditor editor = null; string failure = null; bool recoverAccepted = false;
        editor = new TaskEditor(new TodoItem { Text = "整理研究记录", HasDeadline = false }, false, delegate(TaskDraft draft) {
            editor.RecoverRecurrenceHistory = null;
            bool saved = window.TrySaveRecurringRule("rule-one", draft, out failure);
            if (!saved && session.GetRecurrenceSummary("rule-one").MissedCount > 0) editor.RecoverRecurrenceHistory = delegate {
                if (!recoverAccepted) return false;
                session.ResolveMissedRecurrences(false);
                return session.GetRecurrenceSummary("rule-one").MissedCount == 0;
            };
            return saved;
        }, RecurringTasks.Copy(state.RecurringRules[0].Recurrence), true);
        editor.SaveErrorProvider = delegate { return failure; };
        try { using (var host = new Host(editor)) {
            Assert(session.GetRecurrenceSummary("rule-one").MissedCount == 0, "An editable current rule was misclassified as missed.");
            string longText = "仅修改未来：" + new string('研', 200); string longNotes = new string('注', 1800);
            host.Find<TextBox>("待办事项").Text = longText; host.Find<TextBox>("备注，选填").Text = longNotes;
            // A long edit crossing midnight must keep its input and expose recovery in-place.
            now = now.AddDays(2); Click(host.Find<Button>("保存待办"));
            Assert(editor.Result == null && failure != null && host.Find<TextBlock>("待办编辑错误提示").Text == failure && failure.Contains("遗漏"), "Core save failure was hidden behind the active editor.");
            Button recover = host.Find<Button>("保留草稿处理遗漏周期");
            Assert(recover.Visibility == Visibility.Visible, "Cross-midnight missed periods offered no in-place recovery.");
            Click(recover);
            Assert(editor.Result == null && host.Find<TextBox>("待办事项").Text == longText && host.Find<TextBox>("备注，选填").Text == longNotes,
                "Cancelling missed-period handling lost the existing draft.");
            recoverAccepted = true; Click(recover);
            Assert(recover.Visibility == Visibility.Collapsed && host.Find<TextBox>("备注，选填").Text == longNotes, "Resolving history did not retain the edit draft.");
            // Exercise a real locked atomic-store replacement, not a fake exception string.
            string before = File.ReadAllText(store.DataFilePath);
            using (var locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Click(host.Find<Button>("保存待办"));
                Assert(editor.Result == null && failure != null && host.Find<TextBlock>("待办编辑错误提示").Text == failure,
                    "Real IO failure detail was not displayed inside the editor.");
                Assert(recover.Visibility == Visibility.Collapsed && session.GetRecurringRule("rule-one").Text == "整理研究记录", "IO failure changed the rule or incorrectly offered history recovery.");
            }
            Assert(File.ReadAllText(store.DataFilePath) == before, "A locked failed save replaced the committed data.");
            SaveUnshown(host.Find<Button>("保存待办"), editor);
            Assert(editor.Result != null && session.GetRecurringRule("rule-one").Text == longText && session.GetRecurringRule("rule-one").Notes == longNotes,
                "Retry did not commit the retained long draft after history and IO recovery.");
        } }
        finally { editor.Close(); window.Dispose(); window.Close(); }
    }

    private static void DynamicEditorWorkAreaBounds()
    {
        foreach (int screenHeight in new[] { 720, 500 }) foreach (bool existingRule in new[] { false, true }) {
            var editor = new TaskEditor(new TodoItem { Text = "动态展开布局", HasDeadline = false }, false, delegate { return false; },
                existingRule ? new RecurrenceOptions { Frequency = RecurrenceFrequency.Daily, StartDate = new DateTime(2026, 11, 2) } : null, true);
            try {
                Rect area = new Rect(0, 0, 1280, screenHeight);
                editor.Top = screenHeight == 720 ? 81 : 60; editor.MaxHeight = screenHeight - 24;
                if (screenHeight == 500) editor.Width = 320;
                MainWindow.LimitChildHeightAtPosition(editor, area);
                using (var host = new Host(editor)) {
                    foreach (bool repeating in new[] { false, true }) foreach (int frequency in new[] { 0, 1, 2 })
                    foreach (bool deadline in new[] { false, true }) foreach (bool notes in new[] { false, true }) {
                        host.Find<CheckBox>("重复任务").IsChecked = repeating;
                        host.Find<ComboBox>("重复方式").SelectedIndex = frequency;
                        host.Find<CheckBox>("设置截止日期").IsChecked = deadline;
                        host.Find<Expander>("展开或收起备注").IsExpanded = notes; host.Layout();
                        Assert(editor.Top + editor.Height <= area.Bottom - 11, "Expanding editor settings placed the window below its work area.");
                        Assert(host.Fits(host.Find<Button>("保存待办")), "Dynamic settings moved Save outside the window.");
                    }
                    host.Find<CheckBox>("重复任务").IsChecked = false;
                    host.Find<CheckBox>("设置截止日期").IsChecked = false;
                    editor.SaveErrorProvider = delegate { return "保存失败：" + new string('错', 2000); };
                    Click(host.Find<Button>("保存待办")); host.Layout();
                    var errorScroll = host.Find<ScrollViewer>("待办保存错误详情");
                    Assert(errorScroll.ActualHeight <= 120 && errorScroll.ScrollableHeight > 0, "Long error detail did not remain bounded and scrollable.");
                    Assert(host.Fits(host.Find<Button>("保存待办")), "Long error detail pushed Save offscreen.");
                }
            }
            finally { editor.Close(); }
        }
    }

    private static void SaveUnshown(Button button, TaskEditor editor)
    {
        try { Click(button); }
        catch (InvalidOperationException error) {
            if (editor.Result == null || error.StackTrace == null || error.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) < 0) throw;
        }
    }

    private sealed class FakePanelHost : IPanelHost
    {
        private Rect bounds = new Rect(1000, 20, 430, 475);
        public Rect GetBounds() { return bounds; }
        public void SetBounds(Rect value) { bounds = value; }
        public Rect WorkArea() { return new Rect(0, 0, 1600, 1000); }
    }

    private sealed class Host : IDisposable
    {
        private readonly Window window;
        private readonly FrameworkElement content;
        private readonly Grid canvas;
        internal Host(Window window)
        {
            this.window = window; content = (FrameworkElement)window.Content; window.Content = null;
            TextElement.SetFontFamily(content, window.FontFamily); TextElement.SetFontSize(content, window.FontSize);
            canvas = new Grid { Width = window.Width, Height = window.Height, Background = window.Background };
            canvas.Children.Add(content); Layout();
        }
        internal void Layout()
        {
            canvas.Width = window.Width; canvas.Height = window.Height;
            canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); canvas.UpdateLayout();
        }
        internal IEnumerable<T> All<T>() where T : DependencyObject { return RecurringUiTests.All(content).OfType<T>(); }
        internal bool Fits(FrameworkElement element)
        {
            Rect bounds = element.TransformToAncestor(canvas).TransformBounds(new Rect(element.RenderSize));
            return bounds.Height > 0 && bounds.Width > 0 && bounds.Top >= 0 && bounds.Left >= 0 && bounds.Bottom <= canvas.ActualHeight + 1 && bounds.Right <= canvas.ActualWidth + 1;
        }
        internal T Find<T>(string name) where T : DependencyObject
        {
            T result = All<T>().SingleOrDefault(value => AutomationProperties.GetName(value) == name);
            Assert(result != null, "Missing recurrence control: " + name); return result;
        }
        public void Dispose() { canvas.Children.Clear(); window.Content = content; }
    }
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        return Walk(root, new HashSet<DependencyObject>());
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root, HashSet<DependencyObject> seen)
    {
        if (!seen.Add(root)) yield break;
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root)) {
            var value = child as DependencyObject; if (value != null) foreach (var nested in Walk(value, seen)) yield return nested;
        }
        if (root is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var nested in Walk(VisualTreeHelper.GetChild(root, i), seen)) yield return nested;
    }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
    private static void Assert(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
}
