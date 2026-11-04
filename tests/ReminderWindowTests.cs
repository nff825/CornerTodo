using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CornerTodo;

internal static class ReminderWindowTests
{
    private static int checks;
    private static readonly DateTime Now = new DateTime(2026, 11, 19, 14, 30, 0);
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var one = ReminderControllerTests.Item("card-one");
        using (var scope = new CardScope()) {
            ReminderWindow window = scope.Window;
            int views = 0, completions = 0, dismissals = 0;
            window.DismissRequested += delegate { dismissals++; };
            window.UpdateItems(new[] { one }, delegate(TodoItem item) { Check(item.Id == one.Id, "View targets the wrong item."); views++; },
                delegate(TodoItem item) { Check(item.Id == one.Id, "Completion targets the wrong item."); completions++; });
            Check(window.Topmost && !window.ShowActivated && !window.ShowInTaskbar && window.WindowStyle == WindowStyle.None,
                "Reminder card does not declare non-activating independent floating presentation.");
            Check(Descendants<TextBlock>(window.Content).Any(t => t.Text == "距截止还有1天2小时30分钟"), "Long lead-time text does not match the example deadline.");
            Check(Descendants<TextBlock>(window.Content).Any(t => t.Text == "11月20日 17:00 截止"), "Popup deadline is incorrect.");
            Button target = Find(window, "reminder-view");
            window.UpdateItems(new[] { TodoStateCopy.Copy(one) }, delegate { views++; }, delegate { completions++; });
            Check(Object.ReferenceEquals(target, Find(window, "reminder-view")), "Timer-only refresh replaced an active button target.");
            Click(Find(window, "reminder-view")); Click(Find(window, "reminder-complete")); Click(Find(window, "reminder-dismiss"));
            Check(views == 1 && completions == 1 && dismissals == 1 && !one.IsCompleted && one.ReminderHandledAt == null,
                "Card actions mutated task state directly or dismiss invoked completion.");
            window.ShowFailure("示例保存失败，可稍后重试。"); Check(window.ErrorText.Visibility == Visibility.Visible, "Save retry feedback is missing.");
            window.ErrorText.Visibility = Visibility.Collapsed;
            Render(window, Path.Combine(output, "reminder-card.png"));
        }
        using (var scope = new CardScope()) {
            var many = new List<TodoItem>();
            for (int index = 0; index < 20; index++) {
                TodoItem item = ReminderControllerTests.Item("many-" + index); item.Text = index == 0 ? "同时到期的多项任务，集中显示在一张提醒卡片" : "示例事项 " + (index + 1);
                many.Add(item);
            }
            scope.Window.UpdateItems(many, delegate { }, delegate { });
            Check(Descendants<TextBlock>(scope.Window.Content).Any(t => t.Text.Contains("20 项")), "Merged card does not show its aggregate count.");
            Check(scope.Window.ReminderRows.Children.OfType<StackPanel>().Count() == 20 && scope.Window.ReminderScroll.MaxHeight <= 390,
                "Multiple reminders created an unbounded window instead of a bounded scrolling list.");
            scope.Window.FitTo(new Rect(0, 0, 640, 500));
            Check(scope.Window.Width <= 616 && scope.Window.MaxHeight <= 476 && scope.Window.ReminderScroll.MaxHeight <= 366,
                "Reminder card did not fit a small work area.");
            Render(scope.Window, Path.Combine(output, "reminder-batch.png"));
        }
        using (var scope = new CardScope()) {
            var item = ReminderControllerTests.Item("extreme"); item.Text = new string('长', 80); item.DueDate = DateTime.MaxValue.AddDays(-1);
            scope.Window.UpdateItems(new[] { item }, delegate { }, delegate { });
            scope.Window.FitTo(new Rect(-1280, 0, 1280, 720));
            Check(scope.Window.Left < 0 && scope.Window.Top >= 0 && scope.Window.MaxHeight <= 696,
                "A negative work-area origin moved the reminder outside its work area.");
            Check(ReminderWindow.Remaining(item, Now).StartsWith("距截止还有") && !ReminderWindow.Remaining(item, Now).Contains("-"),
                "Extreme valid deadlines overflowed remaining-time formatting.");
            item.DueDate = Now; Check(ReminderWindow.Remaining(item, Now) == "已到截止时间", "At-deadline reminder says it is early.");
            item.DueDate = Now.AddSeconds(30); Check(ReminderWindow.Remaining(item, Now) == "距截止不足1分钟", "Sub-minute remaining time is misleading.");
            item.DueDate = Now.AddMinutes(-90); Check(ReminderWindow.Remaining(item, Now) == "已逾期1小时30分钟", "Late reminder duration is incorrect.");
            item.DueDate = Now.AddDays(1).AddHours(2).AddMinutes(30).ToUniversalTime();
            Check(ReminderWindow.Remaining(item, Now.ToUniversalTime()) == "距截止还有1天2小时30分钟", "UTC deadlines changed elapsed-time presentation.");
        }
        Console.WriteLine("PASS: " + checks + " reminder card/actions/layout/format assertions; real controls rendered offscreen, no Window.Show or native notifications.");
    }

    private static Button Find(ReminderWindow window, string tag) { return Descendants<Button>(window.Content).Single(b => Object.Equals(b.Tag, tag)); }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
    private static IEnumerable<T> Descendants<T>(object root) where T : class
    {
        T self = root as T; if (self != null) yield return self;
        DependencyObject dependency = root as DependencyObject; if (dependency == null) yield break;
        foreach (object child in LogicalTreeHelper.GetChildren(dependency)) foreach (T item in Descendants<T>(child)) yield return item;
    }
    private static void Render(ReminderWindow window, string path)
    {
        var content = (FrameworkElement)window.Content; window.Content = null;
        content.Width = Math.Min(400, window.Width); content.Measure(new Size(content.Width, Double.PositiveInfinity));
        double height = Math.Ceiling(content.DesiredSize.Height);
        var canvas = new Canvas { Width = content.Width + 24, Height = height + 24, Background = Theme.Brush("#DDECF1"),
            UseLayoutRounding = window.UseLayoutRounding, SnapsToDevicePixels = window.SnapsToDevicePixels };
        Canvas.SetLeft(content, 12); Canvas.SetTop(content, 12); canvas.Children.Add(content);
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width * scale), (int)Math.Ceiling(canvas.Height * scale), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write)) encoder.Save(stream);
        Check(content.DesiredSize.Width <= 400.1 && height > 100 && bitmap.PixelHeight > 150, "Rendered reminder content is clipped or empty.");
        canvas.Children.Clear();
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private sealed class CardScope : IDisposable
    {
        internal readonly ReminderWindow Window = new ReminderWindow(delegate { return Now; });
        public void Dispose() { Check(new WindowInteropHelper(Window).Handle == IntPtr.Zero, "A reminder card test created an HWND."); Window.Close(); }
    }
}
