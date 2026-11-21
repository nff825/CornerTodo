using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CornerTodo;

internal static class DeadlineCalendarUiTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Local);

    internal static void Run(string outputDirectory)
    {
        var state = new TodoState();
        var report = Item("report", "向李老师汇报研究进展", Now.Date.AddHours(15));
        var data = Item("data", "整理实验数据", Now.Date.AddHours(17).AddMinutes(30));
        var summary = Item("summary", "提交本周小结", Now.Date);
        summary.HasTime = false;
        var undated = Item("undated", "整理参考文献", DateTime.MinValue);
        undated.HasDeadline = false; undated.HasTime = false;
        state.Items.AddRange(new[] { report, data, summary, undated,
            Item("late", "提交报销", Now.AddDays(-1)),
            Item("outline", "提交论文提纲", Now.AddDays(1)),
            Item("notes", "归档会议纪要", Now.AddDays(4)),
            Item("proposal", "提交开题报告", Now.AddDays(8)) });

        DeadlineCalendarWindow window = null;
        bool acceptCompletion = false;
        string edited = null;
        window = new DeadlineCalendarWindow(state, Now,
            delegate(TodoItem item) { edited = item.Id; },
            delegate(TodoItem item, bool completed) {
                if (!acceptCompletion) return false;
                state.Items.Single(candidate => candidate.Id == item.Id).IsCompleted = completed;
                window.Refresh(state, Now);
                return true;
            });
        try {
            object root = window.Content;
            Check(!window.Topmost, "Desktop mode opened an always-on-top calendar.");
            state.Settings.AlwaysOnTop = true;
            window.Refresh(state, Now);
            Check(window.Topmost, "A floating panel can obscure a calendar in the normal z-order group.");
            state.Settings.AlwaysOnTop = false;
            window.Refresh(state, Now);
            Check(!window.Topmost, "Returning to desktop mode left the calendar always on top.");
            var day = Find<Button>(root, "CalendarDay_20261112");
            Check(Elements<TextBlock>(day).Any(text => text.Text == "+1 项"), "A date with three tasks did not show the overflow count.");
            Check(Find<TextBlock>(root, "CalendarDetailTitle").Text.Contains("11月12日"), "The window did not open on today's date.");
            Check(Find<CheckBox>(root, "CalendarComplete_summary") != null, "The third task was missing from the selected day's detail.");

            Click(Find<Button>(root, "CalendarNextMonth"));
            Check(Find<TextBlock>(root, "CalendarMonthTitle").Text == "2026年12月", "Next month did not change the calendar.");
            Click(Find<Button>(root, "CalendarToday"));
            Check(Find<TextBlock>(root, "CalendarMonthTitle").Text == "2026年11月", "Today did not return to the current month.");
            Click(Find<Button>(root, "CalendarDay_20261113"));
            Check(Find<CheckBox>(root, "CalendarComplete_outline") != null, "Selecting a date did not update the details.");

            Click(Find<Button>(root, "CalendarOverdue"));
            Check(Find<CheckBox>(root, "CalendarComplete_late") != null, "The overdue entry did not open its task list.");
            Click(Find<Button>(root, "CalendarUnscheduled"));
            Check(Find<CheckBox>(root, "CalendarComplete_undated") != null, "The undated entry did not open its task list.");
            window.Refresh(state, Now.AddSeconds(10));
            Check(Find<TextBlock>(root, "CalendarDetailTitle").Text == "未设截止", "A timed refresh discarded the current view.");

            Click(Find<Button>(root, "CalendarToday"));
            Click(Find<Button>(root, "CalendarEdit_report"));
            Check(edited == "report", "Edit was routed to the wrong task.");
            var complete = Find<CheckBox>(root, "CalendarComplete_report");
            Toggle(complete, true);
            Check(complete.IsChecked == false && Find<TextBlock>(root, "CalendarStatus").Visibility == Visibility.Visible,
                "A failed save left an optimistic completion state or hid its error.");
            acceptCompletion = true;
            Toggle(complete, true);
            Check(Find<CheckBox>(root, "CalendarComplete_report") == null, "A completed task remained in the default filter.");
            Find<CheckBox>(root, "CalendarIncludeCompleted").IsChecked = true;
            complete = Find<CheckBox>(root, "CalendarComplete_report");
            Check(complete != null && complete.IsChecked == true, "The completed filter did not restore the completed task.");
            Toggle(complete, false);
            Check(!report.IsCompleted, "The completed task could not be restored.");
            Find<CheckBox>(root, "CalendarIncludeCompleted").IsChecked = false;

            data.DueDate = Now.AddDays(1);
            window.Refresh(state, Now);
            Check(Find<CheckBox>(root, "CalendarComplete_data") == null, "An edited deadline did not leave its previous date.");
            Click(Find<Button>(root, "CalendarDay_20261113"));
            Check(Find<CheckBox>(root, "CalendarComplete_data") != null, "An edited deadline did not reach its new date.");
            data.DueDate = Now.Date.AddHours(17).AddMinutes(30);
            window.Refresh(state, Now);
            Click(Find<Button>(root, "CalendarToday"));
            if (!String.IsNullOrWhiteSpace(outputDirectory)) Render(window, outputDirectory);
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: deadline calendar navigation, saved updates, rollback, filters and offscreen layout.");
    }

    private static TodoItem Item(string id, string title, DateTime due)
    {
        return new TodoItem { Id = id, Text = title, DueDate = due, HasTime = true, CreatedAt = Now.AddDays(-2) };
    }

    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
    private static void Toggle(CheckBox box, bool value)
    {
        box.IsChecked = value;
        box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent, box));
    }

    private static T Find<T>(object root, string id) where T : DependencyObject
    {
        return Elements<T>(root).SingleOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    }

    private static IEnumerable<T> Elements<T>(object root) where T : DependencyObject
    {
        T match = root as T;
        if (match != null) yield return match;
        DependencyObject dependency = root as DependencyObject;
        if (dependency == null) yield break;
        foreach (object child in LogicalTreeHelper.GetChildren(dependency))
            foreach (T element in Elements<T>(child)) yield return element;
    }

    private static void Render(DeadlineCalendarWindow window, string outputDirectory)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Child = content, Width = window.Width, Height = window.Height - 40,
            Background = window.Background, UseLayoutRounding = true, SnapsToDevicePixels = true };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);
        var size = new Size(host.Width, host.Height);
        host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
        Check(Find<Button>(host, "CalendarDay_20261112").ActualHeight >= 55, "A normal-sized calendar compressed its task cells.");
        Check(Find<Button>(host, "CalendarUnscheduled").ActualHeight > 0, "The undated entry is missing from the layout.");
        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(host);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(outputDirectory);
        using (FileStream file = File.Create(Path.Combine(outputDirectory, "deadline-calendar.png"))) png.Save(file);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
