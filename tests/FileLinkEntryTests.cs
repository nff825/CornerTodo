using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CornerTodo;

internal static class FileLinkEntryTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Local);
    private static int checks;

    internal static void Run(string outputDirectory)
    {
        checks = 0;
        PanelRouting(outputDirectory);
        SecondaryViewRouting();
        MainWindowPersistence();
        Placement();
        Console.WriteLine("PASS: " + checks + " file-link entry/controller assertions; unshown windows, fake persistence, no opened files.");
    }

    private static TodoState Fixture()
    {
        var state = new TodoState(); state.Settings.AutoExpand = false;
        state.Items.Add(new TodoItem { Id = "linked", Text = "向李老师汇报研究进展", HasTime = true,
            CreatedAt = Now.AddDays(-3), DueDate = Now.AddHours(3), IsImportant = true,
            FileLinks = new List<string> { @"C:\研究资料\研究进展.pptx", @"D:\实验数据\实验数据.xlsx" } });
        state.Items.Add(new TodoItem { Id = "empty", Text = "阅读参考文献", HasDeadline = false, CreatedAt = Now });
        state.Items.Add(new TodoItem { Id = "done", Text = "整理上周实验记录", DueDate = Now,
            IsCompleted = true, CompletedAt = Now, CreatedAt = Now.AddDays(-2),
            FileLinks = new List<string> { @"C:\研究资料\实验记录.docx" } });
        return state;
    }

    private static void PanelRouting(string directory)
    {
        TodoState state = Fixture(); TodoItem requested = null; FrameworkElement anchor = null;
        var view = new TodoPanelView(new TodoViewActions {
            OpenFiles = delegate(TodoItem item, FrameworkElement source) { requested = item; anchor = source; }
        });
        view.Render(state, Now, true, null);
        var entry = Find<Button>(view.Surface, "TaskFiles_linked");
        Check(entry.Visibility == Visibility.Visible && entry.IsEnabled, "A linked task has no active compact entry.");
        Check(Find<Button>(view.Surface, "TaskFiles_empty").Visibility == Visibility.Collapsed,
            "An unlinked task consumes space for an empty attachment count.");
        Click(entry);
        Check(requested.Id == "linked" && requested.FileLinks.Count == 2 && Object.ReferenceEquals(anchor, entry),
            "Panel file entry did not forward its task and visual anchor.");
        Click(Find<Button>(view.Surface, "TaskFiles_done"));
        Check(requested.Id == "done", "Completed tasks cannot open retained file associations.");
        var emptyRow = Elements<Grid>(view.Surface).Single(row => Object.Equals(row.Tag, "empty"));
        var menu = emptyRow.ContextMenu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Header, "相关文件…"));
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menu));
        Check(requested.Id == "empty" && Object.ReferenceEquals(anchor, emptyRow), "An empty task has no context-menu path to add files.");
        state.Items[0] = TodoStateCopy.Copy(state.Items[0]);
        state.Items[0].FileLinks.Add(@"D:\工作资料\补充材料.pdf");
        view.Render(state, Now, true, null);
        entry = Find<Button>(view.Surface, "TaskFiles_linked"); Click(entry);
        Check(requested.FileLinks.Count == 3 && AutomationProperties.GetName(entry).Contains("3"),
            "A re-rendered panel entry used an old association snapshot.");
        Check(state.Items[0].FileLinks.Count == 3, "Viewing file entries modified task metadata.");
        if (!String.IsNullOrWhiteSpace(directory)) Render(view.Surface, 480, 610, directory, "file-links-panel.png");
    }

    private static void SecondaryViewRouting()
    {
        TodoState state = Fixture(); TodoItem requested = null; FrameworkElement anchor = null;
        Action<TodoItem, FrameworkElement> open = delegate(TodoItem item, FrameworkElement source) { requested = item; anchor = source; };
        var calendar = new DeadlineCalendarWindow(state, Now, null, null, open);
        var quadrants = new QuadrantWindow(state, Now, null, null, null, null, open);
        try {
            Button calendarEntry = Find<Button>(calendar, "CalendarFiles_linked"); Click(calendarEntry);
            Check(requested.Id == "linked" && Object.ReferenceEquals(anchor, calendarEntry), "Calendar file routing is incorrect.");
            Button quadrantEntry = Find<Button>(quadrants, "QuadrantFiles_linked"); Click(quadrantEntry);
            Check(requested.Id == "linked" && Object.ReferenceEquals(anchor, quadrantEntry), "Quadrant file routing is incorrect.");
            state.Items[0] = TodoStateCopy.Copy(state.Items[0]); state.Items[0].FileLinks = new List<string> { @"D:\新资料\新版.pptx" };
            calendar.Refresh(state, Now); quadrants.Refresh(state, Now);
            Check(Object.ReferenceEquals(calendarEntry, Find<Button>(calendar, "CalendarFiles_linked")) &&
                Object.ReferenceEquals(quadrantEntry, Find<Button>(quadrants, "QuadrantFiles_linked")),
                "A file update unnecessarily recreated a retained calendar or quadrant row.");
            Click(calendarEntry); Check(requested.FileLinks.Single() == @"D:\新资料\新版.pptx", "Calendar entry retained an old path.");
            Click(quadrantEntry); Check(requested.FileLinks.Single() == @"D:\新资料\新版.pptx", "Quadrant entry retained an old path.");
            state.Items[0].FileLinks.Clear(); calendar.Refresh(state, Now); quadrants.Refresh(state, Now);
            Check(calendarEntry.Visibility == Visibility.Collapsed && quadrantEntry.Visibility == Visibility.Collapsed,
                "Removing the last file left a visible count in a secondary view.");
            Check(new WindowInteropHelper(calendar).Handle == IntPtr.Zero && new WindowInteropHelper(quadrants).Handle == IntPtr.Zero,
                "File routing tests created native windows.");
        }
        finally { calendar.Close(); quadrants.Close(); }
    }

    private static void MainWindowPersistence()
    {
        var store = new MemoryStore();
        var session = new TodoSession(store, Fixture(), false, delegate { return Now; });
        string unusedDirectory = Path.Combine(Path.GetTempPath(), "CornerTodo-UnusedFileEntry-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(new TodoStore(unusedDirectory), session, true, true,
            new UnavailableStartupManager(null), new FakePanelHost(), new ApplicationInteraction());
        try {
            store.Fail = true;
            Check(!window.TrySaveFileLinks("linked", new[] { @"D:\工作资料\更新.pptx" }), "The coordinator accepted a failed file-link save.");
            Check(window.CurrentState.Items[0].FileLinks.Count == 2, "Failed coordinator save changed committed links.");
            Check(window.Panel.StatusText.Text.Contains("未保存"), "Failed coordinator save has no visible status.");
            store.Fail = false;
            Check(window.TrySaveFileLinks("linked", new[] { @"D:\工作资料\更新.pptx" }), "The coordinator could not retry a failed save.");
            Check(window.CurrentState.Items[0].FileLinks.Single() == @"D:\工作资料\更新.pptx", "The successful coordinator save was not published.");
            Check(AutomationProperties.GetName(Find<Button>(window, "TaskFiles_linked")).Contains("1"),
                "A successful coordinator save did not refresh the compact count.");
            Check(new WindowInteropHelper(window).Handle == IntPtr.Zero && !Directory.Exists(unusedDirectory),
                "The coordinator fixture created a native window or a real data directory.");
        }
        finally { window.Dispose(); window.Close(); }
    }

    private static void Placement()
    {
        var window = new Window { Width = 480, Height = 360 };
        try {
            Rect area = new Rect(-1920, 0, 1920, 1040);
            MainWindow.PositionFileLinks(window, area, new Rect(-200, 150, 30, 30));
            Check(window.Left >= area.Left + 12 && window.Left + window.Width <= area.Right - 12,
                "A file popover escapes the horizontal work area.");
            Check(Math.Abs(window.Top - 186) < 0.001, "A file popover is not placed beneath its entry.");
            MainWindow.PositionFileLinks(window, area, new Rect(-1900, 1000, 30, 25));
            Check(window.Top + window.Height <= 994 && window.Left == area.Left + 12,
                "A bottom-edge popover did not flip above or clamp to the left margin.");
            Check(new WindowInteropHelper(window).Handle == IntPtr.Zero, "Popover geometry created a native window.");
        }
        finally { window.Close(); }
    }

    private sealed class MemoryStore : ITodoStore
    {
        internal bool Fail;
        public void Save(TodoState state) { if (Fail) throw new IOException("Synthetic file-link write failure."); }
    }

    private sealed class FakePanelHost : IPanelHost
    {
        private Rect bounds = new Rect(1200, 20, 480, 610);
        public Rect GetBounds() { return bounds; }
        public void SetBounds(Rect value) { bounds = value; }
        public Rect WorkArea() { return new Rect(0, 0, 1920, 1040); }
    }

    private static void Render(FrameworkElement content, int width, int height, string directory, string name)
    {
        TextElement.SetFontFamily(content, new FontFamily("Microsoft YaHei UI"));
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(directory);
        using (FileStream stream = File.Create(Path.Combine(directory, name))) encoder.Save(stream);
    }

    private static void Click(Button button)
    {
        Check(button != null, "The expected file entry is missing.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static T Find<T>(object root, string id) where T : DependencyObject
    {
        return Elements<T>(root).SingleOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    }

    private static IEnumerable<T> Elements<T>(object root) where T : DependencyObject
    {
        T element = root as T; if (element != null) yield return element;
        DependencyObject dependency = root as DependencyObject; if (dependency == null) yield break;
        foreach (object child in LogicalTreeHelper.GetChildren(dependency))
            foreach (T nested in Elements<T>(child)) yield return nested;
    }

    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
}
