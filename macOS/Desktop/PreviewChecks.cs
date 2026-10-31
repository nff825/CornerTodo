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
        var content = (Control)window.Content!;
        double scale = Math.Max(1, window.RenderScaling);
        using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(content.Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(content.Bounds.Height * scale))), new Vector(96 * scale, 96 * scale));
        bitmap.Render(content); bitmap.Save(path);
    }

    private static void ExpectInvalid(Action action, string message)
    {
        bool invalid = false; try { action(); } catch (InvalidOperationException) { invalid = true; }
        Assert(invalid, message);
    }
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
}
