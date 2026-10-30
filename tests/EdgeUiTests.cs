using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CornerTodo;

// Uses the real coordinator, views and persistence with a fake desktop and clock.
internal static class EdgeUiTests
{
    private static int assertions;

    internal static void Run(string outputDirectory)
    {
        string data = Path.Combine(outputDirectory, "isolated-edge-ui-" + Guid.NewGuid().ToString("N"));
        DateTime now = DateTime.Now;
        DateTime clock = DateTime.UtcNow;
        TodoState initial = Fixture(now);
        TodoStore store = new TodoStore(data);
        store.Save(initial);
        TodoSession session = new TodoSession(store, initial, false);
        var sharedInteraction = new ApplicationInteraction();
        var host = new FakeHost();
        var window = new MainWindow(store, session, true, false, new NoStartup(), host, sharedInteraction, delegate { return clock; });
        try
        {
            window.Interaction.Initialize(initial.Settings);
            window.Interaction.UpdateExpansion(1, false, true, true);
            Assert(!window.CurrentState.Settings.EdgeAutoHide && !window.Interaction.IsEdgeHidden,
                "A clean state enabled or hid the edge view by default.");
            ContextMenu menu = Menu(window);
            window.Interaction.UpdateExpansion(0, false, true, true);
            window.Interaction.UpdateExpansion(1, false, false, false);
            int beforeMenu = host.SetCalls;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert(!window.Interaction.IsAnimating && host.SetCalls == beforeMenu,
                "Opening an ordinary menu snapped a paused growth animation to its final size.");
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, menu));
            window.Interaction.UpdateExpansion(1, false, true, true);
            MenuItem edge = menu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, "edge-auto-hide"));
            Assert(!edge.IsChecked && edge.IsCheckable, "The optional mode was not exposed as an unchecked menu item.");
            edge.IsChecked = true; Click(edge);
            Assert(window.CurrentState.Settings.EdgeAutoHide && store.Load().Settings.EdgeAutoHide,
                "The real menu did not persist the enabled mode.");
            int beforeFailure = host.SetCalls;
            using (var locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                edge.IsChecked = false; Click(edge);
                Assert(edge.IsChecked && window.CurrentState.Settings.EdgeAutoHide && window.Interaction.IsEdgeDocked,
                    "A failed preference save changed the checkmark or live docking mode.");
                Assert(host.SetCalls == beforeFailure && window.Panel.StatusText.Visibility == Visibility.Visible,
                    "A failed save moved the panel or did not explain that the setting was retained.");
            }
            Assert(!window.Interaction.IsEdgeHidden, "Enabling edge mode immediately hid the panel.");
            Leave(window); window.Interaction.PollEdgeAutoHide();
            clock = clock.AddMilliseconds(799); window.Interaction.PollEdgeAutoHide();
            Assert(!window.Interaction.IsEdgeHidden, "The full coordinator hid before the leave delay.");
            clock = clock.AddMilliseconds(1); window.Interaction.PollEdgeAutoHide();
            Assert(window.Interaction.IsEdgeHidden && window.Panel.Surface.Child == window.Panel.EdgeButton && host.Bounds.Width == 48,
                "Controller state, capsule content and native-adapter bounds disagree.");
            Assert(window.Panel.EdgeButton.ToolTip.ToString().Contains("未完成 3 项，其中逾期 1 项"),
                "The integrated capsule miscounted pending and overdue tasks.");
            window.SaveGeometry();
            WidgetSettings saved = store.Load().Settings;
            Assert(saved.Width == 430 && saved.Height == 475 && saved.Left.Value + saved.Width == host.Area.Right - 8,
                "Saving while hidden persisted the capsule size or its temporary left edge.");
            window.Interaction.UpdateExpansion(1, false, true, true);
            Assert(window.Interaction.IsEdgeHidden && host.Bounds.Width == 48, "A deadline refresh automatically revealed the capsule.");
            RenderPanel(window, outputDirectory, "edge-capsule", 48, 152);
            Click(window.Panel.EdgeButton);
            Assert(!window.Interaction.IsEdgeHidden && window.Panel.Surface.Child == window.Panel.Root && host.Bounds.Width == 800,
                "Clicking the capsule did not restore the current urgency and full content.");
            clock = clock.AddSeconds(3); window.Interaction.PollEdgeAutoHide();
            Assert(!window.Interaction.IsEdgeHidden, "The explicit reveal disappeared without another leave event.");
            RenderPanel(window, outputDirectory, "edge-expanded", 430, 560);
            Leave(window); window.Interaction.PollEdgeAutoHide();
            sharedInteraction.EditorOpen = true;
            clock = clock.AddSeconds(2); window.Interaction.PollEdgeAutoHide();
            Assert(!window.Interaction.IsEdgeHidden, "An active editor did not block automatic hiding.");
            sharedInteraction.EditorOpen = false; window.Interaction.PollEdgeAutoHide();
            clock = clock.AddMilliseconds(800); window.Interaction.PollEdgeAutoHide();
            Assert(window.Interaction.IsEdgeHidden, "Auto-hide did not resume after editing finished.");
            // Exercise the real tracked-menu event without creating a native popup.
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert(!window.Interaction.IsEdgeHidden, "A menu opened on the capsule without restoring its panel.");
            Leave(window); window.Interaction.PollEdgeAutoHide(); clock = clock.AddSeconds(2); window.Interaction.PollEdgeAutoHide();
            Assert(!window.Interaction.IsEdgeHidden, "An open menu did not block automatic hiding.");
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, menu));
            window.Interaction.PollEdgeAutoHide(); clock = clock.AddMilliseconds(800); window.Interaction.PollEdgeAutoHide();
            Assert(window.Interaction.IsEdgeHidden, "Closing the menu did not permit hiding again.");
            MenuItem reset = menu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Header, "恢复右上角位置"));
            Click(reset); clock = clock.AddSeconds(3); window.Interaction.PollEdgeAutoHide();
            Assert(!window.Interaction.IsEdgeHidden && window.Panel.Surface.Child == window.Panel.Root,
                "The reset menu did not provide a stable way to find the panel.");
            Leave(window); window.Interaction.PollEdgeAutoHide(); clock = clock.AddMilliseconds(800); window.Interaction.PollEdgeAutoHide();
            Assert(window.TrySetEdgeAutoHide(false) && !window.Interaction.IsEdgeHidden && !store.Load().Settings.EdgeAutoHide,
                "Disabling edge mode failed to restore the panel and save the disabled preference.");
            Assert(store.Load().Items.Count == initial.Items.Count && store.Load().Items.Select(item => item.Id).SequenceEqual(initial.Items.Select(item => item.Id)),
                "Display operations changed stored task identity or removed tasks.");
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero && !window.Topmost,
                "Isolated UI checks displayed or made a native window topmost.");
            string savedBeforeLateCallback = File.ReadAllText(store.DataFilePath);
            window.HostLost = true;
            Assert(!window.TrySetEdgeAutoHide(true) && File.ReadAllText(store.DataFilePath) == savedBeforeLateCallback,
                "A callback on a lost host changed the saved preference.");
            window.HostLost = false;
            int calls = host.SetCalls; window.Dispose();
            Assert(!window.TrySetEdgeAutoHide(true) && File.ReadAllText(store.DataFilePath) == savedBeforeLateCallback,
                "A late callback changed saved settings after window disposal.");
            clock = clock.AddSeconds(10); window.Interaction.PollEdgeAutoHide();
            Assert(host.SetCalls == calls && !window.Interaction.IsEdgeTimerRunning, "Disposal left a late edge timer able to move the window.");
        }
        finally { window.Close(); }
        RenderTimePicker(outputDirectory, now);
        Console.WriteLine("PASS: " + assertions + " integrated edge UI/persistence assertions; real controls, fake desktop and clock, no native windows or startup changes.");
    }

    private static TodoState Fixture(DateTime now)
    {
        var state = new TodoState(); state.Settings.Left = 1146; state.Settings.Top = 18;
        state.Items.Add(Item("edge-overdue", "整理研究数据", now.AddHours(-1), now, TaskPriority.High));
        state.Items.Add(Item("edge-urgent", "向李老师汇报研究进展", now.AddHours(2), now, TaskPriority.Normal));
        state.Items.Add(Item("edge-later", "修改论文第二章", now.AddDays(4), now, TaskPriority.Low));
        return state;
    }

    private static TodoItem Item(string id, string text, DateTime due, DateTime now, TaskPriority priority)
    { return new TodoItem { Id = id, Text = text, DueDate = due, HasTime = true, CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2), Priority = priority }; }

    private static ContextMenu Menu(MainWindow window)
    { return (ContextMenu)typeof(MainWindow).GetMethod("BuildMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null); }
    private static void Leave(MainWindow window)
    {
        window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
    }
    private static void Click(MenuItem item) { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
    private static void Click(ButtonBase button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button)); }
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }

    private static void RenderPanel(MainWindow window, string directory, string name, double width, double height)
    {
        Border surface = window.Panel.Surface;
        window.Content = null;
        TextElement.SetFontFamily(surface, window.FontFamily);
        var canvas = new Grid { Width = width, Height = height, Background = Theme.Brush("#A8CDDA") };
        canvas.Children.Add(surface);
        try { Render(canvas, width, height, Path.Combine(directory, name + ".png")); }
        finally { canvas.Children.Remove(surface); window.Content = surface; }
    }

    private static void RenderTimePicker(string outputDirectory, DateTime now)
    {
        TodoItem item = Item("picker", "向李老师汇报研究进展", now.Date.AddDays(1).AddHours(17).AddMinutes(23), now, TaskPriority.Normal);
        item.Notes = "整理本周实验结果\n准备下一阶段研究计划";
        var editor = new TaskEditor(item);
        try
        {
            var content = (FrameworkElement)editor.Content; editor.Content = null;
            TextElement.SetFontFamily(content, editor.FontFamily); TextElement.SetFontSize(content, editor.FontSize);
            var canvas = new Grid { Background = editor.Background }; canvas.Children.Add(content);
            Render(canvas, 460, 690, Path.Combine(outputDirectory, "time-picker.png"));
            Assert(new WindowInteropHelper(editor).Handle == IntPtr.Zero, "Rendering the time picker created a native window.");
        }
        finally { editor.Close(); }
    }

    private static void Render(FrameworkElement canvas, double width, double height, string path)
    {
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); canvas.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(width * 1.5), (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
        image.Render(canvas); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path)) encoder.Save(file);
    }

    private sealed class FakeHost : IPanelHost
    {
        internal readonly Rect Area = new Rect(0, 0, 1600, 1000);
        internal Rect Bounds = new Rect(1146, 18, 430, 475);
        internal int SetCalls;
        public Rect GetBounds() { return Bounds; }
        public void SetBounds(Rect bounds) { SetCalls++; Bounds = bounds; }
        public Rect WorkArea() { return Area; }
    }

    private sealed class NoStartup : IStartupManager
    {
        public StartupRegistration Inspect() { return new StartupRegistration(false, false, "", "", false); }
        public void Enable() { throw new InvalidOperationException("No startup changes in isolated edge tests."); }
        public void Disable() { throw new InvalidOperationException("No startup changes in isolated edge tests."); }
    }
}
