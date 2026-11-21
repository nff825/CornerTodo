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

internal static class FileLinkEditorTests
{
    private const string First = @"C:\研究资料\论文第二章.docx";
    private const string Second = @"D:\实验数据\本周测试记录.xlsx";
    private static int checks;

    internal static void Run(string imageDirectory = null)
    {
        checks = 0;
        EmptyDefaultsAndExistingReferences();
        MultipleSelectionDeduplicationAndRemoval();
        PickerCancellationAndErrorsKeepInput();
        SaveFailureRetryAndCancel();
        RecurringAndCompletedDrafts();
        SmallLayoutAndRendering(imageDirectory ?? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "output", "v1120-file-links-editor-20261121")));
        Console.WriteLine("PASS: " + checks + " file-link editor assertions; fake file picker, unshown WPF windows, original files untouched.");
    }

    private static TodoItem Fixture()
    {
        return new TodoItem { Id = "file-link-editor", Text = "完善论文第二章", Notes = "需要保留的备注", IsImportant = true,
            Priority = TaskPriority.Normal, FileLinks = new List<string> { First }, HasDeadline = true, HasTime = true,
            DueDate = DateTime.UtcNow.AddDays(7).AddTicks(1250), ReminderEnabled = true, ReminderMinutesBefore = 15,
            ReminderPlanId = "existing-plan", ReminderSetAt = DateTime.UtcNow.AddDays(-1), ReminderHandledAt = DateTime.UtcNow.AddHours(-1) };
    }

    private static void EmptyDefaultsAndExistingReferences()
    {
        var picker = new FakeService(); TaskDraft draft = null;
        WithEditor(null, picker, delegate(TaskDraft value) { draft = value; return true; }, null, true,
            delegate(TaskEditor editor, Host host) {
                Assert(!Files(host).IsExpanded && Files(host).FileLinks.Count == 0, "A new task expanded empty optional file settings.");
                Assert(picker.Picks == 0 && picker.OtherCalls == 0, "Constructing the editor accessed the file service.");
                Assert(!host.Find<Expander>("展开或收起备注").IsExpanded, "File settings changed optional notes defaults.");
                host.Find<TextBox>("待办事项").Text = "未关联文件的事项"; Click(host.Find<Button>("保存待办"));
                Assert(draft != null && draft.FileLinks != null && draft.FileLinks.Count == 0, "Saving an empty file list did not preserve legacy task behavior.");
            });
        TodoItem original = Fixture();
        original.FileLinks.Add(@"\\文件服务器\共享资料\参考方案.pdf");
        original.FileLinks.Add(@"/home/user/草稿\修订.pdf");
        WithEditor(original, picker, null, null, true, delegate(TaskEditor editor, Host host) {
            Assert(Files(host).IsExpanded && Files(host).FileLinks.SequenceEqual(original.FileLinks), "Editing did not retain and show existing file references.");
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkName-0").Text == "论文第二章.docx", "Windows file names were not readable.");
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkDirectory-0").Text == @"C:\研究资料", "Original file directory was not shown.");
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkName-1").Text == "参考方案.pdf", "UNC file names were not readable.");
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkName-2").Text == @"草稿\修订.pdf", "A backslash in a POSIX file name was misread as a directory separator.");
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkDirectory-2").Text == "/home/user", "A POSIX file name's backslash corrupted its original directory.");
            List<string> copy = Files(host).FileLinks; copy.Clear();
            Assert(Files(host).FileLinks.Count == 3 && original.FileLinks.Count == 3, "Reading the file draft exposed a mutable shared collection.");
            Assert(picker.Picks == 0 && picker.OtherCalls == 0, "Rendering existing or missing references accessed original files.");
        });
    }

    private static void MultipleSelectionDeduplicationAndRemoval()
    {
        TodoItem original = Fixture(); var picker = new FakeService { Selected = new List<string> { First.ToLowerInvariant(), Second, @"C:\根目录文件.pdf" } };
        WithEditor(original, picker, null, null, true, delegate(TaskEditor editor, Host host) {
            Click(host.FindId<Button>("TaskEditorAddFileLinks"));
            Assert(picker.Picks == 1 && Object.ReferenceEquals(picker.LastOwner, editor), "The multiselect picker was not owned by the editing window.");
            Assert(Files(host).FileLinks.SequenceEqual(new[] { First, Second, @"C:\根目录文件.pdf" }), "Multiselect did not retain order and deduplicate Windows path casing.");
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkDirectory-2").Text == @"C:\", "A drive-root file showed a drive-relative original directory.");
            picker.Selected.Clear();
            Assert(Files(host).FileLinks.Count == 3, "A picker-owned list remained shared with the editable draft.");
            Click(host.FindId<Button>("TaskEditorRemoveFileLink-1"));
            Assert(Files(host).FileLinks.SequenceEqual(new[] { First, @"C:\根目录文件.pdf" }), "Removing one reference discarded other files.");
            Assert(original.FileLinks.SequenceEqual(new[] { First }) && picker.OtherCalls == 0, "Removing a draft reference mutated the task or touched original files.");
            Click(host.FindId<Button>("TaskEditorRemoveFileLink-1")); Click(host.FindId<Button>("TaskEditorRemoveFileLink-0"));
            Assert(Files(host).FileLinks.Count == 0 && host.FindId<StackPanel>("TaskEditorFileLinkRows").Children.Count == 0,
                "The last removed association left a stale row or path.");
        });
    }

    private static void PickerCancellationAndErrorsKeepInput()
    {
        TodoItem original = Fixture(); var picker = new FakeService();
        WithEditor(original, picker, null, null, true, delegate(TaskEditor editor, Host host) {
            host.Find<TextBox>("待办事项").Text = "保留未保存的事项";
            host.Find<TextBox>("备注，选填").Text = "长备注\r\n第二行";
            host.Find<ComboBox>("截止小时，24小时制").Text = "17";
            host.Find<ComboBox>("截止分钟").Text = "23";
            host.Find<RadioButton>("优先级：高").IsChecked = true;
            string before = EditableValues(host);
            foreach (IList<string> cancelled in new IList<string>[] { null, new List<string>() }) {
                picker.Selected = cancelled; Click(host.FindId<Button>("TaskEditorAddFileLinks"));
                Assert(EditableValues(host) == before && Files(host).FileLinks.SequenceEqual(new[] { First }), "Cancelling file selection changed task inputs or references.");
            }
            picker.Error = new IOException("测试选择器不可用"); Click(host.FindId<Button>("TaskEditorAddFileLinks"));
            Assert(host.FindId<TextBlock>("TaskEditorFileLinkStatus").Visibility == Visibility.Visible
                && host.FindId<TextBlock>("TaskEditorFileLinkStatus").Text.Contains("当前输入已保留"), "File-picker failure had no readable retained-input explanation.");
            Assert(EditableValues(host) == before && Files(host).FileLinks.SequenceEqual(new[] { First }), "A file-picker failure discarded unsaved inputs.");
            picker.Error = null; picker.Selected = new List<string> { Second, "relative.txt" };
            Click(host.FindId<Button>("TaskEditorAddFileLinks"));
            Assert(Files(host).FileLinks.SequenceEqual(new[] { First }) && EditableValues(host) == before, "Invalid picker output partially replaced the existing file draft.");
            picker.Selected = new List<string> { Second }; Click(host.FindId<Button>("TaskEditorAddFileLinks"));
            Assert(Files(host).FileLinks.SequenceEqual(new[] { First, Second })
                && host.FindId<TextBlock>("TaskEditorFileLinkStatus").Visibility == Visibility.Collapsed, "A successful retry did not clear the stale file-selection error.");
            Assert(original.FileLinks.SequenceEqual(new[] { First }) && editor.Result == null && picker.OtherCalls == 0, "Selecting files submitted a task or touched file contents.");
        });
    }

    private static void SaveFailureRetryAndCancel()
    {
        TodoItem original = Fixture(); DateTime due = original.DueDate; var picker = new FakeService { Selected = new List<string> { Second } };
        int attempts = 0; var captured = new List<TaskDraft>();
        WithEditor(original, picker, delegate(TaskDraft draft) {
            attempts++; captured.Add(draft);
            if (attempts == 1) { draft.FileLinks.Add(@"X:\模拟失败回调修改.docx"); return false; }
            return true;
        }, null, true, delegate(TaskEditor editor, Host host) {
            Click(host.FindId<Button>("TaskEditorAddFileLinks"));
            Click(host.Find<Button>("保存待办"));
            Assert(editor.Result == null && attempts == 1 && Files(host).FileLinks.SequenceEqual(new[] { First, Second }),
                "A failed save published the draft or shared its mutable file list with the callback.");
            Assert(original.FileLinks.SequenceEqual(new[] { First }), "A failed save modified the original task's file references.");
            Files(host).IsExpanded = false;
            Click(host.Find<Button>("保存待办"));
            Assert(attempts == 2 && editor.Result != null && editor.Result.FileLinks.SequenceEqual(new[] { First, Second }),
                "Saving a collapsed file section lost its references or a retry reused the failed callback's mutated list.");
            Assert(editor.Result.DueDate == due && editor.Result.DueDate.Kind == DateTimeKind.Utc && editor.Result.HasTime,
                "A file-only edit changed deadline precision or UTC semantics.");
            Assert(editor.Result.ReminderEnabled && editor.Result.ReminderMinutesBefore == 15 && original.ReminderPlanId == "existing-plan"
                && original.ReminderHandledAt.HasValue, "A file-only edit reset reminder input or existing de-duplication state.");
            Assert(original.FileLinks.Count == 1 && captured[0].FileLinks.Count == 3 && captured[1].FileLinks.Count == 2,
                "Successful draft submission leaked collection mutations into another draft or original task.");
        });
        attempts = 0;
        WithEditor(original, picker, delegate { attempts++; return false; }, null, true, delegate(TaskEditor editor, Host host) {
            Click(host.FindId<Button>("TaskEditorAddFileLinks")); Click(host.FindId<Button>("TaskEditorRemoveFileLink-0"));
            Click(host.Find<Button>("取消编辑待办"));
            Assert(attempts == 0 && editor.Result == null && original.FileLinks.SequenceEqual(new[] { First }), "Cancelling file edits wrote task state.");
        });
        WithEditor(original, picker, delegate { attempts++; return false; }, null, true, delegate(TaskEditor editor, Host host) {
            Click(host.FindId<Button>("TaskEditorRemoveFileLink-0")); Click(host.Find<Button>("保存待办")); Click(host.Find<Button>("取消编辑待办"));
            Assert(attempts == 1 && editor.Result == null && original.FileLinks.SequenceEqual(new[] { First }), "Cancelling after failed file save wrote or discarded the original task's references.");
        });
    }

    private static void RecurringAndCompletedDrafts()
    {
        var picker = new FakeService(); TodoItem template = Fixture(); TaskDraft captured = null;
        var recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Weekly, WeekDays = 2, StartDate = DateTime.Today };
        WithEditor(template, picker, delegate(TaskDraft value) { captured = value; return false; }, recurrence, true,
            delegate(TaskEditor editor, Host host) {
                Files(host).IsExpanded = false; Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.FileLinks.SequenceEqual(new[] { First }) && captured.Recurrence != null,
                    "Editing a repeat rule dropped its original-file references.");
                captured.FileLinks.Clear(); Assert(template.FileLinks.Count == 1 && Files(host).FileLinks.Count == 1,
                    "A repeat-rule draft shared its references with the template or editor.");
            });
        TodoItem occurrence = Fixture(); occurrence.RecurringRuleId = "file-rule"; occurrence.OccurrenceDate = DateTime.Today;
        WithEditor(occurrence, picker, delegate(TaskDraft value) { captured = value; return false; }, null, false,
            delegate(TaskEditor editor, Host host) {
                Click(host.FindId<Button>("TaskEditorRemoveFileLink-0")); Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.FileLinks.Count == 0 && captured.Recurrence == null && occurrence.FileLinks.Count == 1,
                    "Removing a single occurrence's reference changed the original instance or recurring rule.");
            });
        TodoItem completed = Fixture(); completed.IsCompleted = true; completed.CompletedAt = DateTime.UtcNow;
        completed.HasDeadline = false; completed.HasTime = false; completed.ReminderEnabled = false;
        WithEditor(completed, picker, delegate(TaskDraft value) { captured = value; return false; }, null, true,
            delegate(TaskEditor editor, Host host) {
                Click(host.Find<Button>("保存待办"));
                Assert(captured != null && captured.FileLinks.SequenceEqual(new[] { First }) && !captured.HasDeadline,
                    "A completed or unscheduled task dropped existing references.");
            });
    }

    private static void SmallLayoutAndRendering(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        WithEditor(null, new FakeService(), null, null, true, delegate(TaskEditor editor, Host host) {
            host.Find<TextBox>("待办事项").Text = "新增待办";
            host.Render(Path.Combine(outputDirectory, "editor-file-links-default.png"));
        });
        TodoItem visual = Fixture(); visual.Notes = ""; visual.FileLinks.Add(Second); visual.ReminderEnabled = false;
        WithEditor(visual, new FakeService(), null, null, true, delegate(TaskEditor editor, Host host) {
            host.Render(Path.Combine(outputDirectory, "editor-file-links-expanded.png"));
            Assert(host.Fits(host.FindId<Button>("TaskEditorAddFileLinks"), host.Canvas), "Existing references opened with an inaccessible Add action.");
            Assert(host.Fits(host.Find<Button>("保存待办"), host.Canvas), "Opening linked files clipped the fixed Save action.");
        });
        TodoItem small = Fixture(); small.Notes = new string('注', TodoRules.MaxNotesLength);
        for (int index = 0; index < 25; index++) small.FileLinks.Add(@"D:\很长的原始资料文件夹\" + new string('长', 70) + index + ".pdf");
        WithEditor(small, new FakeService(), delegate { return false; }, null, true, delegate(TaskEditor editor, Host host) {
            editor.Width = 320; editor.MaxHeight = 300; editor.Height = 300;
            host.Find<CheckBox>("重复任务").IsChecked = true; host.Find<ComboBox>("重复方式").SelectedIndex = 1;
            host.Find<CheckBox>("提醒我").IsChecked = true; host.Layout();
            ScrollViewer body = host.Find<ScrollViewer>("待办编辑内容");
            Assert(body.ScrollableHeight > 0 && body.ViewportHeight > 0, "Many related files removed the small editor's usable scrolling body.");
            host.FindId<Grid>("TaskEditorFileLinkRow-0").BringIntoView(); host.Layout();
            Assert(host.Fits(host.FindId<Button>("TaskEditorRemoveFileLink-0"), body), "A file's remove action could not be reached on a small screen.");
            Assert(host.Fits(host.Find<Button>("保存待办"), host.Canvas) && host.Fits(host.Find<Button>("取消编辑待办"), host.Canvas),
                "Long file paths moved fixed Save/Cancel outside the small editor.");
            Assert(body.ExtentWidth <= body.ViewportWidth + 1, "Long original paths forced horizontal overflow.");
            host.Render(Path.Combine(outputDirectory, "editor-file-links-small.png"));
            host.Find<CheckBox>("重复任务").IsChecked = false;
            editor.SaveErrorProvider = delegate { return "保存失败：" + new string('错', 1500); };
            Click(host.Find<Button>("保存待办")); host.Layout();
            Assert(host.Fits(host.Find<Button>("保存待办"), host.Canvas) && Files(host).FileLinks.Count == 26,
                "A long persistence error clipped Save or discarded related files.");
        });
    }

    private static FileLinksEditor Files(Host host) { return host.FindId<FileLinksEditor>("TaskEditorFileLinks"); }
    private static string EditableValues(Host host)
    {
        return host.Find<TextBox>("待办事项").Text + "|" + host.Find<TextBox>("备注，选填").Text + "|"
            + host.Find<DatePicker>("截止日期").Text + "|" + host.Find<ComboBox>("截止小时，24小时制").Text + "|"
            + host.Find<ComboBox>("截止分钟").Text + "|" + host.Find<CheckBox>("提醒我").IsChecked + "|"
            + host.Find<RadioButton>("优先级：高").IsChecked;
    }
    private static void WithEditor(TodoItem item, FakeService service, Func<TaskDraft, bool> save,
        RecurrenceOptions recurrence, bool allowRecurrence, Action<TaskEditor, Host> check)
    {
        var editor = new TaskEditor(item, false, save, recurrence, allowRecurrence, 24, service);
        try { using (var host = new Host(editor)) check(editor, host); }
        finally { Assert(new WindowInteropHelper(editor).Handle == IntPtr.Zero, "File-link editor tests created a native window."); editor.Close(); }
    }
    private static void Click(Button button)
    {
        try { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
        catch (InvalidOperationException error) {
            if (error.StackTrace == null || error.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) < 0) throw;
        }
    }
    private sealed class FakeService : IFileLinkService
    {
        internal IList<string> Selected = new List<string>();
        internal Exception Error;
        internal int Picks, OtherCalls;
        internal Window LastOwner;
        public IList<string> PickFiles(Window owner) { Picks++; LastOwner = owner; if (Error != null) throw Error; return Selected; }
        public string PickReplacement(Window owner, string currentPath) { OtherCalls++; throw new InvalidOperationException("Editor must not replace files through the viewer service."); }
        public void Open(string path) { OtherCalls++; throw new InvalidOperationException("Editor must not open files."); }
        public void Locate(string path) { OtherCalls++; throw new InvalidOperationException("Editor must not locate files."); }
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
            Canvas = new Grid { Background = window.Background, UseLayoutRounding = true, SnapsToDevicePixels = true };
            Canvas.Children.Add(content); Layout();
        }
        internal void Layout()
        {
            Canvas.Width = window.Width; Canvas.Height = window.Height;
            Canvas.Measure(new Size(Canvas.Width, Canvas.Height)); Canvas.Arrange(new Rect(0, 0, Canvas.Width, Canvas.Height)); Canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); Canvas.UpdateLayout();
        }
        internal T Find<T>(string name) where T : DependencyObject
        { return FindValue<T>(delegate(T value) { return AutomationProperties.GetName(value) == name; }, name); }
        internal T FindId<T>(string id) where T : DependencyObject
        { return FindValue<T>(delegate(T value) { return AutomationProperties.GetAutomationId(value) == id; }, id); }
        private T FindValue<T>(Func<T, bool> predicate, string description) where T : DependencyObject
        {
            T value = Descendants(content, new HashSet<DependencyObject>()).OfType<T>().SingleOrDefault(predicate);
            Assert(value != null, "Missing file-link editor control: " + description); return value;
        }
        internal bool Fits(FrameworkElement element, FrameworkElement container)
        {
            Rect bounds = element.TransformToAncestor(container).TransformBounds(new Rect(new Point(), element.RenderSize));
            return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
                && bounds.Right <= container.ActualWidth + 1 && bounds.Bottom <= container.ActualHeight + 1;
        }
        internal void Render(string path)
        {
            Layout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Canvas.Width), (int)Math.Ceiling(Canvas.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(Canvas); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
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
