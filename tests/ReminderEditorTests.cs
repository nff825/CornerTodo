using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CornerTodo;

internal static class ReminderEditorTests
{
    private static int checks;

    internal static void Run(string imageDirectory = null)
    {
        checks = 0;
        DefaultsAndAvailability();
        DurationSelectionAndPreview();
        ValidationRetainsDraft();
        ClearingScheduleAndReminder();
        FailedSaveAndRetry();
        RecurringDraftsAndOriginalIsolation();
        SmallLayouts();
        ListIndicators();
        RenderExamples(imageDirectory ?? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "reminder-editor-qa")));
        Console.WriteLine("PASS: " + checks + " reminder editor/list assertions; all windows unshown, no native notifications or personal data.");
    }

    private static TodoItem Fixture()
    {
        return new TodoItem { Id = "reminder-editor-fixture", Text = "向李老师汇报研究进展", Notes = "保留编辑输入", HasDeadline = true, HasTime = true,
            DueDate = new DateTime(2026, 11, 20, 17, 0, 0), CreatedAt = new DateTime(2026, 11, 1), ReminderEnabled = false };
    }

    private static void DefaultsAndAvailability()
    {
        WithEditor(null, null, delegate(TaskEditor editor, Host host) {
            CheckBox enabled = host.Find<CheckBox>("提醒我");
            Assert(enabled.IsChecked == false && !enabled.IsEnabled, "A new untimed task opted into or enabled reminder settings.");
            Assert(host.Find<StackPanel>("提醒提前时长设置").Visibility == Visibility.Collapsed, "Disabled reminders exposed their settings.");
            Assert(host.Find<TextBlock>("提醒启用说明").Visibility == Visibility.Visible, "An unavailable reminder had no explanation.");
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            Assert(enabled.IsEnabled && enabled.IsChecked == false, "Choosing a specific time silently turned on reminders.");
            Assert(host.Find<TextBlock>("提醒启用说明").Visibility == Visibility.Collapsed, "Available reminders retained a confusing disabled explanation.");
            enabled.IsChecked = true; host.Layout();
            Assert(host.Find<StackPanel>("提醒提前时长设置").Visibility == Visibility.Visible, "Checking the reminder did not expand the numeric fields.");
            Assert(host.Find<ComboBox>("提醒提前天数").Text == "0" && host.Find<ComboBox>("提醒提前小时数").Text == "00"
                && host.Find<ComboBox>("提醒提前分钟数").Text == "15", "The newly enabled draft lost its short default lead time.");
            foreach (ComboBox input in Parts(host)) Assert(input.IsEditable, "Reminder units no longer allow keyboard entry.");
            host.Find<ComboBox>("提醒提前天数").Text = "7";
            enabled.IsChecked = false; host.Layout();
            Assert(host.Find<StackPanel>("提醒提前时长设置").Visibility == Visibility.Collapsed, "Unchecking reminders left their fields expanded.");
            enabled.IsChecked = true;
            Assert(host.Find<ComboBox>("提醒提前天数").Text == "7", "Temporarily collapsing the settings discarded its draft.");
        });
    }

    private static void DurationSelectionAndPreview()
    {
        foreach (int[] parts in new[] { new[] { 0, 0, 0 }, new[] { 1, 2, 30 }, new[] { 7, 0, 0 }, new[] { 3650, 23, 59 } }) {
            TodoItem item = Fixture(); item.DueDate = DateTime.Today.AddDays(3652).AddHours(17);
            WithEditor(item, null, delegate(TaskEditor editor, Host host) {
                host.Find<CheckBox>("提醒我").IsChecked = true;
                SetParts(host, parts[0].ToString(), parts[1].ToString(), parts[2].ToString()); host.Layout();
                int expectedLead = parts[0] * 1440 + parts[1] * 60 + parts[2];
                DateTime expectedTime = item.DueDate.AddMinutes(-expectedLead);
                string summary = host.Find<TextBlock>("提醒时间预览").Text;
                Assert(summary.Contains(expectedTime.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture)), "Preview did not combine days, hours and minutes into the exact reminder time.");
                Save(host, editor);
                Assert(editor.Result != null && editor.Result.ReminderEnabled && editor.Result.ReminderMinutesBefore == expectedLead,
                    "Saving a selected duration lost its reminder flag or total minutes.");
                Assert(host.Find<TextBlock>("提醒时间预览").Text == summary, "Saving changed the visible reminder preview.");
            });
        }
        TodoItem example = Fixture(); example.ReminderEnabled = true; example.ReminderMinutesBefore = 1590;
        WithEditor(example, null, delegate(TaskEditor editor, Host host) {
            Assert(host.Find<CheckBox>("提醒我").IsChecked == true && host.Find<StackPanel>("提醒提前时长设置").Visibility == Visibility.Visible,
                "Editing an enabled reminder did not restore its expanded controls.");
            Assert(host.Find<ComboBox>("提醒提前天数").Text == "1" && host.Find<ComboBox>("提醒提前小时数").Text == "02"
                && host.Find<ComboBox>("提醒提前分钟数").Text == "30", "Existing duration was split into incorrect units.");
            Assert(host.Find<TextBlock>("提醒时间预览").Text.Contains("11月19日 14:30"), "The approved 17:00 minus 1d2h30m example is wrong.");
            ComboBox minute = host.Find<ComboBox>("提醒提前分钟数"); minute.SelectedItem = "00";
            Assert(host.Find<TextBlock>("提醒时间预览").Text.Contains("11月19日 15:00"), "Point-selecting a new minute did not refresh the summary.");
        });
    }

    private static void ValidationRetainsDraft()
    {
        int calls = 0;
        WithEditor(Fixture(), delegate { calls++; return true; }, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("提醒我").IsChecked = true;
            host.Find<TextBox>("待办事项").Text = "提醒失败仍保留事项";
            host.Find<TextBox>("备注，选填").Text = "提醒失败仍保留备注";
            foreach (string[] parts in new[] { new[] { "-1", "0", "0" }, new[] { "+1", "0", "0" }, new[] { "3651", "0", "0" },
                new[] { "1.5", "0", "0" }, new[] { "", "0", "0" }, new[] { "10000", "0", "0" }, new[] { "1", "24", "0" },
                new[] { "1", "2", "60" }, new[] { "1", "-2", "0" }, new[] { "1", "2", "bad" } }) {
                SetParts(host, parts[0], parts[1], parts[2]); Click(host.Find<Button>("保存待办"));
                Assert(editor.Result == null && calls == 0 && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("提醒"), "An invalid reminder reached persistence.");
                Assert(Parts(host).Select(input => input.Text).SequenceEqual(parts), "Reminder validation rewrote invalid input before the user could correct it.");
                Assert(host.Find<TextBox>("备注，选填").Text == "提醒失败仍保留备注", "Reminder validation lost other editable fields.");
            }
        });
        TodoItem past = Fixture(); past.DueDate = DateTime.Now.AddDays(-3);
        WithEditor(past, null, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("提醒我").IsChecked = true;
            Assert(host.Find<TextBlock>("提醒时间预览").Text.Contains("不补弹"), "A past reminder plan did not explain that it will not suddenly replay.");
        });
        TodoItem minimum = Fixture(); minimum.DueDate = new DateTime(1, 1, 2, 17, 0, 0);
        WithEditor(minimum, delegate { calls++; return true; }, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("提醒我").IsChecked = true; SetParts(host, "3650", "0", "0");
            Click(host.Find<Button>("保存待办"));
            Assert(editor.Result == null && host.Find<TextBlock>("待办编辑错误提示").Text.Contains("范围"), "An underflowing reminder was submitted.");
        });
    }

    private static void ClearingScheduleAndReminder()
    {
        foreach (bool clearDate in new[] { true, false }) {
            TodoItem original = Fixture(); original.ReminderEnabled = true; original.ReminderMinutesBefore = 60;
            WithEditor(original, null, delegate(TaskEditor editor, Host host) {
                host.Find<CheckBox>(clearDate ? "设置截止日期" : "设置具体时刻").IsChecked = false;
                Assert(!host.Find<CheckBox>("提醒我").IsEnabled && host.Find<CheckBox>("提醒我").IsChecked == false
                    && host.Find<StackPanel>("提醒提前时长设置").Visibility == Visibility.Collapsed, "Removing the scheduling requirement left reminder fields active.");
                SetParts(host, "invalid", "invalid", "invalid"); Save(host, editor);
                Assert(editor.Result != null && !editor.Result.ReminderEnabled && editor.Result.ReminderMinutesBefore == 0,
                    "Hidden reminder values prevented clearing or leaked into a disabled draft.");
                Assert(original.ReminderEnabled && original.ReminderMinutesBefore == 60, "Editing schedule controls mutated the original task.");
            });
        }
        WithEditor(Fixture(), null, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("提醒我").IsChecked = true;
            host.Find<CheckBox>("设置具体时刻").IsChecked = false;
            host.Find<CheckBox>("设置具体时刻").IsChecked = true;
            Assert(host.Find<CheckBox>("提醒我").IsChecked == false, "Restoring a clock time silently opted back into reminders.");
        });
    }

    private static void FailedSaveAndRetry()
    {
        int attempts = 0; TaskDraft submitted = null;
        WithEditor(Fixture(), delegate(TaskDraft draft) { attempts++; submitted = draft; return attempts > 1; }, delegate(TaskEditor editor, Host host) {
            host.Find<CheckBox>("提醒我").IsChecked = true; SetParts(host, "2", "3", "4");
            Click(host.Find<Button>("保存待办"));
            Assert(editor.Result == null && attempts == 1 && submitted.ReminderEnabled && submitted.ReminderMinutesBefore == 3064,
                "A failed save incorrectly published or omitted the reminder draft.");
            Assert(Parts(host).Select(input => input.Text).SequenceEqual(new[] { "2", "3", "4" })
                && host.Find<CheckBox>("提醒我").IsChecked == true, "Failed persistence discarded reminder inputs.");
            Save(host, editor);
            Assert(editor.Result != null && attempts == 2 && editor.Result.ReminderMinutesBefore == 3064, "Reminder save could not be retried with its retained draft.");
        });
    }

    private static void RecurringDraftsAndOriginalIsolation()
    {
        TodoItem original = Fixture(); original.ReminderEnabled = true; original.ReminderMinutesBefore = 1440;
        var editor = new TaskEditor(original, false, delegate { return false; }, new RecurrenceOptions { Frequency = RecurrenceFrequency.Weekly, WeekDays = 2,
            StartDate = original.DueDate.Date }, true);
        try { using (var host = new Host(editor)) {
            Assert(host.Find<CheckBox>("提醒我").IsChecked == true, "Rule editing lost its reminder fields.");
            Assert(host.Find<CheckBox>("重复任务").IsChecked == true, "Reminder settings interfered with repeat settings.");
            host.Find<CheckBox>("提醒我").IsChecked = false; Click(host.Find<Button>("保存待办"));
            Assert(original.ReminderEnabled && original.ReminderMinutesBefore == 1440, "A rejected rule edit mutated reminder settings on the supplied item.");
        } } finally { Assert(NoHandle(editor), "Rule editor created an HWND."); editor.Close(); }
        original.RecurringRuleId = "rule-fixture";
        WithEditor(original, null, delegate(TaskEditor occurrence, Host host) {
            Assert(host.Find<CheckBox>("重复任务").Visibility == Visibility.Collapsed && host.Find<CheckBox>("提醒我").IsChecked == true,
                "Editing this occurrence hid its independently editable reminder or exposed future-rule editing.");
            host.Find<CheckBox>("提醒我").IsChecked = false; Save(host, occurrence);
            Assert(!occurrence.Result.ReminderEnabled && occurrence.Result.Recurrence == null && original.ReminderEnabled,
                "Editing this occurrence changed its template or original object.");
        });
    }

    private static void SmallLayouts()
    {
        foreach (int width in new[] { 320, 460 }) foreach (int height in new[] { 300, 500 }) {
            var editor = new TaskEditor(Fixture());
            try {
                editor.Width = width; editor.MaxHeight = height; editor.Height = height;
                using (var host = new Host(editor)) {
                    host.Find<CheckBox>("提醒我").IsChecked = true;
                    host.Find<CheckBox>("重复任务").IsChecked = true;
                    host.Find<ComboBox>("重复方式").SelectedIndex = 1;
                    host.Find<Expander>("展开或收起备注").IsExpanded = true; host.Layout();
                    Assert(editor.Height <= height && host.Fits(host.Find<Button>("保存待办")), "Expanded reminders pushed Save below a small available window.");
                    host.Find<StackPanel>("提醒提前时长设置").BringIntoView(); host.Layout();
                    ScrollViewer body = host.Find<ScrollViewer>("待办编辑内容");
                    foreach (ComboBox input in Parts(host)) Assert(host.FitsWithin(input, body), "A reminder unit was clipped horizontally or could not be scrolled into view.");
                    Assert(host.Fits(host.Find<Button>("保存待办")), "Scrolling to reminder settings displaced the fixed save action.");
                }
            }
            finally { Assert(NoHandle(editor), "Small layout created an HWND."); editor.Close(); }
        }
    }

    private static void ListIndicators()
    {
        TodoItem item = Fixture(); item.ReminderEnabled = true; item.ReminderMinutesBefore = 1590;
        var state = new TodoState(); state.Items.Add(item); string edited = null;
        var view = new TodoPanelView(new TodoViewActions { Edit = delegate(TodoItem chosen) { edited = chosen.Id; } });
        view.Render(state, DateTime.Now, false, null);
        Button bell = Descendants(view.List, new HashSet<DependencyObject>()).OfType<Button>().Single(button => Object.Equals(button.Tag, "reminder"));
        Assert(Convert.ToString(bell.ToolTip).Contains("提前1天2小时30分钟") && Convert.ToString(bell.ToolTip).Contains("11月19日 14:30"), "The compact bell lost its exact reminder details.");
        Click(bell); Assert(edited == item.Id, "Clicking the reminder indicator did not open the correct task.");
        item.IsCompleted = true; view.Render(state, DateTime.Now, false, null);
        Assert(!Descendants(view.List, new HashSet<DependencyObject>()).OfType<Button>().Any(button => Object.Equals(button.Tag, "reminder")), "Completed tasks retained a pending-reminder bell.");
        item.IsCompleted = false; item.HasDeadline = false; item.HasTime = false; view.Render(state, DateTime.Now, false, null);
        Assert(!Descendants(view.List, new HashSet<DependencyObject>()).OfType<Button>().Any(button => Object.Equals(button.Tag, "reminder")), "A missing deadline produced a false reminder indicator.");
    }

    private static void RenderExamples(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (bool enabled in new[] { false, true }) {
            TodoItem item = Fixture(); item.Notes = ""; item.ReminderEnabled = enabled; item.ReminderMinutesBefore = enabled ? 1590 : 0;
            WithEditor(item, null, delegate(TaskEditor editor, Host host) {
                host.Layout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.Canvas.Width * 1.5), (int)Math.Ceiling(host.Canvas.Height * 1.5), 144, 144, PixelFormats.Pbgra32);
                bitmap.Render(host.Canvas); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string path = Path.Combine(directory, enabled ? "reminder-editor.png" : "reminder-default.png");
                using (var stream = File.Create(path)) encoder.Save(stream);
                Assert(File.Exists(path), "Reminder editor render was not saved.");
            });
        }
    }

    private static ComboBox[] Parts(Host host) { return new[] { host.Find<ComboBox>("提醒提前天数"), host.Find<ComboBox>("提醒提前小时数"), host.Find<ComboBox>("提醒提前分钟数") }; }
    private static void SetParts(Host host, string days, string hours, string minutes) { ComboBox[] parts = Parts(host); parts[0].Text = days; parts[1].Text = hours; parts[2].Text = minutes; }
    private static void WithEditor(TodoItem item, Func<TaskDraft, bool> save, Action<TaskEditor, Host> action)
    {
        var editor = new TaskEditor(item, false, save);
        try { using (var host = new Host(editor)) action(editor, host); }
        finally { Assert(NoHandle(editor), "Reminder tests created a native editor."); editor.Close(); }
    }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
    private static void Save(Host host, TaskEditor editor)
    {
        try { Click(host.Find<Button>("保存待办")); }
        catch (InvalidOperationException error) {
            if (editor.Result == null || error.StackTrace == null || error.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) < 0) throw;
        }
    }
    private static bool NoHandle(Window window) { return new WindowInteropHelper(window).Handle == IntPtr.Zero; }
    private static void Assert(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(parent)) yield break; yield return parent;
        foreach (object child in LogicalTreeHelper.GetChildren(parent)) {
            var value = child as DependencyObject; if (value != null) foreach (var nested in Descendants(value, visited)) yield return nested;
        }
        if (parent is Visual) for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var nested in Descendants(VisualTreeHelper.GetChild(parent, index), visited)) yield return nested;
    }
    private sealed class Host : IDisposable
    {
        private readonly Window window; private readonly FrameworkElement content;
        internal readonly Grid Canvas;
        internal Host(Window window)
        {
            this.window = window; content = (FrameworkElement)window.Content; window.Content = null;
            TextElement.SetFontFamily(content, window.FontFamily); TextElement.SetFontSize(content, window.FontSize); TextElement.SetForeground(content, window.Foreground);
            Canvas = new Grid { Background = window.Background, UseLayoutRounding = window.UseLayoutRounding, SnapsToDevicePixels = window.SnapsToDevicePixels };
            Canvas.Children.Add(content); Layout();
        }
        internal void Layout()
        {
            Canvas.Width = window.Width; Canvas.Height = window.Height;
            Canvas.Measure(new Size(Canvas.Width, Canvas.Height)); Canvas.Arrange(new Rect(0, 0, Canvas.Width, Canvas.Height)); Canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); Canvas.UpdateLayout();
        }
        internal T Find<T>(string name) where T : DependencyObject
        {
            T found = Descendants(content, new HashSet<DependencyObject>()).OfType<T>().SingleOrDefault(value => AutomationProperties.GetName(value) == name);
            Assert(found != null, "Missing reminder control: " + name); return found;
        }
        internal bool Fits(FrameworkElement element) { return FitsWithin(element, Canvas); }
        internal bool FitsWithin(FrameworkElement element, FrameworkElement container)
        {
            Rect bounds = element.TransformToAncestor(container).TransformBounds(new Rect(new Point(), element.RenderSize));
            return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Right <= container.ActualWidth + 1
                && bounds.Top >= -1 && bounds.Bottom <= container.ActualHeight + 1;
        }
        public void Dispose() { Canvas.Children.Clear(); window.Content = content; }
    }
}
