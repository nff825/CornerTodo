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
using System.Windows.Threading;
using CornerTodo;

internal static class FileLinkWindowTests
{
    private const string First = @"C:\研究资料\实验报告.docx";
    private const string Second = @"D:\项目文件\分析结果.xlsx";
    private const string Foreign = "/Users/research/Documents/论文草稿.pdf";
    private static int checks, groups;

    [STAThread]
    public static int Main(string[] args)
    {
        Application application = null;
        try {
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Run(args.Length == 0 ? null : args[0]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { if (application != null) application.Shutdown(); }
    }

    internal static void Run(string outputDirectory = null)
    {
        checks = groups = 0;
        Test("Empty state and cross-platform file-name presentation", Presentation);
        Test("Open and locate route to the original path without saving", OpenAndLocate);
        Test("Missing files keep associations and expose a recoverable explanation", MissingFiles);
        Test("Cancelling and duplicate picks make no unnecessary save", AddAndCancel);
        Test("Failed or throwing saves retain associations and permit retry", FailedSave);
        Test("Relinking handles cancellation, failure, duplicate targets and retry", Relinking);
        Test("Removing an association saves only its new list", Removing);
        Test("Refresh and appearance preview preserve source ownership and row controls", RefreshAndAppearance);
        Test("The native service rejects missing or foreign paths before starting another process", NativeValidation);
        Test("Small layouts keep actions reachable and long lists scroll", delegate { LayoutAndRendering(outputDirectory); });
        Console.WriteLine("PASS: " + groups + " file-link window groups, " + checks + " checks; fake dialogs, launchers and persistence, no shown windows.");
    }

    private static void Test(string name, Action test) { test(); groups++; Console.WriteLine("  OK " + name); }

    private static void Presentation()
    {
        using (var fixture = new Fixture()) {
            Check(Find<TextBlock>(fixture.Window, "FileLinksHeading").Text == "相关文件 0", "Empty count is wrong.");
            Check(Find<TextBlock>(fixture.Window, "FileLinksEmpty").Visibility == Visibility.Visible, "Empty list lacks its state.");
            Check(fixture.Service.Calls == 0 && fixture.SaveCalls == 0, "Construction accessed files or saved metadata.");
            fixture.Item.FileLinks = new List<string> { First, Foreign, @"\\server\share\协作计划.pdf" };
            fixture.Window.Refresh(fixture.Item);
            Check(Find<TextBlock>(fixture.Window, "FileLinkName_0").Text == "实验报告.docx", "Windows basename is wrong.");
            Check(Find<TextBlock>(fixture.Window, "FileLinkName_1").Text == "论文草稿.pdf", "A POSIX location appeared as an entire file name.");
            Check(Find<TextBlock>(fixture.Window, "FileLinkName_2").Text == "协作计划.pdf", "UNC basename is wrong.");
            Check(FileLinksWindow.FileName("C:/mixed/folder/figure.png") == "figure.png", "Forward-slash Windows basename is wrong.");
            Check(FileLinksWindow.FileName("/Users/research/Documents/草稿\\修订.pdf") == "草稿\\修订.pdf", "A valid POSIX file-name backslash was treated as a separator.");
            Check(Find<TextBlock>(fixture.Window, "FileLinkDirectory_1").Text == "/Users/research/Documents/", "Secondary directory is wrong.");
            List<string> exposed = fixture.Window.CurrentLinks; exposed.Clear();
            Check(fixture.Window.CurrentLinks.Count == 3, "CurrentLinks aliases the displayed list.");
            Check(fixture.Item.FileLinks.Count == 3 && fixture.Service.Calls == 0, "Displaying links touched source files or the input list.");
        }
    }

    private static void OpenAndLocate()
    {
        using (var fixture = new Fixture(First, Foreign)) {
            Click(Find<Button>(fixture.Window, "FileLinkOpen_0"));
            Menu(fixture.Window, "FileLinkLocate_1", 1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.Service.Opened.SequenceEqual(new[] { First }) && fixture.Service.Located.SequenceEqual(new[] { Foreign }),
                "Open/locate changed a stored path or targeted the wrong file.");
            Check(fixture.SaveCalls == 0 && fixture.Window.CurrentLinks.SequenceEqual(new[] { First, Foreign }), "Open/locate modified associations.");
        }
    }

    private static void MissingFiles()
    {
        using (var fixture = new Fixture(First)) {
            fixture.Service.OpenError = new FileNotFoundException("找不到文件，请重新关联。");
            Click(Find<Button>(fixture.Window, "FileLinkOpen_0"));
            Check(Error(fixture).Contains("找不到文件") && fixture.Window.CurrentLinks.Single() == First, "Missing-file open discarded its association or error.");
            fixture.Service.LocateError = new IOException("文件已经移动。");
            Menu(fixture.Window, "FileLinkLocate_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(Error(fixture).Contains("文件已经移动") && fixture.SaveCalls == 0, "A failed locate saved or hid the failure.");
            fixture.Service.OpenError = null; Click(Find<Button>(fixture.Window, "FileLinkOpen_0"));
            Check(Find<TextBlock>(fixture.Window, "FileLinksError").Visibility == Visibility.Collapsed, "A successful retry retained an old error.");
        }
    }

    private static void AddAndCancel()
    {
        using (var fixture = new Fixture(First)) {
            fixture.Service.Picked = new List<string>(); Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            fixture.Service.Picked = null; Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            Check(fixture.SaveCalls == 0 && fixture.Window.CurrentLinks.SequenceEqual(new[] { First }), "Cancelled selection changed the list.");
            fixture.Service.Picked = new List<string> { First, First.ToUpperInvariant() };
            Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            Check(fixture.SaveCalls == 0, "Picking only duplicate Windows paths saved a redundant change.");
            fixture.Service.Picked = new List<string> { Second, Foreign, Second };
            fixture.Save = delegate(List<string> paths) { fixture.Saved = new List<string>(paths); paths.Clear(); return true; };
            Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            Check(fixture.Window.CurrentLinks.SequenceEqual(new[] { First, Second, Foreign }) && fixture.Saved.Count == 3,
                "Multiselect did not merge independently or remove duplicates.");
            Check(fixture.Item.FileLinks.SequenceEqual(new[] { First }), "Adding directly mutated the supplied task.");
            Check(Object.ReferenceEquals(fixture.Service.LastOwner, fixture.Window), "The file picker did not receive its floating window owner.");
        }
    }

    private static void FailedSave()
    {
        using (var fixture = new Fixture(First)) {
            fixture.Service.Picked = new List<string> { Second };
            fixture.Save = delegate(List<string> paths) { paths.Clear(); return false; };
            Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            Check(fixture.Window.CurrentLinks.SequenceEqual(new[] { First }) && Error(fixture).Contains("尚未保存"), "Rejected save changed the current list.");
            fixture.Save = delegate { throw new IOException("示例写入失败"); };
            Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            Check(Error(fixture).Contains("示例写入失败") && fixture.Window.CurrentLinks.Count == 1, "Throwing save lost rollback/error.");
            fixture.Save = delegate { return true; }; Click(Find<Button>(fixture.Window, "FileLinksAdd"));
            Check(fixture.Window.CurrentLinks.SequenceEqual(new[] { First, Second }) && fixture.SaveCalls == 3,
                "A rejected association could not be retried.");
            Check(Find<TextBlock>(fixture.Window, "FileLinksHeading").Text == "相关文件 2", "Successful retry did not update the count.");
        }
    }

    private static void Relinking()
    {
        using (var fixture = new Fixture(First, Second)) {
            fixture.Service.Replacement = null;
            Menu(fixture.Window, "FileLinkRelink_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.SaveCalls == 0 && fixture.Service.ReplacedPath == First, "Cancelled relink saved or targeted a different path.");
            fixture.Service.Replacement = Foreign; fixture.Save = delegate { return false; };
            Menu(fixture.Window, "FileLinkRelink_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.Window.CurrentLinks.SequenceEqual(new[] { First, Second }), "Rejected relink changed the original row.");
            fixture.Save = delegate { return true; };
            Menu(fixture.Window, "FileLinkRelink_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.Window.CurrentLinks.SequenceEqual(new[] { Foreign, Second }), "Relink retry lost replacement order.");
            Click(Find<Button>(fixture.Window, "FileLinkOpen_0"));
            Check(fixture.Service.Opened.Single() == Foreign, "Relink left an old path in the open handler.");
            fixture.Service.Replacement = Second;
            Menu(fixture.Window, "FileLinkRelink_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.Window.CurrentLinks.SequenceEqual(new[] { Second }), "Relinking to an existing association retained a duplicate.");
            Check(fixture.Item.FileLinks.SequenceEqual(new[] { First, Second }), "Relinking mutated the caller's task.");
        }
    }

    private static void Removing()
    {
        using (var fixture = new Fixture(First)) {
            fixture.Save = delegate { return false; };
            Menu(fixture.Window, "FileLinkRemove_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.Window.CurrentLinks.Single() == First && Error(fixture).Contains("尚未保存"), "Removal failure lost the association.");
            fixture.Save = delegate(List<string> paths) { fixture.Saved = new List<string>(paths); return true; };
            Menu(fixture.Window, "FileLinkRemove_0", 0).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(fixture.Window.CurrentLinks.Count == 0 && fixture.Saved.Count == 0 && fixture.Item.FileLinks.Count == 1,
                "Removal did not submit only an empty metadata list.");
            Check(fixture.Service.Calls == 0 && Find<TextBlock>(fixture.Window, "FileLinksHeading").Text == "相关文件 0", "Removal touched a file service or retained its count.");
        }
    }

    private static void RefreshAndAppearance()
    {
        using (var fixture = new Fixture(First, Second)) {
            Button retained = Find<Button>(fixture.Window, "FileLinkOpen_0");
            var large = AppearanceSettings.CreateDark(); large.TextScale = 1.5; large.BackgroundTransparency = 100;
            fixture.Window.Refresh(fixture.Item, large);
            Check(Object.ReferenceEquals(retained, Find<Button>(fixture.Window, "FileLinkOpen_0")), "Preview recreated an unchanged row.");
            Near(22.5, Find<TextBlock>(fixture.Window, "FileLinkName_0").FontSize, "File-name text scale");
            Near(16.5, Find<TextBlock>(fixture.Window, "FileLinkDirectory_0").FontSize, "Directory text scale");
            Check(((SolidColorBrush)((Border)fixture.Window.Content).Background).Color.A >= 239 &&
                ((SolidColorBrush)fixture.Window.Background).Color.A == 0, "The floating window lost its readable surface or transparent corners.");
            large.PrimaryTextColor = "#000000";
            Check(((SolidColorBrush)Find<TextBlock>(fixture.Window, "FileLinkName_0").Foreground).Color != Colors.Black,
                "Appearance preview retains a mutable caller alias.");
            fixture.Window.Refresh(fixture.Item, AppearanceSettings.CreateLight());
            Near(15, Find<TextBlock>(fixture.Window, "FileLinkName_0").FontSize, "Cancelled text-size preview");
            var updated = new TodoItem { Id = fixture.Item.Id, Text = "更新后的事项", FileLinks = new List<string> { Foreign } };
            fixture.Window.Refresh(updated); updated.FileLinks.Clear();
            Check(fixture.Window.CurrentLinks.Single() == Foreign && Find<TextBlock>(fixture.Window, "FileLinksTaskTitle").Text == "更新后的事项",
                "External refresh failed or retained the caller's link collection.");
        }
    }

    private static void NativeValidation()
    {
        var service = new WindowsFileLinkService();
        string missing = Path.Combine(Path.GetTempPath(), "CornerTodo-Missing-" + Guid.NewGuid().ToString("N"), "missing.pdf");
        foreach (Action request in new Action[] { delegate { service.Open(missing); }, delegate { service.Locate(missing); },
            delegate { service.Open(Foreign); }, delegate { service.Locate("relative-file.txt"); } }) {
            bool rejected = false;
            try { request(); }
            catch (FileNotFoundException error) { rejected = error.Message.Contains("重新关联"); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("重新关联"); }
            Check(rejected, "A missing, foreign or relative path reached a native launcher.");
        }
    }

    private static void LayoutAndRendering(string directory)
    {
        using (var fixture = new Fixture(First, Second, Foreign)) {
            Layout(fixture.Window, fixture.Window.Width, fixture.Window.Height, directory, "file-links.png");
            var large = AppearanceSettings.CreateDark(); large.TextScale = 1.5;
            fixture.Item.FileLinks = Enumerable.Range(0, 18).Select(index => @"C:\研究资料\子目录\待核对的实验原始记录与分析结果 " + index + ".pdf").ToList();
            fixture.Window.Refresh(fixture.Item, large);
            fixture.Service.OpenError = new IOException(new string('错', 2000));
            Click(Find<Button>(fixture.Window, "FileLinkOpen_0"));
            Layout(fixture.Window, 320, 300, directory, "file-links-small.png");
            ScrollViewer scroll = Find<ScrollViewer>(fixture.Window, "FileLinksList");
            Check(scroll.ViewportHeight > 0 && scroll.ScrollableHeight > 0, "The small floating window does not scroll its file list.");
            Check(Find<TextBlock>(fixture.Window, "FileLinkName_0").TextTrimming == TextTrimming.CharacterEllipsis,
                "A long file name was allowed to widen the popup.");
            scroll.ScrollToBottom(); Layout(fixture.Window, 320, 300, null, null);
            Near(scroll.ScrollableHeight, scroll.VerticalOffset, "File list bottom is unreachable");
        }
    }

    private static void Layout(FileLinksWindow window, double width, double height, string directory, string filename)
    {
        var content = (FrameworkElement)window.Content; window.Content = null;
        var host = new Border { Child = content, Width = width, Height = height, UseLayoutRounding = true, SnapsToDevicePixels = true };
        try {
            TextElement.SetFontFamily(host, window.FontFamily); TextElement.SetFontSize(host, window.FontSize);
            var size = new Size(width, height); host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); host.UpdateLayout();
            foreach (string id in new[] { "FileLinksAdd", "FileLinksClose" }) {
                Button action = Find<Button>(host, id); Point top = action.TranslatePoint(new Point(), host);
                Check(action.ActualHeight > 0 && top.Y >= 0 && top.Y + action.ActualHeight <= height + 0.1,
                    "A floating-window action is outside the small viewport: " + id);
            }
            if (!String.IsNullOrWhiteSpace(directory) && !String.IsNullOrWhiteSpace(filename)) {
                var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); image.Render(host);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(directory);
                using (FileStream stream = File.Create(Path.Combine(directory, filename))) encoder.Save(stream);
            }
        }
        finally { host.Child = null; window.Content = content; }
    }

    private static MenuItem Menu(FileLinksWindow window, string id, int index)
    { return Find<MenuItem>(Find<Button>(window, "FileLinkMore_" + index).ContextMenu, id); }
    private static string Error(Fixture fixture)
    {
        TextBlock error = Find<TextBlock>(fixture.Window, "FileLinksError");
        Check(error.Visibility == Visibility.Visible, "An operation error is hidden."); return error.Text;
    }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
    private static T Find<T>(object root, string id) where T : DependencyObject
    { return Elements<T>(root).Single(element => AutomationProperties.GetAutomationId(element) == id); }
    private static IEnumerable<T> Elements<T>(object root) where T : DependencyObject
    {
        T match = root as T; if (match != null) yield return match;
        DependencyObject dependency = root as DependencyObject; if (dependency == null) yield break;
        foreach (object child in LogicalTreeHelper.GetChildren(dependency))
            foreach (T element in Elements<T>(child)) yield return element;
    }
    private static void Near(double expected, double actual, string message)
    { Check(Math.Abs(expected - actual) < 0.001, message + ": expected " + expected + ", got " + actual); }
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private sealed class FakeService : IFileLinkService
    {
        internal IList<string> Picked = new List<string>();
        internal string Replacement, ReplacedPath;
        internal Window LastOwner;
        internal Exception OpenError, LocateError;
        internal readonly List<string> Opened = new List<string>(), Located = new List<string>();
        internal int Picks, Replacements;
        internal int Calls { get { return Picks + Replacements + Opened.Count + Located.Count; } }
        public IList<string> PickFiles(Window owner) { LastOwner = owner; Picks++; return Picked; }
        public string PickReplacement(Window owner, string path) { LastOwner = owner; Replacements++; ReplacedPath = path; return Replacement; }
        public void Open(string path) { Opened.Add(path); if (OpenError != null) throw OpenError; }
        public void Locate(string path) { Located.Add(path); if (LocateError != null) throw LocateError; }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly TodoItem Item;
        internal readonly FakeService Service = new FakeService();
        internal readonly FileLinksWindow Window;
        internal Func<List<string>, bool> Save = delegate { return true; };
        internal List<string> Saved;
        internal int SaveCalls;
        internal Fixture(params string[] links)
        {
            Item = new TodoItem { Id = "linked-task", Text = "向李老师汇报研究进展", FileLinks = links.ToList() };
            Window = new FileLinksWindow(Item, delegate(List<string> paths) { SaveCalls++; return Save(paths); }, Service);
        }
        public void Dispose()
        {
            Check(new WindowInteropHelper(Window).Handle == IntPtr.Zero, "A file-link test created a native window.");
            Window.Close();
        }
    }
}
