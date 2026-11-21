using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CornerTodo;

internal static class ImportanceEditorTests
{
    private static int checks;

    internal static void Run()
    {
        checks = 0;
        DefaultsAndIndependentPriority();
        SavedDraftFailureRetryAndCancel();
        RecurringRuleAndOccurrenceDrafts();
        CurrentDraftPreview();
        ThresholdAndCompletedTaskPreview();
        SmallLayoutKeepsActionsVisible();
        Console.WriteLine("PASS: " + checks + " importance editor/preview/draft assertions; unshown WPF windows only.");
    }

    private static TodoItem Fixture()
    {
        return new TodoItem { Id = "importance-editor", Text = "完善论文第二章", Notes = "保留原来的备注", IsImportant = true,
            Priority = TaskPriority.Low, HasDeadline = false, CreatedAt = new DateTime(2026, 11, 1) };
    }

    private static void DefaultsAndIndependentPriority()
    {
        WithEditor(null, null, null, true, 24, delegate(TaskEditor editor, Host host) {
            Assert(host.Find<CheckBox>("标为重要").IsChecked == false, "A new task must not opt into importance.");
            Assert(host.Find<RadioButton>("优先级：中").IsChecked == true, "Importance changed the normal priority default.");
            Assert(!host.Find<Expander>("展开或收起备注").IsExpanded, "Importance expanded optional notes by default.");
            host.Find<CheckBox>("设置截止日期").IsChecked = false;
            Assert(Preview(host) == "不重要不紧急", "An unscheduled new task must not be urgent.");
            host.Find<RadioButton>("优先级：高").IsChecked = true;
            Assert(host.Find<CheckBox>("标为重要").IsChecked == false && Preview(host) == "不重要不紧急",
                "Selecting high priority silently marked a task as important.");
            host.Find<CheckBox>("标为重要").IsChecked = true;
            Assert(host.Find<RadioButton>("优先级：高").IsChecked == true && Preview(host) == "重要不紧急",
                "Importance did not refresh the preview independently of priority.");
            host.Find<RadioButton>("优先级：低").IsChecked = true;
            Assert(host.Find<CheckBox>("标为重要").IsChecked == true && Preview(host) == "重要不紧急",
                "Selecting low priority cleared importance or changed classification.");
        });
        foreach (bool value in new[] { false, true }) {
            TodoItem item = Fixture(); item.IsImportant = value;
            WithEditor(item, null, null, true, 24, delegate(TaskEditor editor, Host host) {
                Assert(host.Find<CheckBox>("标为重要").IsChecked == value, "Editing lost the supplied importance flag.");
                Assert(host.Find<RadioButton>("优先级：低").IsChecked == true, "Loading importance changed stored priority.");
                Assert(Preview(host) == (value ? "重要不紧急" : "不重要不紧急"), "Editing an unscheduled task classified it incorrectly.");
            });
        }
    }

    private static void SavedDraftFailureRetryAndCancel()
    {
        TodoItem original = Fixture(); string before = Fingerprint(original);
        int calls = 0; var drafts = new List<TaskDraft>();
        WithEditor(original, delegate(TaskDraft draft) { drafts.Add(draft); return ++calls > 1; }, null, true, 24,
            delegate(TaskEditor editor, Host host) {
                host.Find<TextBox>("待办事项").Text = "失败后保留的事项";
                host.Find<TextBox>("备注，选填").Text = "第一行\r\n未保存的备注";
                host.Find<CheckBox>("标为重要").IsChecked = false;
                host.Find<RadioButton>("优先级：高").IsChecked = true;
                Click(host.Find<Button>("保存待办"));
                Assert(calls == 1 && editor.Result == null && !drafts[0].IsImportant, "A failed save published or lost the unchecked importance draft.");
                Assert(host.Find<CheckBox>("标为重要").IsChecked == false && Preview(host) == "不重要不紧急", "A failed save restored the old importance flag.");
                Assert(host.Find<TextBox>("备注，选填").Text == "第一行\r\n未保存的备注" && Fingerprint(original) == before,
                    "A failed importance save mutated the source or lost notes.");
                Assert(host.Find<TextBlock>("待办编辑错误提示").Text.Contains("输入仍保留"), "A failed save did not explain draft retention.");
                Click(host.Find<Button>("保存待办"));
                Assert(calls == 2 && editor.Result != null && !editor.Result.IsImportant && editor.Result.Priority == TaskPriority.High,
                    "Retry did not save independent priority and importance values.");
                Assert(drafts[1].Text == drafts[0].Text && drafts[1].Notes == drafts[0].Notes && Fingerprint(original) == before,
                    "Retry changed the draft or edited the original item.");
            });

        TaskDraft added = null;
        WithEditor(null, delegate(TaskDraft draft) { added = draft; return true; }, null, true, 24,
            delegate(TaskEditor editor, Host host) {
                host.Find<TextBox>("待办事项").Text = "新增重要事项";
                host.Find<CheckBox>("标为重要").IsChecked = true;
                Click(host.Find<Button>("保存待办"));
                Assert(added != null && added.IsImportant && editor.Result == added, "A new important task was not submitted.");
            });
        calls = 0;
        WithEditor(original, delegate { calls++; return false; }, null, true, 24, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("标为重要").IsChecked = false;
            Click(host.Find<Button>("取消编辑待办"));
            Assert(calls == 0 && editor.Result == null && Fingerprint(original) == before, "Cancel submitted or mutated an importance change.");
        });
        WithEditor(original, delegate { calls++; return false; }, null, true, 24, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("标为重要").IsChecked = false;
            Click(host.Find<Button>("保存待办"));
            Click(host.Find<Button>("取消编辑待办"));
            Assert(calls == 1 && editor.Result == null && Fingerprint(original) == before, "Cancel after failure saved again or mutated importance.");
        });
    }

    private static void RecurringRuleAndOccurrenceDrafts()
    {
        TodoItem template = Fixture(); TaskDraft captured = null;
        var recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Weekly, WeekDays = 2, StartDate = DateTime.Today };
        WithEditor(template, delegate(TaskDraft draft) { captured = draft; return false; }, recurrence, true, 24,
            delegate(TaskEditor editor, Host host) {
                Assert(editor.Title == "修改后续重复规则" && host.Find<CheckBox>("标为重要").IsChecked == true,
                    "Rule editing lost its title or inherited importance.");
                Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.IsImportant && captured.Recurrence != null, "Rule draft dropped importance or recurrence options.");
                host.Find<CheckBox>("标为重要").IsChecked = false; Click(host.Find<Button>("保存待办"));
                Assert(!captured.IsImportant && template.IsImportant, "Clearing rule importance mutated the original template.");
            });
        TodoItem occurrence = Fixture(); occurrence.RecurringRuleId = "rule-importance"; occurrence.OccurrenceDate = DateTime.Today;
        string before = Fingerprint(occurrence); captured = null;
        WithEditor(occurrence, delegate(TaskDraft draft) { captured = draft; return false; }, null, false, 24,
            delegate(TaskEditor editor, Host host) {
                Assert(host.Find<CheckBox>("重复任务").Visibility == Visibility.Collapsed && host.Find<CheckBox>("标为重要").IsChecked == true,
                    "Editing a generated occurrence lost importance or exposed rule editing.");
                Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.IsImportant && captured.Recurrence == null, "Occurrence draft dropped importance or rewrote the repeat rule.");
                host.Find<CheckBox>("标为重要").IsChecked = false; Click(host.Find<Button>("保存待办"));
                Assert(!captured.IsImportant && Fingerprint(occurrence) == before, "An occurrence importance draft modified its source item.");
            });
    }

    private static void CurrentDraftPreview()
    {
        WithEditor(Fixture(), null, null, true, 24, delegate(TaskEditor editor, Host host) {
            Assert(Preview(host) == "重要不紧急", "An unscheduled important task was urgent.");
            DatePicker date = host.Find<DatePicker>("截止日期");
            date.SelectedDate = DateTime.Today.AddDays(10);
            host.Find<CheckBox>("设置截止日期").IsChecked = true;
            Assert(Preview(host) == "重要不紧急", "A distant deadline did not update the draft preview.");
            date.SelectedDate = DateTime.Today.AddDays(-2);
            Assert(Preview(host) == "重要且紧急", "A newly edited overdue date did not become urgent immediately.");
            host.Find<CheckBox>("标为重要").IsChecked = false;
            Assert(Preview(host) == "紧急不重要", "Urgent unimportant classification must use the agreed label.");
            host.Find<CheckBox>("标为重要").IsChecked = true;
            date.SelectedDate = DateTime.Today.AddDays(1);
            Assert(Preview(host) == "重要不紧急", "A date-only deadline became urgent at the start of its day instead of its end.");
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            host.Find<ComboBox>("截止小时，24小时制").SelectedIndex = 0;
            host.Find<ComboBox>("截止分钟").SelectedIndex = 0;
            host.Layout();
            Assert(Preview(host) == "重要且紧急", "Point-selected hour/minute did not update the time-based classification: " + Preview(host)
                + "; " + host.Find<ComboBox>("截止小时，24小时制").Text + ":" + host.Find<ComboBox>("截止分钟").Text);
            host.Find<CheckBox>("设置具体时刻").IsChecked = false;
            Assert(Preview(host) == "重要不紧急", "Clearing the specific time failed to restore end-of-day semantics.");
            date.Text = DateTime.Today.AddDays(-3).ToString("yyyy年M月d日", CultureInfo.GetCultureInfo("zh-CN"));
            Assert(Preview(host) == "重要且紧急", "Typing the deadline did not refresh classification.");
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            host.Find<ComboBox>("截止小时，24小时制").Text = "25";
            Assert(Preview(host).Contains("有效具体时刻"), "Invalid time text displayed a misleading valid quadrant.");
            host.Find<CheckBox>("设置截止日期").IsChecked = false;
            Assert(Preview(host) == "重要不紧急", "An unscheduled draft depended on disabled invalid time input.");
            host.Find<CheckBox>("设置截止日期").IsChecked = true;
            host.Find<ComboBox>("截止小时，24小时制").Text = "17";
            host.Find<ComboBox>("截止分钟").Text = "99";
            Assert(Preview(host).Contains("有效具体时刻"), "Invalid minute text kept a stale quadrant.");
            host.Find<ComboBox>("截止分钟").Text = "23";
            Assert(Preview(host) == "重要且紧急", "Corrected typed time did not restore the quadrant preview.");
            Assert(editor.Result == null, "Previewing fields submitted a task.");
        });
    }

    private static void ThresholdAndCompletedTaskPreview()
    {
        foreach (int threshold in new[] { 6, 24, 72, 168, -1 }) {
            TodoItem item = Fixture(); item.HasDeadline = true; item.HasTime = true; item.DueDate = DateTime.Now.AddHours(48);
            WithEditor(item, null, null, true, threshold, delegate(TaskEditor editor, Host host) {
                Assert(Preview(host) == (threshold == 72 || threshold == 168 ? "重要且紧急" : "重要不紧急"),
                    "Editor preview ignored the supplied urgency threshold or its default normalization.");
            });
        }
        TodoItem completed = Fixture(); completed.HasDeadline = true; completed.HasTime = true;
        completed.DueDate = DateTime.Today.AddDays(-2).AddHours(17);
        completed.IsCompleted = true; completed.CompletedAt = DateTime.Today.AddDays(-10);
        WithEditor(completed, null, null, false, 24, delegate(TaskEditor editor, Host host) {
            Assert(Preview(host) == "重要且紧急", "Completed-task preview classified historical completion instead of current edited values.");
        });
        TodoItem precise = Fixture(); precise.HasDeadline = true; precise.HasTime = true;
        precise.DueDate = DateTime.UtcNow.AddDays(7).AddTicks(1234); DateTime before = precise.DueDate;
        WithEditor(precise, delegate { return true; }, null, true, 24, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("标为重要").IsChecked = false; Click(host.Find<Button>("保存待办"));
            Assert(editor.Result.DueDate == before && editor.Result.DueDate.Kind == DateTimeKind.Utc,
                "Changing importance reset stored deadline precision or UTC kind.");
        });
    }

    private static void SmallLayoutKeepsActionsVisible()
    {
        TodoItem item = Fixture(); item.Notes = new string('注', TodoRules.MaxNotesLength);
        WithEditor(item, delegate { return false; }, null, true, 24, delegate(TaskEditor editor, Host host) {
            editor.Width = 320; editor.MaxHeight = 300; editor.Height = 300; host.Layout();
            host.Find<CheckBox>("重复任务").IsChecked = true;
            host.Find<ComboBox>("重复方式").SelectedIndex = 1;
            host.Find<CheckBox>("设置截止日期").IsChecked = true;
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            host.Find<CheckBox>("提醒我").IsChecked = true;
            host.Layout();
            var body = host.Find<ScrollViewer>("待办编辑内容");
            Assert(body.ViewportHeight > 0 && body.ScrollableHeight > 0, "Expanded options removed the small editor's scrollable body.");
            CheckBox important = host.Find<CheckBox>("标为重要"); important.BringIntoView(); host.Layout();
            Assert(host.Fits(important, body), "The importance checkbox could not be reached on a small screen.");
            host.Find<TextBlock>("四象限归类预览").BringIntoView(); host.Layout();
            Assert(host.Fits(host.Find<TextBlock>("四象限归类预览"), body), "The classification preview could not be reached on a small screen.");
            Assert(host.Fits(host.Find<Button>("保存待办"), host.Canvas) && host.Fits(host.Find<Button>("取消编辑待办"), host.Canvas),
                "Importance or recurrence moved Save/Cancel outside the small editor.");
            host.Find<CheckBox>("标为重要").IsChecked = false;
            host.Find<CheckBox>("设置截止日期").IsChecked = false;
            host.Find<CheckBox>("重复任务").IsChecked = false;
            editor.SaveErrorProvider = delegate { return "保存失败：" + new string('错', 2000); };
            Click(host.Find<Button>("保存待办")); host.Layout();
            Assert(host.Find<CheckBox>("标为重要").IsChecked == false && host.Fits(host.Find<Button>("保存待办"), host.Canvas),
                "A long save failure discarded importance or clipped the fixed Save action.");
        });
    }

    private static string Preview(Host host) { return host.Find<TextBlock>("四象限归类预览").Text; }
    private static string Fingerprint(TodoItem item)
    {
        return item.Id + "|" + item.Text + "|" + item.Notes + "|" + item.IsImportant + "|" + item.Priority + "|" + item.HasDeadline
            + "|" + item.DueDate.ToString("O") + "|" + item.HasTime + "|" + item.IsCompleted + "|" + item.CompletedAt + "|" + item.RecurringRuleId;
    }
    private static void WithEditor(TodoItem item, Func<TaskDraft, bool> save, RecurrenceOptions recurrence, bool allowRecurrence,
        int urgentHours, Action<TaskEditor, Host> check)
    {
        var editor = new TaskEditor(item, false, save, recurrence, allowRecurrence, urgentHours);
        try { using (var host = new Host(editor)) check(editor, host); }
        finally { Assert(new WindowInteropHelper(editor).Handle == IntPtr.Zero, "Importance tests created a native editor window."); editor.Close(); }
    }
    private static void Click(Button button)
    {
        try { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
        catch (InvalidOperationException error) {
            if (error.StackTrace == null || error.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) < 0) throw;
        }
    }
    private sealed class Host : IDisposable
    {
        private readonly Window window;
        private readonly FrameworkElement content;
        internal readonly Grid Canvas;
        internal Host(Window window)
        {
            this.window = window; content = (FrameworkElement)window.Content; window.Content = null;
            TextElement.SetFontFamily(content, window.FontFamily); TextElement.SetFontSize(content, window.FontSize); TextElement.SetForeground(content, window.Foreground);
            Canvas = new Grid { Background = window.Background }; Canvas.Children.Add(content); Layout();
        }
        internal void Layout()
        {
            Canvas.Width = window.Width; Canvas.Height = window.Height;
            Canvas.Measure(new Size(Canvas.Width, Canvas.Height)); Canvas.Arrange(new Rect(0, 0, Canvas.Width, Canvas.Height)); Canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); Canvas.UpdateLayout();
        }
        internal T Find<T>(string name) where T : DependencyObject
        {
            T result = Descendants(content, new HashSet<DependencyObject>()).OfType<T>().SingleOrDefault(value => AutomationProperties.GetName(value) == name);
            Assert(result != null, "Missing importance editor control: " + name); return result;
        }
        internal bool Fits(FrameworkElement element, FrameworkElement container)
        {
            Rect bounds = element.TransformToAncestor(container).TransformBounds(new Rect(new Point(), element.RenderSize));
            return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
                && bounds.Right <= container.ActualWidth + 1 && bounds.Bottom <= container.ActualHeight + 1;
        }
        public void Dispose() { Canvas.Children.Clear(); window.Content = content; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(parent)) yield break;
        yield return parent;
        foreach (object child in LogicalTreeHelper.GetChildren(parent)) {
            var value = child as DependencyObject;
            if (value != null) foreach (DependencyObject nested in Descendants(value, visited)) yield return nested;
        }
        if (parent is Visual) for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (DependencyObject nested in Descendants(VisualTreeHelper.GetChild(parent, index), visited)) yield return nested;
    }
    private static void Assert(bool condition, string message)
    {
        checks++; if (!condition) throw new InvalidOperationException(message);
    }
}
