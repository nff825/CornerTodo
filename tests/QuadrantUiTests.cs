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

internal static class QuadrantUiTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Local);

    internal static void Run(string outputDirectory)
    {
        var state = Scenario();
        int added = 0;
        string edited = null;
        bool saveCompletion = false, saveRange = false, failEdit = false;
        QuadrantWindow window = null;
        window = new QuadrantWindow(state, Now, delegate { added++; },
            delegate(TodoItem item) {
                if (failEdit) throw new IOException("示例编辑失败");
                edited = item.Id;
            },
            delegate(TodoItem item, bool completed) {
                if (!saveCompletion) return false;
                TodoItem current = state.Items.Single(candidate => candidate.Id == item.Id);
                current.IsCompleted = completed; current.CompletedAt = completed ? (DateTime?)Now : null;
                window.Refresh(state, Now);
                return true;
            },
            delegate(int hours) {
                if (!saveRange) return false;
                state.Settings.QuadrantUrgentHours = hours;
                window.Refresh(state, Now);
                return true;
            });
        try {
            object root = window.Content;
            Check(window.Owner == null && window.ShowInTaskbar, "The independent quadrant window is missing its own taskbar entry.");
            Check(!window.Topmost, "Desktop mode opened a topmost quadrant window.");
            Check(Find<TextBlock>(root, "QuadrantSummary").Text == "未完成 9 项", "The top count did not include all nine pending tasks.");
            ExpectRows(root, 0, "late", "proposal", "report");
            ExpectRows(root, 1, "paper", "plan");
            ExpectRows(root, 2, "expense", "room");
            ExpectRows(root, 3, "desktop", "wallpaper");
            Check(Find<TextBlock>(root, "QuadrantDeadline_plan").Text == "未设截止", "An important undated task did not remain visible.");
            Check(Find<TextBlock>(root, "QuadrantDeadline_late").Text.Contains("已逾期"), "The overdue row has no textual overdue status.");
            Check(!Elements<Border>(root).Any(border => Object.Equals(border.Tag, "priority")), "The quadrant rows repeated priority badges.");

            Click(Find<Button>(root, "QuadrantAdd"));
            Click(Find<Button>(root, "QuadrantEdit_report"));
            Check(added == 1 && edited == "report", "Add/edit requests were not forwarded to the correct callbacks.");
            failEdit = true;
            Click(Find<Button>(root, "QuadrantEdit_report"));
            Check(Find<TextBlock>(root, "QuadrantStatus").Text.Contains("示例编辑失败"), "An editor callback failure has no visible explanation.");
            failEdit = false;

            var range = Find<ComboBox>(root, "QuadrantUrgentHours");
            Check(range.Items.Cast<ComboBoxItem>().Select(option => (int)option.Tag).SequenceEqual(new[] { 6, 24, 72, 168 }),
                "The quadrant urgent range choices are incomplete.");
            var desktopRow = Find<Border>(root, "QuadrantRow_desktop");
            SelectRange(range, 72);
            Check((int)((ComboBoxItem)range.SelectedItem).Tag == 24 && state.Settings.QuadrantUrgentHours == 24,
                "A failed range save left an unsaved selection.");
            Check(Find<TextBlock>(root, "QuadrantStatus").Visibility == Visibility.Visible, "A failed range save did not show a status.");
            ExpectRows(root, 3, "desktop", "wallpaper");
            saveRange = true;
            SelectRange(range, 72);
            Check((int)((ComboBoxItem)range.SelectedItem).Tag == 72, "A successful urgent range save did not update its selector.");
            Check(Object.ReferenceEquals(desktopRow, Find<Border>(root, "QuadrantRow_desktop")), "A range change recreated a task row while moving it between quadrants.");
            Check(RowIds(root, 2).Contains("desktop") && !RowIds(root, 3).Contains("desktop"), "A wider urgent range did not regroup the affected task.");
            SelectRange(range, 24);

            TodoItem late = state.Items.Single(item => item.Id == "late");
            TodoItem proposal = state.Items.Single(item => item.Id == "proposal");
            late.Priority = TaskPriority.Low; proposal.Priority = TaskPriority.High;
            window.Refresh(state, Now);
            ExpectRows(root, 0, "proposal", "report", "late");
            late.Priority = proposal.Priority = TaskPriority.Normal;
            window.Refresh(state, Now);

            var completion = Find<CheckBox>(root, "QuadrantComplete_report");
            Toggle(completion, true);
            Check(completion.IsChecked == false && !state.Items.Single(item => item.Id == "report").IsCompleted,
                "A failed completion save left an optimistic checkbox or task state.");
            Check(Find<TextBlock>(root, "QuadrantStatus").Visibility == Visibility.Visible, "A failed completion save did not show a status.");
            saveCompletion = true;
            Toggle(completion, true);
            Check(Find<CheckBox>(root, "QuadrantComplete_report") == null, "The default filter continued to show a completed task.");
            Check(Find<TextBlock>(root, "QuadrantSummary").Text == "未完成 8 项", "Completing a task did not update the pending total.");
            var filter = Find<CheckBox>(root, "QuadrantIncludeCompleted");
            filter.IsChecked = true;
            completion = Find<CheckBox>(root, "QuadrantComplete_report");
            Check(completion != null && completion.IsChecked == true, "The completed filter did not expose a completed task.");
            Check(Find<TextBlock>(root, "QuadrantTitle_report").TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough),
                "The completed quadrant task lost its strikethrough.");
            Toggle(completion, false);
            Check(state.Items.All(item => !item.IsCompleted), "A completed quadrant task could not be restored.");
            filter.IsChecked = false;

            var completed = Item("completed-history", "已完成研究计划", Now.AddHours(48), true);
            completed.IsCompleted = true; completed.CompletedAt = Now;
            state.Items.Add(completed);
            filter.IsChecked = true;
            window.Refresh(state, Now);
            Check(RowIds(root, 1).Contains(completed.Id), "A completed task was not classified at completion time.");
            var completedRow = Find<Border>(root, "QuadrantRow_" + completed.Id);
            window.Refresh(state, Now.AddDays(10));
            Check(RowIds(root, 1).Contains(completed.Id) && Object.ReferenceEquals(completedRow, Find<Border>(root, "QuadrantRow_" + completed.Id)),
                "Advancing the clock moved or recreated historical completion classification.");
            window.Refresh(state, Now);
            filter.IsChecked = false; state.Items.Remove(completed); window.Refresh(state, Now);

            var unchanged = Find<Button>(root, "QuadrantEdit_report");
            state.Settings.AlwaysOnTop = true;
            window.Refresh(state, Now.AddMinutes(1));
            Check(window.Topmost && Object.ReferenceEquals(unchanged, Find<Button>(root, "QuadrantEdit_report")),
                "Refreshing the topmost preference recreated the current task controls.");
            state.Settings.AlwaysOnTop = false;
            window.Refresh(state, Now);
            Check(!window.Topmost, "Returning to desktop mode left the quadrant window topmost.");
            var preview = AppearanceSettings.CreateDark(); preview.TextScale = 1.5; preview.BackgroundTransparency = 100;
            window.Refresh(state, Now, preview);
            Check(((SolidColorBrush)window.Background).Color.A == 255, "The independent quadrant window inherited a transparent background.");
            Near(22.5, Find<TextBlock>(root, "QuadrantTitle_report").FontSize, "Task text did not follow the preview scale.");
            Near(18, Find<TextBlock>(root, "QuadrantDeadline_report").FontSize, "Deadline text did not follow the preview scale.");
            Check(state.Settings.Appearance.TextScale == 1 && Object.ReferenceEquals(unchanged, Find<Button>(root, "QuadrantEdit_report")),
                "A font preview committed settings or replaced unchanged controls.");
            window.Refresh(state, Now);
            Near(15, Find<TextBlock>(root, "QuadrantTitle_report").FontSize, "Cancelling preview did not restore task typography.");
            if (!String.IsNullOrWhiteSpace(outputDirectory)) Layout(window, window.Width, window.Height - 40, outputDirectory, "quadrant.png");

            for (int index = 0; index < 18; index++)
                state.Items.Add(Item("long-" + index, "核对实验数据、原始记录及图表说明，整理测量结果后提交研究小组讨论 " + index,
                    Now.AddHours(index % 4 + 1), true));
            window.Refresh(state, Now, preview);
            Layout(window, 620, 440, null, null);
            var firstScroll = Find<ScrollViewer>(root, "QuadrantItems_0");
            Check(firstScroll.ScrollableHeight > 0, "The individual quadrant did not scroll its long task list.");
            firstScroll.ScrollToVerticalOffset(80);
            Layout(window, 620, 440, null, null);
            double offset = firstScroll.VerticalOffset;
            var retained = Find<Button>(root, "QuadrantEdit_long-0");
            window.Refresh(state, Now.AddSeconds(1), preview);
            Layout(window, 620, 440, null, null);
            Near(offset, firstScroll.VerticalOffset, "A timed refresh reset a quadrant's scroll position.");
            Check(Object.ReferenceEquals(retained, Find<Button>(root, "QuadrantEdit_long-0")), "A timed refresh recreated a retained row.");
            var board = Find<ScrollViewer>(root, "QuadrantBoard");
            Check(board.ScrollableWidth > 0, "A narrow quadrant window clipped its four-column board without horizontal scrolling.");
            Layout(window, 435, 260, null, null);
            Check(board.ScrollableHeight > 0, "A short quadrant window clipped its board without vertical scrolling.");
            Check(new WindowInteropHelper(window).Handle == IntPtr.Zero, "Quadrant UI tests unexpectedly created a native window.");
            Check(state.Items.Count == 27, "Viewing the quadrant board created extra task occurrences.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: quadrant projection, sorting, actions, rollback, themes, completion history and retained scrolling.");
    }

    private static TodoState Scenario()
    {
        var state = new TodoState();
        state.Items.AddRange(new[] {
            Item("late", "补交实验申请", Now.AddDays(-1), true),
            Item("proposal", "提交开题报告", Now.AddHours(3), true),
            Item("report", "向李老师汇报研究进展", Now.AddHours(4), true),
            Item("paper", "完善论文第二章", Now.AddDays(7), true),
            Item("plan", "梳理下一阶段研究方案", null, true),
            Item("expense", "提交报销材料", Now.AddHours(2), false),
            Item("room", "确认会议室预约", Now.AddHours(5), false),
            Item("desktop", "整理桌面文件", Now.AddDays(2), false),
            Item("wallpaper", "更换桌面壁纸", null, false)
        });
        return state;
    }

    private static TodoItem Item(string id, string text, DateTime? due, bool important)
    {
        return new TodoItem { Id = id, Text = text, IsImportant = important, HasDeadline = due.HasValue,
            HasTime = due.HasValue, DueDate = due ?? DateTime.MinValue, CreatedAt = Now.AddDays(-3), Priority = TaskPriority.Normal };
    }

    private static string[] RowIds(object root, int quadrant)
    {
        var scroll = Find<ScrollViewer>(root, "QuadrantItems_" + quadrant);
        return ((StackPanel)scroll.Content).Children.OfType<Border>()
            .Select(row => AutomationProperties.GetAutomationId(row).Substring("QuadrantRow_".Length)).ToArray();
    }

    private static void ExpectRows(object root, int quadrant, params string[] ids)
    {
        Check(RowIds(root, quadrant).SequenceEqual(ids), "Wrong quadrant task membership or order for " + quadrant + ".");
    }

    private static void SelectRange(ComboBox combo, int hours)
    {
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(item => (int)item.Tag == hours);
    }

    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
    private static void Toggle(CheckBox box, bool value)
    {
        box.IsChecked = value; box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent, box));
    }

    private static T Find<T>(object root, string id) where T : DependencyObject
    {
        return Elements<T>(root).SingleOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    }

    private static IEnumerable<T> Elements<T>(object root) where T : DependencyObject
    {
        T element = root as T;
        if (element != null) yield return element;
        var dependency = root as DependencyObject;
        if (dependency == null) yield break;
        foreach (object child in LogicalTreeHelper.GetChildren(dependency))
            foreach (T nested in Elements<T>(child)) yield return nested;
    }

    private static void Layout(QuadrantWindow window, double width, double height, string directory, string filename)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Child = content, Width = width, Height = height,
            Background = window.Background, UseLayoutRounding = true, SnapsToDevicePixels = true };
        try {
            TextElement.SetFontFamily(host, window.FontFamily); TextElement.SetFontSize(host, window.FontSize);
            TextElement.SetForeground(host, window.Foreground);
            var size = new Size(width, height);
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            host.UpdateLayout();
            if (!String.IsNullOrWhiteSpace(filename)) {
                var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); image.Render(host);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(directory);
                using (FileStream stream = File.Create(Path.Combine(directory, filename))) encoder.Save(stream);
            }
        }
        finally { host.Child = null; window.Content = content; }
    }

    private static void Near(double expected, double actual, string message) { Check(Math.Abs(expected - actual) < 0.001, message); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
