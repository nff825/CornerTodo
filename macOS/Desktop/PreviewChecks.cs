using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using System.Globalization;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace CornerTodo.MacDesktop;

/// <summary>Auxiliary control checks on the build host; never evidence of native Mac execution.</summary>
internal static class PreviewChecks
{
    private static int assertions;

    internal static async Task RunAsync(TodoWindow window, TodoSession session, string? outputDirectory)
    {
        if (!Program.Options.Preview) throw new InvalidOperationException("UI checks require preview mode.");
        assertions = 0;
        Assert(window.IsVisible && !window.Topmost && window.ContentSurface.Bounds.Width > 0, "The non-topmost preview panel failed to appear.");
        Assert(session.Snapshot().Items.Count == 4 && session.Snapshot().Items.Count(item => !item.IsCompleted) == 3,
            "The preview did not use its isolated sample tasks.");
        Assert(!session.Snapshot().Settings.EdgeAutoHide, "The optional edge mode was enabled by default.");
        if (outputDirectory != null)
        {
            Directory.CreateDirectory(outputDirectory);
            SavePng(window, Path.Combine(outputDirectory, "panel.png"));
        }
        await ValidateTaskEditor(window);
        await ValidateNoDeadlinePanel(window, outputDirectory);
        await ValidateUnifiedPanel(window, outputDirectory);
        await ValidateAuxiliaryDialog(window);
        ValidateAppearance(window);
        await ValidateRealEditorFlow(window);
        await ValidateEditorLayouts(window, outputDirectory);
        await ValidateEdge(window, session, outputDirectory);
        Assert(window.SaveBeforeExit(), "The preview could not commit its baseline before exit.");
        if (outputDirectory != null)
        {
            File.WriteAllText(Path.Combine(outputDirectory, "auxiliary-ui-report.json"),
                System.Text.Json.JsonSerializer.Serialize(new {
                    Assertions = assertions, Host = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    NativeMacHost = OperatingSystem.IsMacOS(), NativeMacDesktopBehaviorVerified = false, SampleDataOnly = true,
                    Scope = "Real Avalonia controls, editor drafts, layout rendering and simulated pointer timing; not native mouse input."
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("PASS: " + assertions + " Avalonia auxiliary UI assertions.");
    }

    private static async Task ValidateTaskEditor(TodoWindow owner)
    {
        DateTime utc = DateTime.UtcNow.AddDays(2).AddTicks(1234);
        var source = new TodoItem { Id = "editor-fixture", Text = "原事项", Notes = "原备注", DueDate = utc,
            HasTime = true, Priority = TaskPriority.High, CreatedAt = utc.AddDays(-4), DeadlineSetAt = utc.AddDays(-3) };
        int saveCalls = 0;
        var editor = new TaskEditorWindow(source, _ => { saveCalls++; return false; });
        try
        {
            editor.Show(owner); await Task.Delay(100);
            editor.TaskText.Text = "修改文字"; editor.Notes.Text = "备注第一行\n第二行";
            var draft = editor.BuildDraft();
            Assert(draft.DueDate.Ticks == utc.Ticks && draft.DueDate.Kind == utc.Kind, "Text edits changed the original UTC deadline precision.");
            Assert(draft.Notes.Contains("第二行") && draft.Priority == TaskPriority.High, "Editor draft lost notes or priority.");
            foreach (var sample in new[] { ("00", "00"), ("23", "59"), ("17", "23") })
            {
                editor.Hour.Input.Text = sample.Item1; editor.Minute.Input.Text = sample.Item2;
                draft = editor.BuildDraft();
                Assert(draft.DueDate.Hour == int.Parse(sample.Item1) && draft.DueDate.Minute == int.Parse(sample.Item2), "An arbitrary valid minute was rejected.");
            }
            foreach (var sample in new[] { ("24", "00"), ("23", "60"), ("-1", "00"), ("", "23") })
            {
                editor.Hour.Input.Text = sample.Item1; editor.Minute.Input.Text = sample.Item2;
                ExpectInvalid(() => editor.BuildDraft(), "An invalid hour/minute was accepted.");
            }
            editor.Hour.Input.Text = "17"; editor.Minute.Input.Text = "23";
            var save = Descendants(editor).OfType<Button>().Single(button => Equals(button.Content, "保存"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(saveCalls == 1 && editor.TaskText.Text == "修改文字" && editor.Notes.Text!.Contains("第二行"), "A failed save lost its editor draft.");
            editor.UseTime.IsChecked = false;
            Assert(!editor.BuildDraft().HasTime && !editor.Hour.IsEnabled && !editor.Minute.IsEnabled, "Date-only mode retained an enabled time input.");
            editor.UseDeadline.IsChecked = false;
            Assert(editor.Date.GetLogicalParent() is Control fields && !fields.IsVisible,
                "Disabling a deadline did not hide its date and time field group.");
            editor.Hour.Input.Text = "invalid";
            draft = editor.BuildDraft();
            Assert(!draft.HasDeadline && draft.DueDate == DateTime.MinValue && !draft.HasTime && draft.Notes.Contains("第二行"),
                "A no-deadline draft validated hidden time inputs or lost its other fields.");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(saveCalls == 2 && editor.UseDeadline.IsChecked == false && editor.IsVisible,
                "A failed no-deadline save closed the editor or restored a fabricated deadline.");
            editor.UseDeadline.IsChecked = true; editor.Hour.Input.Text = "17";
            Assert(editor.Date.GetLogicalParent() is Control enabledFields && enabledFields.IsVisible && editor.BuildDraft().HasDeadline,
                "Re-enabling the deadline did not restore a usable date draft.");
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                editor.Date.Text = "November 2, 2026";
                Assert(editor.BuildDraft().DueDate.Date == new DateTime(2026, 11, 2), "An uncommitted English date input was ignored.");
                editor.Date.Text = "2026年11月3日";
                Assert(editor.BuildDraft().DueDate.Date == new DateTime(2026, 11, 3), "Chinese date input was rejected on an English locale.");
                // The native calendar may visibly restore its last valid value
                // when Text is invalid. Validate our parser without assuming a
                // particular native control's rollback event sequence.
                Assert(!TaskEditorWindow.TryParseEditorDate("not-a-date", out _), "The editor's input parser accepted an invalid date.");
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
            Assert(source.Text == "原事项" && source.Notes == "原备注" && source.DueDate == utc, "Editing mutated the original item before a successful save.");
        }
        finally { editor.Close(); }
        var noDeadlineItem = new TodoItem { Id = "editor-no-deadline", Text = "随手记下", Notes = "保留备注",
            HasDeadline = false, DueDate = DateTime.MinValue, CreatedAt = DateTime.Now.AddDays(-60) };
        var noDeadlineEditor = new TaskEditorWindow(noDeadlineItem, _ => false);
        try
        {
            noDeadlineEditor.Show(owner); await Task.Delay(60);
            Assert(noDeadlineEditor.UseDeadline.IsChecked == false && !noDeadlineEditor.BuildDraft().HasDeadline &&
                noDeadlineEditor.Date.SelectedDate == DateTime.Today,
                "Editing a stored no-deadline task did not retain its state or provide a usable optional date.");
            noDeadlineEditor.UseDeadline.IsChecked = true;
            Assert(noDeadlineEditor.BuildDraft().DueDate.Date == DateTime.Today,
                "Adding a deadline to an undated task reused its empty in-memory date.");
            Assert(!noDeadlineItem.HasDeadline && noDeadlineItem.DueDate == DateTime.MinValue,
                "Toggling an editor deadline mutated its source before saving.");
        }
        finally { noDeadlineEditor.Close(); }
    }

    private static async Task ValidateNoDeadlinePanel(TodoWindow owner, string? outputDirectory)
    {
        var item = new TodoItem { Id = "panel-no-deadline", Text = "整理实验数据", HasDeadline = false,
            DueDate = DateTime.MinValue, CreatedAt = DateTime.Now.AddDays(-60), Priority = TaskPriority.High };
        var state = new TodoState { Items = new List<TodoItem> { item } };
        string directory = Path.Combine(Program.Options.DataDirectory, "no-deadline-helper");
        var session = new TodoSession(new TodoStore(directory), state, true);
        var panel = new TodoWindow(session, directory, () => { }) { PreviewMode = true };
        try
        {
            panel.Show(owner); await Task.Delay(80);
            var due = Descendants(panel).OfType<TextBlock>().Single(text => text.Text == "未设截止");
            Assert(due.GetLogicalParent() is StackPanel content && !content.Children.OfType<Border>().Any(border => border.Height == 3),
                "An undated row retained a time-bar track in its visual content.");
            Assert(Descendants(panel).OfType<TextBlock>().Any(text => text.Text == "逾期 0 项 · 6小时内 0 项"),
                "An old undated task was counted as overdue or urgent.");
            Assert(Descendants(panel).OfType<TextBlock>().Single(text => text.Text == item.Text).FontSize == 15,
                "An undated high-priority task was enlarged as deadline urgency.");
            var scroll = Descendants(panel).OfType<ScrollViewer>().Single(view => AutomationProperties.GetName(view) == "待办事项列表");
            Assert(scroll.Extent.Height <= scroll.Viewport.Height && !scroll.GetVisualDescendants().OfType<ScrollBar>()
                .Any(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.IsVisible),
                "An empty or short task list retained a misleading scroll indicator.");
            if (outputDirectory != null) SavePng(panel, Path.Combine(outputDirectory, "panel-no-deadline.png"));
            session.SetCompleted(item.Id, true); panel.RestorePanel(); await Task.Delay(60);
            Assert(Descendants(panel).OfType<TextBlock>().Any(text => text.Text == "未设截止") &&
                Descendants(panel).OfType<TextBlock>().Single(text => text.Text == item.Text).TextDecorations == TextDecorations.Strikethrough,
                "Completing an undated row lost its status label or strikethrough.");
            Assert(!Directory.Exists(directory), "The isolated preview unexpectedly wrote no-deadline task data.");
        }
        finally { panel.Close(); }
    }

    private static async Task ValidateUnifiedPanel(TodoWindow owner, string? outputDirectory)
    {
        var custom = AppearanceSettings.CreateLight();
        custom.BackgroundColor = "#E8E1D6"; custom.PrimaryTextColor = "#263947"; custom.SecondaryTextColor = "#647583";
        custom.BackgroundTransparency = 35;
        var transparent = AppearanceSettings.CreateLight(); transparent.BackgroundTransparency = 100;
        var appearances = new[] { ("light", AppearanceSettings.CreateLight()), ("dark", AppearanceSettings.CreateDark()),
            ("custom", custom), ("transparent", transparent) };
        foreach (var (name, appearance) in appearances)
        {
            var state = new TodoState();
            state.Settings.Width = 450; state.Settings.Height = 550; state.Settings.AutoExpand = false; state.Settings.Appearance = appearance;
            string[] labels = { "向李老师汇报研究进展", "整理实验数据", "完善论文第二章", "阅读两篇参考文献", "提交本周研究小结",
                "核对实验记录", "准备组会讨论提纲", "整理研究资料", "检查论文格式", "归档参考文献" };
            for (int index = 0; index < labels.Length; index++)
            {
                bool hasDeadline = index is not (3 or 7 or 9);
                state.Items.Add(new TodoItem { Id = "visual-" + index, Text = labels[index],
                    Priority = index == 0 ? TaskPriority.High : index is 3 or 7 or 9 ? TaskPriority.Low : TaskPriority.Normal,
                    CreatedAt = DateTime.Now.AddDays(-3), DeadlineSetAt = hasDeadline ? DateTime.Now.AddDays(-3) : null,
                    HasDeadline = hasDeadline, DueDate = hasDeadline ? DateTime.Today.AddDays(index + 2).AddHours(17) : DateTime.MinValue,
                    HasTime = hasDeadline && index == 0 });
            }
            string directory = Path.Combine(Program.Options.DataDirectory, "unified-" + name);
            var session = new TodoSession(new TodoStore(directory), state, true);
            var panel = new TodoWindow(session, directory, () => { }) { PreviewMode = true };
            try
            {
                panel.Show(owner); await Task.Delay(130); panel.UpdateLayout();
                var add = Descendants(panel).OfType<Button>().Single(button => Equals(button.Content, "＋ 添加待办"));
                var more = Descendants(panel).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "更多操作");
                var toolbar = panel.GetVisualDescendants().OfType<Border>().Single(border => border.Child is StackPanel tools && tools.Children.Contains(more));
                var addSurface = ActionSurface(add);
                Assert(ColorOf(toolbar.Background) == ColorOf(addSurface.Background) && ColorOf(toolbar.BorderBrush) == ColorOf(addSurface.BorderBrush)
                    && toolbar.CornerRadius == new CornerRadius(8) && addSurface.CornerRadius == toolbar.CornerRadius,
                    "Toolbar and compact add action lost their shared surface/outline/corners in " + name + ".");
                Assert(ColorOf(addSurface.Background).A >= 180 && add.Bounds.Width < panel.Width * .6 && add.Bounds.Height >= 34,
                    "An action became invisible on transparent wallpaper or filled the entire footer.");
                Color initial = ColorOf(addSurface.Background);
                ((IPseudoClasses)add.Classes).Set(":pointerover", true); await Task.Delay(25);
                Assert(ColorOf(addSurface.Background) != initial && ColorOf(addSurface.Background).A >= 180,
                    "The real pointer-over template state lost the action fill.");
                Color hover = ColorOf(addSurface.Background);
                ((IPseudoClasses)add.Classes).Set(":pressed", true); await Task.Delay(25);
                Assert(ColorOf(addSurface.Background) != hover && ColorOf(addSurface.Background).A >= 180,
                    "The pressed template state lost its readable action fill.");
                ((IPseudoClasses)add.Classes).Set(":pressed", false); ((IPseudoClasses)add.Classes).Set(":pointerover", false);
                await Task.Delay(25);
                Assert(ColorOf(addSurface.Background) == initial, "The action did not restore its normal fill after interaction.");
                var scroll = Descendants(panel).OfType<ScrollViewer>().Single(view => AutomationProperties.GetName(view) == "待办事项列表");
                var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(candidate => candidate.Orientation == Avalonia.Layout.Orientation.Vertical);
                var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
                var indicator = thumb.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ScrollIndicator");
                Console.WriteLine($"  panel-{name}: extent={scroll.Extent.Height:0.##}, viewport={scroll.Viewport.Height:0.##}, visible={bar.IsVisible}, bar={bar.Bounds}, thumb={thumb.Bounds}, indicator={indicator.Bounds}");
                Assert(scroll.Extent.Height > scroll.Viewport.Height && bar.IsVisible && Math.Abs(bar.Bounds.Width - 14) < .1
                    && Math.Abs(thumb.Bounds.Width - 14) < .1 && indicator.Width == 5 && Math.Abs(indicator.Bounds.Width - 5) <= 1 / panel.RenderScaling + .01,
                    "The slim indicator lost its wider drag area or did not correspond to overflow.");
                Assert(indicator.CornerRadius == new CornerRadius(2.5) && ColorOf(indicator.Background) == Color.Parse(appearance.SecondaryTextColor),
                    "Scrollbar colour/radius did not follow the panel theme.");
                ((IPseudoClasses)thumb.Classes).Set(":pointerover", true); await Task.Delay(20);
                Assert(Math.Abs(indicator.Opacity - .85) < .001, "The real hover template did not emphasize the slim indicator.");
                ((IPseudoClasses)thumb.Classes).Set(":pressed", true); await Task.Delay(20);
                Assert(Math.Abs(indicator.Opacity - 1) < .001, "The real pressed template did not emphasize the slim indicator.");
                ((IPseudoClasses)thumb.Classes).Set(":pressed", false); ((IPseudoClasses)thumb.Classes).Set(":pointerover", false);
                Assert(bar.GetVisualDescendants().OfType<RepeatButton>().Count() == 2, "Arrow controls reappeared in the panel scrollbar.");
                scroll.Offset = new Vector(0, 40); await Task.Delay(25);
                Assert(Math.Abs(bar.Value - scroll.Offset.Y) < .1 && scroll.Offset.Y > 0, "ScrollViewer offset stopped synchronizing the real scrollbar.");
                scroll.Offset = default; await Task.Delay(25);
                var pageDown = bar.GetVisualDescendants().OfType<RepeatButton>().Single(button => button.Name == "PART_PageDownButton");
                pageDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(25);
                Assert(scroll.Offset.Y > 0, "The real scrollbar page-down action no longer scrolls content.");
                scroll.Offset = default; await Task.Delay(25);
                thumb.RaiseEvent(new Avalonia.Input.VectorEventArgs { RoutedEvent = Thumb.DragStartedEvent, Vector = default });
                thumb.RaiseEvent(new Avalonia.Input.VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(0, 12) });
                thumb.RaiseEvent(new Avalonia.Input.VectorEventArgs { RoutedEvent = Thumb.DragCompletedEvent, Vector = new Vector(0, 12) });
                await Task.Delay(25);
                Assert(scroll.Offset.Y > 0 && Math.Abs(scroll.Offset.Y - bar.Value) < .1, "The real Thumb/Track drag route stopped updating the viewport.");
                scroll.Offset = default; await Task.Delay(25);
                if (outputDirectory != null)
                {
                    SavePng(panel, Path.Combine(outputDirectory, "panel-" + name + ".png"));
                    if (name == "light") { scroll.Offset = new Vector(0, 130); await Task.Delay(25); SavePng(panel, Path.Combine(outputDirectory, "scroll-overflow.png")); }
                }
                panel.SetEdgeHiddenForPreview(true); await Task.Delay(25);
                Assert(ColorOf(((Border)panel.ContentSurface).Background).A == 244 && panel.IsEdgeHidden,
                    "Unified actions reduced the edge capsule's independent contrast.");
                panel.SetEdgeHiddenForPreview(false); await Task.Delay(25);
                Assert(ColorOf(ActionSurface(add).Background) == initial && !panel.IsEdgeHidden,
                    "Returning from the capsule changed normal action transparency.");
                Assert(!Directory.Exists(directory), "An auxiliary visual check wrote sample task data.");
            }
            finally { panel.Close(); }
        }
    }

    private static Border ActionSurface(Button button) => button.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ActionSurface");
    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : throw new InvalidOperationException("Expected a solid panel brush.");

    private static async Task ValidateAuxiliaryDialog(TodoWindow owner)
    {
        string information = Program.FeedbackInformation(Program.Options.DataDirectory);
        Assert(information.Contains(Program.Version) && information.Contains(Program.BuildNumber) && information.Contains("程序架构")
            && information.Contains(Path.GetFullPath(Program.Options.DataDirectory)), "About feedback omitted version/build/platform/data-path context.");
        Assert(information.Contains("关于本机"), "A process architecture was incorrectly presented as a specific M-series chip model.");
        var dialog = new Window { Title = "辅助对话框验证", Width = 280, Height = 160, ShowInTaskbar = false };
        Ui.Style(dialog);
        Task show = owner.ShowAuxiliaryDialog(dialog);
        try { await Task.Delay(60); Assert(owner.IsBusy && dialog.IsVisible, "An auxiliary dialog did not guard panel movement and hiding."); }
        finally { dialog.Close(); await show; }
        Assert(!owner.IsBusy, "Closing an auxiliary dialog left the panel permanently busy.");
    }

    private static void ValidateAppearance(TodoWindow owner)
    {
        int previews = 0;
        var editor = new AppearanceWindow(AppearanceSettings.CreateLight(), _ => previews++, _ => false);
        try
        {
            var slider = Descendants(editor).OfType<Slider>().Single(); slider.Value = 100;
            var candidate = editor.BuildCandidate();
            Assert(candidate.BackgroundTransparency == 100 && previews > 0, "Appearance editing did not preview a valid transparency change.");
            var fields = Descendants(editor).OfType<TextBox>().ToArray();
            Assert(fields.Length == 3, "Appearance editor is missing its three color fields.");
            fields[0].Text = "bad";
            ExpectInvalid(() => editor.BuildCandidate(), "An invalid custom color was accepted.");
        }
        finally { editor.Close(); }
    }

    private static async Task ValidateRealEditorFlow(TodoWindow window)
    {
        var add = Descendants(window).OfType<Button>().Single(button => Equals(button.Content, "＋ 添加待办"));
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var editor = lifetime.Windows.OfType<TaskEditorWindow>().Single(candidate => candidate.IsVisible);
        Assert(window.IsBusy && editor.IsVisible && !editor.Topmost, "The actual Add request did not open a normal editor and guard its panel.");
        window.Close();
        Assert(window.IsVisible && window.IsBusy, "Closing the panel bypassed an active editor guard.");
        editor.Close(false); await Task.Delay(100);
        Assert(!window.IsBusy && window.IsVisible, "Cancelling the actual editor did not release its panel guard.");
    }

    private static async Task ValidateEditorLayouts(TodoWindow owner, string? outputDirectory)
    {
        var item = new TodoItem { Id = "render-editor", Text = "向李老师汇报研究进展", Notes = "整理本周实验结果\n准备下一阶段研究计划",
            DueDate = DateTime.Today.AddDays(1).AddHours(17).AddMinutes(23), HasTime = true, CreatedAt = DateTime.Now.AddDays(-2) };
        var editor = new TaskEditorWindow(item, _ => false);
        try
        {
            editor.Show(owner); await Task.Delay(120);
            Assert(editor.Hour.Bounds.Width > 0 && editor.Minute.Bounds.Width > 0, "Real time-selector layout was empty.");
            Assert(Descendants(editor).OfType<Button>().Single(button => Equals(button.Content, "保存")).Bounds.Width > 0,
                "The editor save control was not laid out.");
            if (outputDirectory != null) SavePng(editor, Path.Combine(outputDirectory, "editor.png"));
            editor.Width = 320; editor.Height = 300; await Task.Delay(80);
            var save = Descendants(editor).OfType<Button>().Single(button => Equals(button.Content, "保存"));
            Assert(save.Bounds.Height > 0 && editor.Bounds.Width <= 321, "Small editor layout failed.");
            if (outputDirectory != null) SavePng(editor, Path.Combine(outputDirectory, "editor-small.png"));
        }
        finally { editor.Close(); }
        var undated = new TaskEditorWindow(new TodoItem { Id = "render-undated", Text = "阅读两篇参考文献", HasDeadline = false,
            DueDate = DateTime.MinValue, CreatedAt = DateTime.Now.AddDays(-1) }, _ => false);
        try
        {
            undated.Show(owner); await Task.Delay(100);
            Assert(undated.UseDeadline.IsChecked == false && !undated.BuildDraft().HasDeadline,
                "The no-deadline editor screenshot changed its draft semantics.");
            if (outputDirectory != null) SavePng(undated, Path.Combine(outputDirectory, "no-deadline-editor.png"));
        }
        finally { undated.Close(); }
        var appearance = new AppearanceWindow(AppearanceSettings.CreateDark(), _ => { }, _ => false);
        try
        {
            appearance.Show(owner); await Task.Delay(100);
            if (outputDirectory != null) SavePng(appearance, Path.Combine(outputDirectory, "appearance.png"));
        }
        finally { appearance.Close(); }
    }

    private static async Task ValidateEdge(TodoWindow window, TodoSession session, string? outputDirectory)
    {
        DateTime clock = DateTime.UtcNow;
        window.UtcNow = () => clock;
        window.SetEdgeModeForPreview(true); window.RestorePanel();
        PanelBounds baseline = window.BaselineBounds;
        window.ObservePointerForPreview(false); clock = clock.AddSeconds(2); window.TickForPreview();
        Assert(!window.IsEdgeHidden, "Explicit restore disappeared before the user entered the panel.");
        window.ObservePointerForPreview(true); window.ObservePointerForPreview(false);
        clock = clock.AddMilliseconds(799); window.TickForPreview();
        Assert(!window.IsEdgeHidden, "The preview hid before 800 milliseconds.");
        clock = clock.AddMilliseconds(1); window.TickForPreview();
        Assert(window.IsEdgeHidden && window.Width == 48 && window.Height == 152, "The edge delay did not produce capsule geometry.");
        Assert(window.BaselineBounds.Width == baseline.Width && window.BaselineBounds.Height == baseline.Height, "Capsule geometry overwrote its saved baseline.");
        Assert(Descendants(window).OfType<TextBlock>().Any(text => text.Text == "3"), "Capsule did not show the pending count.");
        await Task.Delay(150);
        if (outputDirectory != null) SavePng(window, Path.Combine(outputDirectory, "edge-capsule.png"));
        window.ObservePointerForPreview(true); window.TickForPreview();
        Assert(window.IsEdgeHidden, "Hovering expanded the capsule.");
        window.RestorePanel();
        Assert(!window.IsEdgeHidden && window.Width >= 320, "Restore failed to recover the full panel.");
        window.SetEdgeModeForPreview(false);
        Assert(!window.IsEdgeHidden && !session.Snapshot().Settings.EdgeAutoHide, "Disabling the option left its capsule active.");
        window.UtcNow = () => DateTime.UtcNow;
    }

    private static IEnumerable<Control> Descendants(Control control) =>
        control.GetLogicalDescendants().OfType<Control>();

    private static void SavePng(Window window, string path)
    {
        window.UpdateLayout();
        double scale = Math.Max(1, window.RenderScaling);
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scale))), new Vector(96 * scale, 96 * scale));
        // Include the editor's real client background and padding; rendering its
        // content Grid alone would misleadingly leave native controls on alpha.
        bitmap.Render(window); bitmap.Save(path);
    }

    private static void ExpectInvalid(Action action, string message)
    {
        bool invalid = false; try { action(); } catch (InvalidOperationException) { invalid = true; }
        Assert(invalid, message);
    }
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
}
