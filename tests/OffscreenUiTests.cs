using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CornerTodo;

internal static class OffscreenUiTests
{
    private static readonly StringBuilder Report = new StringBuilder();
    private static string outputDirectory;
    private static int assertions;
    private const double Scale = 1.5;
    private const double Dpi = 144;

    [STAThread]
    public static int Main(string[] args)
    {
        Application application = null;
        try
        {
            outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual-qa"));
            Directory.CreateDirectory(outputDirectory);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Report.AppendLine("WPF offscreen UI validation at 144 dpi (150%): " + DateTime.Now.ToString("O"));
            Report.AppendLine("No Window.Show, native desktop attachment, or production data directory is used.");
            DateTime now = DateTime.Now;
            RenderScenario("normal", Scenario(now, false, false), now);
            RenderScenario("urgent", Scenario(now, true, false), now);
            RenderScenario("overdue", Scenario(now, true, true), now);
            ValidateLegacyUtcEditor();
            Report.AppendLine("PASS: " + assertions + " assertions across 3 rendered scenarios.");
            Report.AppendLine("Scope: control tree, layout, typography, counts, completed decoration, settings state and render pixels.");
            Report.AppendLine("Live desktop z-order, animations and native input are intentionally outside this offscreen check.");
            File.WriteAllText(Path.Combine(outputDirectory, "ui-validation.txt"), Report.ToString(), new UTF8Encoding(false));
            Console.Write(Report.ToString());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            if (outputDirectory != null)
                File.WriteAllText(Path.Combine(outputDirectory, "ui-validation-failure.txt"),
                    Report.ToString() + Environment.NewLine + error, new UTF8Encoding(false));
            return 1;
        }
        finally
        {
            if (application != null) application.Shutdown();
        }
    }

    private static TodoState Scenario(DateTime now, bool urgent, bool overdue)
    {
        TodoState state = new TodoState();
        state.Settings.Width = 430; state.Settings.Height = 475;
        state.Settings.AutoExpand = true;
        state.Items.Add(Item("normal-one", "向李老师汇报研究进展", now.AddDays(2), now, false));
        state.Items.Add(Item("normal-two", "整理实验数据并核对下周测量方案", now.AddDays(3), now, false));
        if (urgent) state.Items.Add(Item("urgent", "与王同学确认下午的实验方案", now.AddHours(2), now.AddHours(-22), false));
        if (overdue) state.Items.Add(Item("overdue", "给张老师发送论文修改稿", now.AddHours(-3), now.AddDays(-2), false));
        state.Items.Add(Item("completed", "完成阶段实验记录归档", now.AddHours(-6), now.AddDays(-2), true));
        return state;
    }

    private static TodoItem Item(string id, string text, DateTime deadline, DateTime scheduled, bool complete)
    {
        return new TodoItem { Id = id, Text = text, DueDate = deadline, HasTime = true,
            CreatedAt = scheduled, DeadlineSetAt = scheduled, IsCompleted = complete,
            CompletedAt = complete ? (DateTime?)deadline.AddHours(-1) : null };
    }

    private static void RenderScenario(string name, TodoState state, DateTime now)
    {
        string isolatedData = Path.Combine(outputDirectory, "isolated-data", name);
        TodoStore store = new TodoStore(isolatedData);
        MainWindow window = new MainWindow(store, state, true, true);
        try
        {
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "An unshown window unexpectedly has an HWND.");
            Border surface = Field<Border>(window, "surface");
            window.Content = null;
            // Detachment removes inherited Window properties; reproduce those
            // properties explicitly so these are the actual production fonts.
            TextElement.SetFontFamily(surface, window.FontFamily);
            TextElement.SetForeground(surface, window.Foreground);
            surface.Language = window.Language;
            surface.UseLayoutRounding = window.UseLayoutRounding;
            surface.SnapsToDevicePixels = window.SnapsToDevicePixels;

            PanelBounds workArea = new PanelBounds(0, 0, 2560 / Scale, 1528 / Scale);
            double progress = TodoRules.Expansion(state, now);
            PanelBounds target = GrowthLayout.Target(workArea, workArea.Right - 24, 18,
                state.Settings.Width, state.Settings.Height, progress, false);
            Grid canvas = new Grid { Width = target.Width, Height = target.Height,
                Background = new LinearGradientBrush(Color.FromRgb(183, 219, 226), Color.FromRgb(136, 190, 201), 90) };
            canvas.Children.Add(surface);
            canvas.Measure(new Size(target.Width, target.Height));
            canvas.Arrange(new Rect(0, 0, target.Width, target.Height));
            canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            canvas.UpdateLayout();

            ValidateControls(window, state, surface, now);
            ScrollViewer scroll = Field<ScrollViewer>(window, "scroll");
            Assert(scroll.ExtentHeight <= scroll.ViewportHeight + 0.5,
                name + ": these sample tasks should fit without clipping or scrolling.");

            int pixelWidth = (int)Math.Ceiling(target.Width * Scale);
            int pixelHeight = (int)Math.Ceiling(target.Height * Scale);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, Dpi, Dpi, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            Assert(HasTextPixels(bitmap), name + ": rendered output lacks non-background text pixels.");
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string imagePath = Path.Combine(outputDirectory, name + ".png");
            using (FileStream output = File.Create(imagePath)) encoder.Save(output);
            if (name == "normal") Assert(pixelWidth == 645 && pixelHeight == 713, "Normal dimensions should be 645x713 physical pixels.");
            if (name == "overdue") Assert(pixelWidth == 1280 && pixelHeight == 1100, "Overdue dimensions should be the physical maximum.");
            Report.AppendLine(name + ": " + pixelWidth + "x" + pixelHeight + " px; " +
                target.Width.ToString("0.###", CultureInfo.InvariantCulture) + "x" +
                target.Height.ToString("0.###", CultureInfo.InvariantCulture) + " DIP; progress=" +
                progress.ToString("0.###", CultureInfo.InvariantCulture) + "; extent=" +
                scroll.ExtentHeight.ToString("0.###", CultureInfo.InvariantCulture) + "; viewport=" +
                scroll.ViewportHeight.ToString("0.###", CultureInfo.InvariantCulture));
            ValidateMenuAndCollapsedState(window, state, now);
            ValidateBaselineSave(window, target);
            Assert(!Directory.Exists(isolatedData), "Preview UI unexpectedly wrote data files.");
            Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "Offscreen checks unexpectedly created an HWND.");
        }
        finally { window.Close(); }
    }

    private static void ValidateControls(MainWindow window, TodoState state, Border surface, DateTime now)
    {
        List<TextBlock> texts = Descendants(surface).OfType<TextBlock>().ToList();
        TextBlock title = texts.Single(t => t.Text == "待办清单");
        Near(25, title.FontSize, "Title font size");
        foreach (TodoItem item in state.Items)
        {
            TextBlock text = texts.Single(t => t.Text == item.Text);
            double expectedSize = item.Id == "overdue" ? 21 : item.Id == "urgent" ? 19 : 15;
            Near(expectedSize, text.FontSize, item.Id + " task font size");
            Assert(text.FontFamily.Source == window.FontFamily.Source, item.Id + ": Window font was not preserved.");
            Assert(text.ActualHeight > 0 && text.ActualWidth > 0, item.Id + ": task text has zero layout dimensions.");
            if (item.IsCompleted)
                Assert(text.TextDecorations != null && text.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough),
                    "Completed task does not have a strikethrough.");
            else
                Assert(text.TextDecorations == null || text.TextDecorations.Count == 0, "Pending task was struck through.");
            if (item.Id == "urgent" || item.Id == "overdue")
                Assert(text.FontWeight == FontWeights.SemiBold, item.Id + ": highlighted task is not semibold.");
            Grid row = Descendants(surface).OfType<Grid>().Single(g => Object.Equals(g.Tag, item.Id));
            TextBlock deadline = Descendants(row).OfType<TextBlock>().Single(t => t != text);
            Near(item.Id == "urgent" || item.Id == "overdue" ? 13 : 12, deadline.FontSize, item.Id + " deadline font size");
            if (item.Id == "urgent") Assert(deadline.Text.Contains("剩余"), "Urgent task lacks its remaining-time label.");
            if (item.Id == "overdue") Assert(deadline.Text.Contains("已逾期"), "Overdue task lacks its elapsed-time label.");
            CheckBox check = Descendants(row).OfType<CheckBox>().Single();
            Assert(check.IsChecked == item.IsCompleted, item.Id + ": completion checkbox has the wrong state.");
        }
        int pending = state.Items.Count(t => !t.IsCompleted);
        Assert(texts.Any(t => t.Text == "未完成  " + pending), "Pending section count is incorrect.");
        string summary = "逾期 " + state.Items.Count(t => t.Id == "overdue") + " 项 · 6小时内 " +
            state.Items.Count(t => t.Id == "urgent") + " 项";
        Assert(Field<TextBlock>(window, "attentionText").Text == summary, "Urgency summary is incorrect.");
        Assert(Descendants(surface).OfType<Button>().Any(b => (b.Content as string ?? "").Contains("已完成  1")),
            "Completed section count is incorrect.");
        Report.AppendLine("  typography: normal/completed=15, urgent=19, overdue=21; deadlines=12/13; " + summary);
    }

    private static void ValidateMenuAndCollapsedState(MainWindow window, TodoState state, DateTime now)
    {
        ContextMenu menu = (ContextMenu)Invoke(window, "BuildMenu");
        MenuItem automatic = menu.Items.OfType<MenuItem>().Single(i => Object.Equals(i.Header, "随截止时间扩大"));
        Assert(automatic.IsCheckable && automatic.IsChecked, "Automatic-growth menu is not initially checked.");
        automatic.IsChecked = false;
        automatic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, automatic));
        Assert(!window.CurrentState.Settings.AutoExpand, "The automatic-growth switch did not update state.");
        Assert(Field<Thumb>(window, "grip").IsEnabled, "Manual sizing must be available when automatic growth is off.");
        automatic.IsChecked = true;
        automatic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, automatic));
        Assert(window.CurrentState.Settings.AutoExpand, "Automatic growth could not be re-enabled.");
        Assert(Field<Thumb>(window, "grip").IsEnabled == (TodoRules.Expansion(state, now) <= 0.00001),
            "Sizing-grip availability does not reflect attention-driven growth.");
        Invoke(window, "TogglePanel");
        Assert(window.CurrentState.Settings.Collapsed, "Collapsed state was not saved in the UI model.");
        Assert(Field<ScrollViewer>(window, "scroll").Visibility == Visibility.Collapsed, "Collapsed panel still displays its task list.");
        Assert(Field<Button>(window, "add").Visibility == Visibility.Collapsed, "Collapsed panel still displays its add button.");
        Assert(Field<TextBlock>(window, "dateText").Text.Contains("6小时内"), "Collapsed header lacks its attention summary.");
        Invoke(window, "TogglePanel");
        Assert(!window.CurrentState.Settings.Collapsed, "Collapsed panel could not be expanded.");
        Report.AppendLine("  interaction state: auto off/on and collapse/expand passed without showing UI.");
    }

    private static void ValidateBaselineSave(MainWindow window, PanelBounds expanded)
    {
        // SaveGeometry should persist the user baseline, never this expanded
        // render's current Width/Height. preview=true prevents all store writes.
        SetField(window, "baseWidth", 430.0); SetField(window, "baseHeight", 475.0);
        SetField(window, "anchorRight", 1600.0); SetField(window, "anchorTop", 18.0);
        window.Width = expanded.Width; window.Height = expanded.Height;
        SetField(window, "ready", true);
        window.SaveGeometry();
        SetField(window, "ready", false);
        WidgetSettings saved = window.CurrentState.Settings;
        Near(430, saved.Width, "Saved baseline width"); Near(475, saved.Height, "Saved baseline height");
        Near(1170, saved.Left.Value, "Saved baseline left"); Near(18, saved.Top.Value, "Saved baseline top");
        Report.AppendLine("  baseline save: 430x475 and right/top anchor retained; expanded geometry was not persisted.");
    }

    private static void ValidateLegacyUtcEditor()
    {
        TodoItem original = new TodoItem {
            Id = "legacy-utc-with-seconds", Text = "旧版 UTC 事项",
            DueDate = new DateTime(2026, 10, 21, 5, 47, 29, DateTimeKind.Utc).AddTicks(1234),
            HasTime = true, CreatedAt = new DateTime(2026, 10, 19, 7, 10, 11, DateTimeKind.Utc),
            DeadlineSetAt = new DateTime(2026, 10, 19, 8, 11, 12, DateTimeKind.Utc).AddTicks(5678)
        };
        DateTime local = original.DueDate.ToLocalTime();
        TaskEditor textOnly = new TaskEditor(original);
        try
        {
            Assert(Field<DatePicker>(textOnly, "date").SelectedDate == local.Date,
                "Legacy UTC deadline does not display the local calendar date.");
            Assert(Field<TextBox>(textOnly, "time").Text == local.ToString("HH:mm"),
                "Legacy UTC deadline does not display the local clock time.");
            Field<TextBox>(textOnly, "taskText").Text = "仅修改文字，保留原截止精度";
            SaveUnshownEditor(textOnly);
            TodoItem saved = textOnly.Result;
            Assert(saved != null && saved.Text == "仅修改文字，保留原截止精度", "Text-only editor save failed.");
            Assert(saved.DueDate.Ticks == original.DueDate.Ticks, "Text-only edit changed seconds or subsecond precision.");
            Assert(saved.DueDate.Kind == DateTimeKind.Utc, "Text-only edit changed the UTC deadline kind.");
            Assert(saved.DeadlineSetAt.HasValue &&
                saved.DeadlineSetAt.Value.Ticks == original.DeadlineSetAt.Value.Ticks &&
                saved.DeadlineSetAt.Value.Kind == original.DeadlineSetAt.Value.Kind,
                "Text-only edit restarted the original scheduling interval.");
            Assert(saved.Id == original.Id && saved.CreatedAt == original.CreatedAt,
                "Text-only edit changed task identity or creation time.");
            Assert(new WindowInteropHelper(textOnly).Handle == IntPtr.Zero,
                "Text-only editor regression unexpectedly created an HWND.");
        }
        finally { textOnly.Close(); }

        TaskEditor deadlineEdit = new TaskEditor(original);
        try
        {
            Field<TextBox>(deadlineEdit, "time").Text = local.AddMinutes(1).ToString("HH:mm");
            DateTime before = DateTime.Now;
            SaveUnshownEditor(deadlineEdit);
            DateTime after = DateTime.Now;
            TodoItem saved = deadlineEdit.Result;
            DateTime expected = local.Date.AddHours(local.Hour).AddMinutes(local.Minute + 1);
            Assert(saved != null && TodoRules.Deadline(saved).Ticks == expected.Ticks,
                "A real minute edit did not update the effective deadline.");
            Assert(saved.DueDate.Second == 0 && saved.DueDate.Ticks % TimeSpan.TicksPerSecond == 0,
                "A changed minute should use the minute-precision value supplied by the editor.");
            Assert(saved.DeadlineSetAt.HasValue && saved.DeadlineSetAt.Value >= before &&
                saved.DeadlineSetAt.Value <= after && saved.DeadlineSetAt.Value.Kind == DateTimeKind.Local,
                "A real deadline edit did not reset DeadlineSetAt to the save time.");
            Assert(saved.DeadlineSetAt.Value.Ticks != original.DeadlineSetAt.Value.Ticks,
                "A real deadline edit kept the old scheduling interval.");
            Assert(original.DueDate.Second == 29 && original.DueDate.Kind == DateTimeKind.Utc,
                "Saving the editor mutated the original task.");
            Assert(new WindowInteropHelper(deadlineEdit).Handle == IntPtr.Zero,
                "Deadline editor regression unexpectedly created an HWND.");
        }
        finally { deadlineEdit.Close(); }
        Report.AppendLine("editor UTC regression: local date/time display, text-only tick/Kind/DeadlineSetAt preservation, and genuine minute-change scheduling reset passed.");
    }

    private static void SaveUnshownEditor(TaskEditor editor)
    {
        try { Invoke(editor, "Save"); }
        catch (TargetInvocationException error)
        {
            // Save assigns Result before setting DialogResult. An unshown test
            // window is deliberately outside ShowDialog; allow only that exact
            // framework setter failure, never a validation or parsing failure.
            Exception inner = error.InnerException;
            bool expected = inner is InvalidOperationException && editor.Result != null &&
                inner.StackTrace != null && inner.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) >= 0;
            if (!expected) throw;
        }
        Assert(editor.Result != null, "Editor validation returned no result: " + Field<TextBlock>(editor, "error").Text);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }

    private static bool HasTextPixels(RenderTargetBitmap bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int dark = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 200 && pixels[i] < 130 && pixels[i + 1] < 130 && pixels[i + 2] < 130) dark++;
        return dark > 400;
    }

    private static T Field<T>(object instance, string name)
    {
        return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
    private static void SetField(object instance, string name, object value)
    {
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
    private static object Invoke(object instance, string name)
    {
        return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    }
    private static void Near(double expected, double actual, string message)
    {
        Assert(Math.Abs(expected - actual) < 0.000001, message + ": expected " + expected + ", got " + actual);
    }
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
