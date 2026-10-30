using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CornerTodo;

internal static class TaskEditorTests
{
    private static int checks;

    public static void Run()
    {
        checks = 0;
        DefaultsAndEntryPoints();
        TimePickerSelectionAndTyping();
        SaveDraftAndPreserveInput();
        ValidationAndClearing();
        PersistenceFailureAndRetry();
        CancelAndSmallLayout();
        Console.WriteLine("PASS: task editor time-picker/notes/priority behaviors, " + checks + " checks (unshown WPF windows).");
    }

    private static TodoItem Fixture()
    {
        return new TodoItem {
            Id = "editor-test", Text = "一楼珠宝区空调机组", Notes = "检查机组运行情况\r\n联系维修人员确认处理时间",
            Priority = TaskPriority.High, DueDate = new DateTime(2026, 11, 2, 10, 20, 31, DateTimeKind.Utc).AddTicks(1250),
            HasTime = true, CreatedAt = new DateTime(2026, 10, 20), DeadlineSetAt = new DateTime(2026, 10, 21)
        };
    }

    private static void DefaultsAndEntryPoints()
    {
        WithEditor(null, false, delegate(TaskEditor editor, LayoutHost host) {
            Assert(!host.Find<Expander>("展开或收起备注").IsExpanded, "New task notes should start collapsed.");
            Assert(host.Find<RadioButton>("优先级：中").IsChecked == true, "New task priority should be normal.");
            Assert(host.Find<TextBox>("备注，选填").Text.Length == 0, "New task notes should be empty.");
            Assert(FocusManager.GetFocusedElement(editor) == host.Find<TextBox>("待办事项"), "New task entry should focus its task text.");
            Assert(host.All<RadioButton>().Select(button => Convert.ToString(button.Content)).SequenceEqual(new[] { "高", "中", "低" }),
                "Priority options must be short Chinese labels without P codes.");
            Assert(host.Find<Button>("取消编辑待办").IsCancel, "Escape should remain available through the cancel button.");
            KeyBinding shortcut = editor.InputBindings.OfType<KeyBinding>().Single(binding => binding.Command == ApplicationCommands.Save);
            Assert(shortcut.Key == Key.Enter && shortcut.Modifiers == ModifierKeys.Control, "Ctrl+Enter save gesture was lost.");
            ComboBox hour = host.Find<ComboBox>("截止小时，24小时制"); ComboBox minute = host.Find<ComboBox>("截止分钟");
            Assert(!hour.IsEnabled && !minute.IsEnabled, "An untimed task should not enable either time field.");
            Assert(hour.Text == "18" && minute.Text == "00", "New task time should retain the existing 18:00 default.");
            Assert(hour.IsEditable && minute.IsEditable, "Both time fields must support keyboard entry as well as point selection.");
            Assert(hour.Items.Count == 24 && hour.Items[0].Equals("00") && hour.Items[23].Equals("23"), "Hour choices must cover the full 24-hour clock.");
            Assert(minute.Items.Count == 60 && minute.Items[0].Equals("00") && minute.Items[23].Equals("23") && minute.Items[59].Equals("59"),
                "Minute choices must include arbitrary minutes, rather than only five-minute intervals.");
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            Assert(hour.IsEnabled && minute.IsEnabled, "Checking specific time did not enable both fields.");
            host.Find<CheckBox>("设置具体时刻").IsChecked = false;
            Assert(!hour.IsEnabled && !minute.IsEnabled, "Clearing specific time did not disable both fields.");
            host.Find<Expander>("展开或收起备注").IsExpanded = true;
            host.Layout();
            Assert(host.Find<TextBox>("备注，选填").ActualHeight > 0, "Expanding notes should expose its text box.");
        });
        TodoItem item = Fixture();
        WithEditor(item, false, delegate(TaskEditor editor, LayoutHost host) {
            Assert(host.Find<Expander>("展开或收起备注").IsExpanded, "Existing notes should expand automatically.");
            Assert(host.Find<TextBox>("备注，选填").Text == item.Notes, "Existing multiline notes were changed while loading.");
            Assert(host.Find<RadioButton>("优先级：高").IsChecked == true, "Existing priority was not restored.");
            DateTime local = item.DueDate.ToLocalTime();
            Assert(host.Find<ComboBox>("截止小时，24小时制").Text == local.ToString("HH") && host.Find<ComboBox>("截止分钟").Text == local.ToString("mm"),
                "Time selectors did not restore the existing local deadline.");
        });
        item.Notes = "";
        WithEditor(item, true, delegate(TaskEditor editor, LayoutHost host) {
            Assert(host.Find<Expander>("展开或收起备注").IsExpanded, "The notes entry point should expand even empty notes.");
            Assert(FocusManager.GetFocusedElement(editor) == host.Find<TextBox>("备注，选填"), "Notes entry point lost its initial logical focus.");
        });
    }

    private static void TimePickerSelectionAndTyping()
    {
        foreach (int[] parts in new[] { new[] { 0, 0 }, new[] { 23, 59 }, new[] { 17, 23 } })
        {
            WithEditor(null, false, delegate(TaskEditor editor, LayoutHost host) {
                host.Find<TextBox>("待办事项").Text = "点选截止时刻";
                host.Find<DatePicker>("截止日期").SelectedDate = new DateTime(2026, 11, 3);
                host.Find<CheckBox>("设置具体时刻").IsChecked = true;
                host.Find<ComboBox>("截止小时，24小时制").SelectedItem = parts[0].ToString("00", CultureInfo.InvariantCulture);
                host.Find<ComboBox>("截止分钟").SelectedItem = parts[1].ToString("00", CultureInfo.InvariantCulture);
                host.Layout(); Click(host.Find<Button>("保存待办"), true);
                Assert(editor.Result != null && editor.Result.HasTime && editor.Result.DueDate == new DateTime(2026, 11, 3, parts[0], parts[1], 0),
                    "Selecting hour/minute did not save the intended exact minute.");
            });
        }
        WithEditor(null, false, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<TextBox>("待办事项").Text = "键盘填写时刻";
            host.Find<DatePicker>("截止日期").SelectedDate = new DateTime(2026, 11, 3);
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            ComboBox hour = host.Find<ComboBox>("截止小时，24小时制"); ComboBox minute = host.Find<ComboBox>("截止分钟");
            hour.Text = "7"; minute.Text = "3";
            Click(host.Find<Button>("保存待办"), true);
            Assert(editor.Result != null && editor.Result.DueDate == new DateTime(2026, 11, 3, 7, 3, 0), "Keyboard entry did not save the intended hour/minute.");
            Assert(hour.Text == "07" && minute.Text == "03", "Valid keyboard input should normalize to two-digit fields.");
        });
        int persistenceCalls = 0;
        WithEditor(null, false, delegate(TaskDraft draft) { persistenceCalls++; return true; }, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<TextBox>("待办事项").Text = "无效时刻不应保存";
            host.Find<TextBox>("备注，选填").Text = "输入必须保留";
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            ComboBox hour = host.Find<ComboBox>("截止小时，24小时制"); ComboBox minute = host.Find<ComboBox>("截止分钟");
            foreach (string[] parts in new[] {
                new[] { "24", "00" }, new[] { "-1", "00" }, new[] { "+1", "00" }, new[] { "1.5", "00" },
                new[] { "", "00" }, new[] { "1700", "00" }, new[] { "17:00", "00" },
                new[] { "17", "60" }, new[] { "17", "-1" }, new[] { "17", "1.5" }, new[] { "17", "" }
            })
            {
                hour.Text = parts[0]; minute.Text = parts[1];
                Click(host.Find<Button>("保存待办"), false);
                Assert(editor.Result == null && persistenceCalls == 0 && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("时刻"),
                    "Invalid hour/minute input reached persistence or lacked a visible error: " + parts[0] + ":" + parts[1]);
                Assert(hour.Text == parts[0] && minute.Text == parts[1] && host.Find<TextBox>("备注，选填").Text == "输入必须保留",
                    "Validation changed the user's time fields or notes.");
            }
        });
        TodoItem original = Fixture();
        WithEditor(original, false, delegate(TaskEditor editor, LayoutHost host) {
            DateTime local = original.DueDate.ToLocalTime();
            host.Find<ComboBox>("截止小时，24小时制").SelectedIndex = local.Hour;
            host.Find<ComboBox>("截止分钟").SelectedIndex = local.Minute;
            Click(host.Find<Button>("保存待办"), true);
            Assert(editor.Result != null && editor.Result.DueDate == original.DueDate && editor.Result.DueDate.Kind == original.DueDate.Kind,
                "Re-selecting the same clock time lost stored seconds, ticks or UTC kind.");
        });
        WithEditor(original, false, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<ComboBox>("截止小时，24小时制").Text = "invalid";
            host.Find<CheckBox>("设置具体时刻").IsChecked = false;
            Click(host.Find<Button>("保存待办"), true);
            Assert(editor.Result != null && !editor.Result.HasTime && editor.Result.DueDate == original.DueDate.ToLocalTime().Date,
                "Clearing specific time should ignore disabled fields and save the local date only.");
        });
        WithEditor(null, false, delegate(TaskEditor editor, LayoutHost host) {
            host.Width = 320; host.Height = 300; host.Layout();
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            ComboBox hour = host.Find<ComboBox>("截止小时，24小时制"); ComboBox minute = host.Find<ComboBox>("截止分钟");
            hour.BringIntoView(); host.Layout();
            ScrollViewer body = host.Find<ScrollViewer>("待办编辑内容");
            Assert(Fits(hour, body) && Fits(minute, body), "Small editors must keep both clock fields visible together after scrolling to their row.");
            Assert(Fits(host.Find<Button>("保存待办"), host.Canvas), "Scrolling to the clock fields displaced the fixed save action.");
        });
    }

    private static void SaveDraftAndPreserveInput()
    {
        foreach (TaskPriority priority in new[] { TaskPriority.High, TaskPriority.Normal, TaskPriority.Low })
        {
            TodoItem item = Fixture(); string before = ItemValue(item);
            WithEditor(item, false, delegate(TaskEditor editor, LayoutHost host) {
                host.Find<TextBox>("待办事项").Text = "  修改后的事项  ";
                host.Find<TextBox>("备注，选填").Text = " 新的备注 📝\r\n第二行：说明与链接 https://example.invalid ";
                host.Find<RadioButton>("优先级：" + TodoRules.PriorityLabel(priority)).IsChecked = true;
                Click(host.Find<Button>("保存待办"), true);
                Assert(editor.Result != null && editor.Result.Text == "修改后的事项", "Saving did not return the edited title.");
                Assert(editor.Result.Notes == host.Find<TextBox>("备注，选填").Text, "Saving stripped or changed multiline notes.");
                Assert(editor.Result.Priority == priority, "Saving did not return the selected priority.");
                Assert(editor.Result.DueDate == item.DueDate && editor.Result.DueDate.Kind == DateTimeKind.Utc,
                    "Unchanged displayed deadline lost UTC kind, seconds or ticks.");
                Assert(ItemValue(item) == before, "Editor mutated its original item instead of returning a draft.");
            });
        }
        WithEditor(null, false, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<TextBox>("待办事项").Text = "快捷键命令保存";
            host.Find<DatePicker>("截止日期").Text = "2026年11月03日";
            host.Find<Expander>("展开或收起备注").IsExpanded = true;
            host.Find<TextBox>("备注，选填").Text = "可选说明";
            InvokeDialogResult(delegate { ApplicationCommands.Save.Execute(null, editor); });
            Assert(editor.Result != null && editor.Result.Notes == "可选说明", "Save command used by Ctrl+Enter did not submit the draft.");
            Assert(!editor.Result.HasTime && editor.Result.DueDate == new DateTime(2026, 11, 3), "Date-only save changed its day or introduced a time.");
        });
    }

    private static void ValidationAndClearing()
    {
        WithEditor(null, false, delegate(TaskEditor editor, LayoutHost host) {
            Click(host.Find<Button>("保存待办"), false);
            Assert(editor.Result == null && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("事项"), "Empty title should not produce a draft.");
            host.Find<TextBox>("待办事项").Text = "测试事项";
            host.Find<DatePicker>("截止日期").Text = "";
            Click(host.Find<Button>("保存待办"), false);
            Assert(editor.Result == null && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("日期"), "Empty date should not produce a draft.");
            host.Find<DatePicker>("截止日期").Text = "2026年11月03日";
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            host.Find<ComboBox>("截止小时，24小时制").Text = "24";
            host.Find<ComboBox>("截止分钟").Text = "00";
            Click(host.Find<Button>("保存待办"), false);
            Assert(editor.Result == null && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("时刻"), "Invalid time should not produce a draft.");
            host.Find<ComboBox>("截止小时，24小时制").Text = "09";
            host.Find<ComboBox>("截止分钟").Text = "30";
            Click(host.Find<Button>("保存待办"), true);
            Assert(editor.Result != null && editor.Result.HasTime && editor.Result.DueDate == new DateTime(2026, 11, 3, 9, 30, 0),
                "Correcting validation errors should allow saving the intended deadline.");
        });
        TodoItem excessive = Fixture(); excessive.Notes = new string('长', TodoRules.MaxNotesLength + 1);
        WithEditor(excessive, false, delegate(TaskEditor editor, LayoutHost host) {
            TextBox input = host.Find<TextBox>("备注，选填");
            Assert(input.Text == excessive.Notes, "Opening an oversized stored note must not silently truncate it.");
            Click(host.Find<Button>("保存待办"), false);
            Assert(editor.Result == null && host.Find<TextBlock>("待办编辑错误提示").Text.Contains(TodoRules.MaxNotesLength.ToString(CultureInfo.InvariantCulture)),
                "Oversized notes should have a visible validation message and remain editable.");
            input.Text = new string('字', TodoRules.MaxNotesLength);
            Click(host.Find<Button>("保存待办"), true);
            Assert(editor.Result != null && editor.Result.Notes.Length == TodoRules.MaxNotesLength, "The allowed notes boundary should save exactly.");
        });
        WithEditor(Fixture(), false, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<TextBox>("备注，选填").Clear();
            Click(host.Find<Button>("保存待办"), true);
            Assert(editor.Result != null && editor.Result.Notes == "", "Clearing notes should return an empty value for persistence.");
        });
    }

    private static void CancelAndSmallLayout()
    {
        TodoItem item = Fixture(); string before = ItemValue(item);
        WithEditor(item, true, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<TextBox>("备注，选填").Text = "未保存的修改";
            host.Find<RadioButton>("优先级：低").IsChecked = true;
            Click(host.Find<Button>("取消编辑待办"), true);
            Assert(editor.Result == null && ItemValue(item) == before, "Cancel should not submit or mutate changes.");
        });
        item.Text = new string('长', 500); item.Notes = new string('注', TodoRules.MaxNotesLength);
        WithEditor(item, false, delegate(TaskEditor editor, LayoutHost host) {
            host.Width = 320; host.Height = 300; host.Layout();
            Button save = host.Find<Button>("保存待办"); Button cancel = host.Find<Button>("取消编辑待办");
            Assert(Fits(save, host.Canvas) && Fits(cancel, host.Canvas), "Small available work area clipped save/cancel actions.");
            ScrollViewer body = host.Find<ScrollViewer>("待办编辑内容");
            Assert(body.ScrollableHeight > 0 && body.ViewportHeight > 0, "Small editor should retain a usable scrollable body.");
            Assert(!Ancestors(save).Contains(body) && !Ancestors(cancel).Contains(body), "Save/cancel must remain outside the scrolling body.");
            body.ScrollToBottom(); host.Layout();
            Assert(body.VerticalOffset > 0 && Fits(save, host.Canvas), "Scrolling long content displaced the fixed save action.");
        });
    }

    private static void PersistenceFailureAndRetry()
    {
        TodoItem item = Fixture(); string before = ItemValue(item);
        int calls = 0; var submitted = new List<TaskDraft>();
        WithEditor(item, false, delegate(TaskDraft draft) { calls++; submitted.Add(draft); return calls > 1; },
            delegate(TaskEditor editor, LayoutHost host) {
                TextBox title = host.Find<TextBox>("待办事项"); TextBox notes = host.Find<TextBox>("备注，选填");
                title.Text = ""; Click(host.Find<Button>("保存待办"), false);
                Assert(calls == 0, "Invalid editor data must not reach the persistence callback.");
                title.Text = "失败后保留的事项"; notes.Text = "第一行\r\n需要保留的详细说明";
                host.Find<RadioButton>("优先级：低").IsChecked = true;
                host.Find<ComboBox>("截止小时，24小时制").Text = "17";
                host.Find<ComboBox>("截止分钟").Text = "23";
                Click(host.Find<Button>("保存待办"), false);
                Assert(calls == 1 && editor.Result == null, "A failed save must not close or publish the draft.");
                Assert(title.Text == "失败后保留的事项" && notes.Text == "第一行\r\n需要保留的详细说明" && host.Find<RadioButton>("优先级：低").IsChecked == true,
                    "A failed save discarded editable title, notes or priority.");
                Assert(host.Find<TextBlock>("待办编辑错误提示").Text.Contains("输入仍保留"), "A failed save should explain that input is retained.");
                Assert(host.Find<ComboBox>("截止小时，24小时制").Text == "17" && host.Find<ComboBox>("截止分钟").Text == "23",
                    "A failed save discarded the selected clock time.");
                Assert(ItemValue(item) == before, "A failed persistence attempt mutated the supplied task.");
                Click(host.Find<Button>("保存待办"), true);
                Assert(calls == 2 && editor.Result != null, "Retry should invoke persistence exactly once and publish on success.");
                Assert(submitted[0].Notes == submitted[1].Notes && submitted[0].DueDate == submitted[1].DueDate && submitted[1].Priority == TaskPriority.Low,
                    "Retry did not retain the submitted notes, deadline and priority.");
                Assert(ItemValue(item) == before, "Successful draft submission mutated the editor's original task.");
            });
        calls = 0;
        WithEditor(item, false, delegate(TaskDraft draft) { calls++; return false; }, delegate(TaskEditor editor, LayoutHost host) {
            host.Find<TextBox>("备注，选填").Text = "失败后取消";
            Click(host.Find<Button>("保存待办"), false);
            Click(host.Find<Button>("取消编辑待办"), true);
            Assert(calls == 1 && editor.Result == null && ItemValue(item) == before, "Cancel after a failed save should not save again or publish its draft.");
        });
    }

    private static void WithEditor(TodoItem item, bool focusNotes, Action<TaskEditor, LayoutHost> check)
    { WithEditor(item, focusNotes, null, check); }

    private static void WithEditor(TodoItem item, bool focusNotes, Func<TaskDraft, bool> saveDraft, Action<TaskEditor, LayoutHost> check)
    {
        TaskEditor editor = new TaskEditor(item, focusNotes, saveDraft);
        try { using (LayoutHost host = new LayoutHost(editor)) { check(editor, host); } }
        finally { Assert(new WindowInteropHelper(editor).Handle == IntPtr.Zero, "Editor tests must not create a native window."); editor.Close(); }
    }

    private sealed class LayoutHost : IDisposable
    {
        private readonly TaskEditor editor;
        private readonly FrameworkElement content;
        internal readonly Grid Canvas;
        internal double Width, Height;
        internal LayoutHost(TaskEditor window)
        {
            editor = window; Width = window.Width; Height = window.Height; content = (FrameworkElement)window.Content;
            TextElement.SetFontFamily(content, window.FontFamily); TextElement.SetFontSize(content, window.FontSize);
            TextElement.SetForeground(content, window.Foreground); window.Content = null;
            Canvas = new Grid { Background = window.Background }; Canvas.Children.Add(content); Layout();
        }
        internal void Layout()
        {
            Canvas.Width = Width; Canvas.Height = Height;
            Canvas.Measure(new Size(Width, Height)); Canvas.Arrange(new Rect(0, 0, Width, Height)); Canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); Canvas.UpdateLayout();
        }
        internal T Find<T>(string name) where T : DependencyObject
        {
            T found = All<T>().SingleOrDefault(value => AutomationProperties.GetName(value) == name);
            Assert(found != null, "Missing editor control: " + name); return found;
        }
        internal IEnumerable<T> All<T>() where T : DependencyObject { return Descendants(content, new HashSet<DependencyObject>()).OfType<T>(); }
        public void Dispose() { Canvas.Children.Clear(); editor.Content = content; }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(parent)) yield break;
        yield return parent;
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            DependencyObject value = child as DependencyObject;
            if (value != null) foreach (DependencyObject nested in Descendants(value, visited)) yield return nested;
        }
        if (parent is Visual)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (DependencyObject nested in Descendants(VisualTreeHelper.GetChild(parent, i), visited)) yield return nested;
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        while ((child = VisualTreeHelper.GetParent(child)) != null) yield return child;
    }
    private static bool Fits(FrameworkElement element, FrameworkElement container)
    {
        Rect bounds = element.TransformToAncestor(container).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= container.ActualWidth + 1 && bounds.Bottom <= container.ActualHeight + 1;
    }
    private static void Click(Button button, bool unshownDialogResult)
    {
        if (unshownDialogResult) InvokeDialogResult(delegate { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); });
        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private static void InvokeDialogResult(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            if (error.StackTrace == null || error.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) < 0) throw;
        }
    }
    private static string ItemValue(TodoItem item)
    {
        return item.Id + "|" + item.Text + "|" + item.Notes + "|" + item.Priority + "|" + item.DueDate.ToString("O") + "|" + item.DueDate.Kind + "|" + item.HasTime + "|" + item.DeadlineSetAt;
    }
    private static void Assert(bool condition, string message)
    {
        checks++; if (!condition) throw new InvalidOperationException(message);
    }
}
