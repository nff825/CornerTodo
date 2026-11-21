using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CornerTodo;

internal static class AppearanceUiTests
{
    private static readonly StringBuilder Report = new StringBuilder();
    private static string outputDirectory;
    private static string isolatedRoot;
    private static int assertions;

    [STAThread]
    public static int Main(string[] args)
    {
        Application application = null;
        try
        {
            outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appearance-qa"));
            Directory.CreateDirectory(outputDirectory);
            isolatedRoot = Path.Combine(outputDirectory, ".isolated-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolatedRoot);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Report.AppendLine("CornerTodo appearance offscreen validation: " + DateTime.Now.ToString("O"));
            Report.AppendLine("No Window.Show, ShowDialog, native color dialog, desktop attachment or production data is used.");
            ValidatePresetRendering();
            ValidateUnifiedControls();
            ValidateBackgroundOnlyTransparency();
            ValidateAttentionTheme();
            ValidateTextScaleRendering();
            ValidatePreviewSaveAndFailure();
            ValidateEditorInteractions();
            ValidateTextScaleEditor();
            Report.AppendLine("PASS: " + assertions + " assertions.");
            Report.AppendLine("Scope: production control tree, rendered pixels, preview isolation, saved state, failed-save rollback and editor callbacks.");
            Report.AppendLine("Actual desktop composition, modal OS color selection and native mouse input require a separate interactive check.");
            File.WriteAllText(Path.Combine(outputDirectory, "appearance-validation.txt"), Report.ToString(), new UTF8Encoding(false));
            string previousFailure = Path.Combine(outputDirectory, "appearance-validation-failure.txt");
            if (File.Exists(previousFailure)) File.Delete(previousFailure);
            Console.Write(Report.ToString());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            if (outputDirectory != null)
                File.WriteAllText(Path.Combine(outputDirectory, "appearance-validation-failure.txt"),
                    Report.ToString() + Environment.NewLine + error, new UTF8Encoding(false));
            return 1;
        }
        finally
        {
            if (application != null) application.Shutdown();
            // Only remove this test-owned temporary directory within its QA output.
            if (isolatedRoot != null && outputDirectory != null)
            {
                string resolved = Path.GetFullPath(isolatedRoot);
                string prefix = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(resolved).StartsWith(".isolated-data-", StringComparison.Ordinal))
                    try { Directory.Delete(resolved, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static TodoState Scenario()
    {
        DateTime now = DateTime.Now;
        TodoState state = new TodoState();
        state.Settings.Width = 450; state.Settings.Height = 540;
        state.Settings.Left = 1225; state.Settings.Top = 23;
        state.Settings.AutoExpand = false;
        state.Items.Add(new TodoItem { Id = "sample-pending", Text = "向李老师汇报研究进展", DueDate = now.Date.AddDays(2).AddHours(14),
            HasTime = true, CreatedAt = now.AddDays(-1), DeadlineSetAt = now.AddDays(-1) });
        state.Items.Add(new TodoItem { Id = "sample-next", Text = "整理实验数据并确认下周测量方案", DueDate = now.Date.AddDays(3),
            HasTime = false, CreatedAt = now.AddHours(-2), DeadlineSetAt = now.AddHours(-2) });
        state.Items.Add(new TodoItem { Id = "sample-complete", Text = "完成阶段实验记录归档", DueDate = now.Date.AddDays(-1),
            CreatedAt = now.AddDays(-2), IsCompleted = true, CompletedAt = now.AddHours(-1) });
        return state;
    }

    private static void ValidatePresetRendering()
    {
        string[] names = { "light", "dark", "original" };
        AppearanceSettings[] presets = { AppearanceSettings.CreateLight(), AppearanceSettings.CreateDark(), AppearanceSettings.CreateOriginal() };
        for (int i = 0; i < names.Length; i++)
        {
            TodoState state = Scenario(); state.Settings.Appearance = presets[i];
            string dataPath = Path.Combine(isolatedRoot, "render-" + names[i]);
            MainWindow window = new MainWindow(new TodoStore(dataPath), state, true, true);
            try
            {
                CheckPanel(window, presets[i]);
                RenderWindow(window, names[i] + ".png", true);
                Assert(!Directory.Exists(dataPath), "Rendering a preset unexpectedly created data files.");
                AssertNoHandle(window);
            }
            finally { window.Close(); }
        }
        Report.AppendLine("Presets: real light/dark/original panel trees rendered to PNG; background and primary/secondary colors match settings.");
    }

    private static void ValidateBackgroundOnlyTransparency()
    {
        TodoState state = Scenario();
        MainWindow window = new MainWindow(new TodoStore(Path.Combine(isolatedRoot, "alpha")), state, true, true);
        try
        {
            AppearanceSettings opaque = AppearanceSettings.CreateLight(); opaque.BackgroundTransparency = 0;
            Preview(window, opaque);
            Dictionary<string, string> before = TextAppearance(window);
            CheckPanel(window, opaque);
            AppearanceSettings transparent = opaque.Clone(); transparent.BackgroundTransparency = 100;
            Preview(window, transparent);
            CheckPanel(window, transparent);
            Dictionary<string, string> after = TextAppearance(window);
            Assert(before.Count == after.Count && before.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value),
                "Changing background transparency faded or recolored text.");
            RenderWindow(window, "fully-transparent.png", true);
            AssertNoHandle(window);
        }
        finally { window.Close(); }
        Report.AppendLine("Transparency: 0% and 100% alter only the panel fill alpha; text alpha, text opacity and parent opacity remain unchanged.");
    }

    private static void ValidateUnifiedControls()
    {
        AppearanceSettings custom = new AppearanceSettings { BackgroundTransparency = 58, BackgroundColor = "#DFEEE2",
            PrimaryTextColor = "#193C29", SecondaryTextColor = "#4D7057" };
        AppearanceSettings transparent = AppearanceSettings.CreateDark(); transparent.BackgroundTransparency = 100;
        AppearanceSettings[] themes = { AppearanceSettings.CreateLight(), AppearanceSettings.CreateDark(),
            AppearanceSettings.CreateOriginal(), custom, transparent };
        Color? lightSurface = null;
        for (int index = 0; index < themes.Length; index++)
        {
            TodoState state = UnifiedScenario(); state.Settings.Appearance = themes[index];
            string dataPath = Path.Combine(isolatedRoot, "unified-" + index);
            MainWindow window = new MainWindow(new TodoStore(dataPath), state, true, true);
            try
            {
                // Fix only this synthetic fixture's date for reproducible docs.
                window.Panel.Render(state, new DateTime(2026, 11, 4, 14, 0, 0), true, themes[index]);
                string imageName = index == 0 ? "unified-panel.png" : index == 1 ? "unified-dark-panel.png" :
                    index == 3 ? "unified-custom-panel.png" : index == 4 ? "unified-transparent-panel.png" : null;
                RenderWindow(window, imageName, true);
                TodoPanelView panel = window.Panel;
                Button add = panel.AddButton;
                Border addSurface = ButtonSurface(add);
                Color toolbarColor = BrushColor(panel.ToolbarSurface.Background);
                Assert(toolbarColor == BrushColor(addSurface.Background), "Toolbar and add action must have exactly the same base surface color and alpha.");
                Assert(BrushColor(panel.ToolbarSurface.BorderBrush) == BrushColor(addSurface.BorderBrush),
                    "Toolbar and add action must share their subtle outline color.");
                Assert(panel.ToolbarSurface.CornerRadius == addSurface.CornerRadius,
                    "Toolbar and add action must share the same corner radius.");
                Assert(toolbarColor.A >= 180 && toolbarColor.A < 240,
                    "Action entry points must stay visible without becoming the old nearly opaque white block.");
                Assert(add.ActualWidth < panel.Surface.ActualWidth / 2 && add.Padding.Left >= 10 && add.Padding.Right >= 10,
                    "The add action should be a compact padded control rather than a full-width footer fill.");
                Assert(add.ActualHeight >= 32 && panel.ToolbarSurface.ActualHeight >= 32,
                    "Unified action controls must keep a usable pointer target.");
                if (index == 0) lightSurface = toolbarColor;
                if (index == 1 || index == 3) Assert(toolbarColor != lightSurface.Value,
                    "Dark/custom action surfaces must derive from the selected theme, not a fixed light swatch.");

                // Read-only input dependency properties are driven directly in
                // this offscreen harness so the real template triggers execute.
                // This does not emulate native mouse routing or create an HWND.
                SetInputState(add, typeof(UIElement), "IsMouseOverPropertyKey", true);
                RenderWindow(window, null, true);
                Color hovered = BrushColor(ButtonSurface(add).Background);
                Assert(hovered.A >= toolbarColor.A && hovered != toolbarColor,
                    "Hover must preserve the add action's visible fill and provide distinct feedback.");
                SetInputState(add, typeof(ButtonBase), "IsPressedPropertyKey", true);
                RenderWindow(window, null, true);
                Color pressed = BrushColor(ButtonSurface(add).Background);
                Assert(pressed.A >= toolbarColor.A && pressed != hovered,
                    "Pressed must remain visible and differ from hover.");
                SetInputState(add, typeof(ButtonBase), "IsPressedPropertyKey", false);
                SetInputState(add, typeof(UIElement), "IsMouseOverPropertyKey", false);
                RenderWindow(window, null, true);
                Assert(BrushColor(ButtonSurface(add).Background) == toolbarColor, "Leaving the add action must restore its shared base fill.");

                SetInputState(panel.MoreButton, typeof(UIElement), "IsMouseOverPropertyKey", true);
                RenderWindow(window, null, true);
                Assert(BrushColor(ButtonSurface(panel.MoreButton).Background).A > 0 &&
                    BrushColor(panel.ToolbarSurface.Background) == toolbarColor,
                    "Toolbar hover must be visible over the same persistent theme surface.");
                SetInputState(panel.MoreButton, typeof(UIElement), "IsMouseOverPropertyKey", false);
                RenderWindow(window, null, true);

                ScrollBar bar = (ScrollBar)panel.Scroll.Template.FindName("PART_VerticalScrollBar", panel.Scroll);
                Assert(bar != null && panel.Scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible && panel.Scroll.ScrollableHeight > 0,
                    "Long fixture must expose a real scrollable viewport and scrollbar.");
                Track track = (Track)bar.Template.FindName("PART_Track", bar);
                Border indicator = (Border)track.Thumb.Template.FindName("Indicator", track.Thumb);
                Assert(indicator != null && indicator.ActualWidth >= 4 && indicator.ActualWidth <= 6 && bar.ActualWidth >= 14,
                    "Scrollbar must stay visually slim with a wider usable drag target.");
                Brush expectedScrollColor = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : panel.Palette.Muted;
                Assert(BrushColor(indicator.Background) == BrushColor(expectedScrollColor) && indicator.Opacity >= 0.5,
                    "Scrollbar must follow the theme's secondary color and remain visible with transparent panels.");
                Near(1, add.Opacity, "Action control opacity");
                Near(1, panel.Surface.Opacity, "Unified panel opacity");
                window.Width = 48; window.Height = 152;
                panel.SetEdgeHidden(true);
                panel.Render(state, new DateTime(2026, 11, 4, 14, 0, 0), true, themes[index]);
                RenderWindow(window, null, true);
                Color capsuleColor = BrushColor(panel.Surface.Background);
                Assert(capsuleColor.A >= 230 && capsuleColor.R == toolbarColor.R && capsuleColor.G == toolbarColor.G && capsuleColor.B == toolbarColor.B,
                    "The edge capsule must retain a highly visible, theme-matched fill independently of normal action transparency.");
                Assert(panel.Surface.Child == panel.EdgeButton && BrushColor(panel.Palette.Ink).A == 255,
                    "Theme refresh must preserve the compact capsule and its opaque text, including on fully transparent panels.");
                Assert(BrushColor(panel.ToolbarSurface.Background) == toolbarColor,
                    "The capsule's stronger fill must not change the normal toolbar color or transparency.");
                panel.SetEdgeHidden(false); window.Width = 430; window.Height = 540;
                RenderWindow(window, null, true);
                Assert(panel.Surface.Child == panel.Root && BrushColor(ButtonSurface(add).Background) == toolbarColor &&
                    BrushColor(panel.ToolbarSurface.Background) == toolbarColor,
                    "Expanding the capsule must restore the unified normal action surfaces.");
                Assert(!Directory.Exists(dataPath), "Unified style validation unexpectedly wrote application data.");
                AssertNoHandle(window);
            }
            finally { window.Close(); }
        }
        Report.AppendLine("Unified actions: light/dark/original/custom/transparent themes share toolbar and compact add surfaces; real hover/pressed template transitions preserve fill; slim scrollbar retains its wider drag area; independent edge fill retains capsule contrast across theme refresh and reveal.");
    }

    private static TodoState UnifiedScenario()
    {
        DateTime now = new DateTime(2026, 11, 4, 14, 0, 0);
        TodoState state = new TodoState(); state.Settings.Width = 430; state.Settings.Height = 540;
        state.Settings.AutoExpand = false;
        string[] titles = { "向李老师汇报研究进展", "整理实验数据", "完成论文初稿", "阅读两篇参考文献", "提交周报", "整理书架" };
        for (int index = 0; index < titles.Length; index++)
        {
            bool undated = index == 3 || index == 5;
            state.Items.Add(new TodoItem { Id = "unified-sample-" + index, Text = titles[index],
                HasDeadline = !undated, HasTime = index == 0,
                DueDate = undated ? DateTime.MinValue : index == 0 ? now.Date.AddHours(17) : now.Date.AddDays(index == 1 ? 1 : index == 2 ? 4 : 2),
                CreatedAt = now.AddDays(-3), DeadlineSetAt = undated ? (DateTime?)null : now.AddDays(-3),
                Priority = index == 0 || index == 2 ? TaskPriority.High : undated ? TaskPriority.Low : TaskPriority.Normal });
        }
        state.Items.Add(new TodoItem { Id = "unified-done", Text = "完成上周实验记录归档", HasDeadline = false,
            IsCompleted = true, CreatedAt = now.AddDays(-5), CompletedAt = now.AddDays(-1) });
        return state;
    }

    private static Border ButtonSurface(Button button)
    {
        button.ApplyTemplate();
        Border surface = button.Template.FindName("Surface", button) as Border;
        Assert(surface != null, "A panel action button has no rendered surface.");
        return surface;
    }

    private static Color BrushColor(Brush brush)
    {
        SolidColorBrush solid = brush as SolidColorBrush;
        Assert(solid != null, "Expected a theme-colored solid brush.");
        return solid.Color;
    }

    private static void SetInputState(DependencyObject control, Type owner, string keyName, bool enabled)
    {
        FieldInfo field = owner.GetField(keyName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert(field != null, "Missing test input-state key: " + keyName);
        control.SetValue((DependencyPropertyKey)field.GetValue(null), enabled);
    }

    private static void ValidateAttentionTheme()
    {
        TodoState state = Scenario();
        DateTime now = DateTime.Now;
        state.Items.Add(new TodoItem { Id = "sample-urgent", Text = "与王同学确认实验方案", DueDate = now.AddHours(2),
            HasTime = true, CreatedAt = now.AddHours(-22), DeadlineSetAt = now.AddHours(-22) });
        state.Items.Add(new TodoItem { Id = "sample-overdue", Text = "给张老师发送论文修改稿", DueDate = now.AddHours(-3),
            HasTime = true, CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2) });
        state.Settings.Height = 720;
        state.Settings.Appearance = AppearanceSettings.CreateDark();
        MainWindow window = new MainWindow(new TodoStore(Path.Combine(isolatedRoot, "attention")), state, true, true);
        try
        {
            RenderWindow(window, "dark-attention.png", true);
            List<TextBlock> texts = Descendants(window.Panel.Surface).OfType<TextBlock>().ToList();
            foreach (TodoItem item in state.Items.Where(t => t.Id == "sample-urgent" || t.Id == "sample-overdue"))
            {
                TextBlock text = texts.Single(t => t.Text == item.Text);
                Near(item.Id == "sample-urgent" ? 19 : 21, text.FontSize, "Attention text size after a theme change");
                Assert(text.FontWeight == FontWeights.SemiBold, "Attention emphasis was lost after a theme change.");
                SolidColorBrush foreground = text.Foreground as SolidColorBrush;
                Assert(foreground != null && foreground.Color.A == 255, "Attention text was faded by background transparency.");
                Grid row = Descendants(window.Panel.Surface).OfType<Grid>().Single(g => Object.Equals(g.Tag, item.Id));
                TextBlock due = Descendants(row).OfType<TextBlock>().Single(t => Object.Equals(t.Tag, "deadline"));
                Assert(due.Text.Contains(item.Id == "sample-urgent" ? "剩余" : "已逾期"), "Attention status disappeared after a theme change.");
            }
            CheckPanel(window, state.Settings.Appearance);
            AssertNoHandle(window);
        }
        finally { window.Close(); }
        Report.AppendLine("Dark attention view: urgent/overdue typography, opaque text, status labels and normal selected colors are preserved.");
    }

    private static void ValidatePreviewSaveAndFailure()
    {
        TodoState initial = Scenario();
        initial.Settings.Appearance = AppearanceSettings.CreateOriginal();
        TodoStore store = new TodoStore(Path.Combine(isolatedRoot, "save-lifecycle"));
        store.Save(initial);
        byte[] originalBytes = File.ReadAllBytes(store.DataFilePath);
        MainWindow window = new MainWindow(store, initial, true, false);
        try
        {
            string unchanged = StateWithoutAppearance(window.CurrentState);
            AppearanceSettings committed = window.CurrentState.Settings.Appearance.Clone();
            AppearanceSettings draft = AppearanceSettings.CreateDark();
            draft.TextScale = 1.25;
            Preview(window, draft);
            CheckPanel(window, draft);
            CheckTaskTextScale(window, 1.25);
            Assert(window.CurrentState.Settings.Appearance.Matches(committed), "Preview replaced committed appearance state.");
            Assert(File.ReadAllBytes(store.DataFilePath).SequenceEqual(originalBytes), "Preview wrote the data file.");
            Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Preview changed a task, deadline or baseline setting.");
            draft.BackgroundColor = "#112233";
            draft.TextScale = 1.5;
            Assert(window.CurrentState.Settings.Appearance.Matches(committed), "The draft aliases committed settings.");
            Invoke(window, "Render");
            CheckPanel(window, AppearanceSettings.CreateDark());
            CheckTaskTextScale(window, 1.25);

            // Exercise the same restoration entry point used by the editor's finally path.
            Invoke(window, "CancelAppearancePreview");
            CheckPanel(window, committed);
            CheckTaskTextScale(window, committed.TextScale);
            Assert(File.ReadAllBytes(store.DataFilePath).SequenceEqual(originalBytes), "Restoring a preview wrote data.");

            AppearanceSettings selected = AppearanceSettings.CreateDark();
            selected.BackgroundTransparency = 37;
            selected.TextScale = 1.3;
            Assert(Save(window, selected), "Saving a valid appearance failed.");
            Assert(window.CurrentState.Settings.Appearance.Matches(selected), "Successful save did not commit the selected appearance.");
            Assert(store.Load().Settings.Appearance.Matches(selected), "Appearance did not survive loading from disk.");
            Assert(!Object.ReferenceEquals(selected, window.CurrentState.Settings.Appearance), "Saved appearance shares the mutable draft object.");
            Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Saving appearance changed tasks or baseline geometry.");
            Assert(File.Exists(store.DataFilePath + ".bak"), "Saving appearance did not retain the previous data backup.");
            CheckPanel(window, selected);
            CheckTaskTextScale(window, 1.3);

            MainWindow reopened = new MainWindow(store, store.Load(), true, false);
            try
            {
                CheckPanel(reopened, selected);
                CheckTaskTextScale(reopened, 1.3);
                AssertNoHandle(reopened);
            }
            finally { reopened.Close(); }

            byte[] savedBytes = File.ReadAllBytes(store.DataFilePath);
            AppearanceSettings saved = window.CurrentState.Settings.Appearance.Clone();
            AppearanceSettings rejected = AppearanceSettings.CreateLight(); rejected.BackgroundColor = "#EEDDCC";
            rejected.TextScale = 0.9;
            Preview(window, rejected);
            using (FileStream locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert(!Save(window, rejected), "Saving over a nonreplaceable data file unexpectedly succeeded.");
                Assert(window.CurrentState.Settings.Appearance.Matches(saved), "Failed save replaced committed appearance.");
                Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Failed save changed tasks or baseline geometry.");
                Assert(File.ReadAllBytes(store.DataFilePath).SequenceEqual(savedBytes), "Failed save altered the previous data file.");
                CheckPanel(window, saved);
                CheckTaskTextScale(window, saved.TextScale);
            }
            Assert(store.Load().Settings.Appearance.Matches(saved), "Failed save changed the reloadable appearance.");
            Invoke(window, "CancelAppearancePreview");
            CheckPanel(window, saved);

            TodoState clone = window.CurrentState;
            Assert(StateWithoutAppearance(clone) == unchanged, "A current-state snapshot lost task or geometry fields.");
            Assert(!Object.ReferenceEquals(clone.Settings.Appearance, window.CurrentState.Settings.Appearance), "A current-state snapshot shares appearance settings.");
            clone.Settings.Appearance.BackgroundColor = "#010203";
            clone.Settings.Appearance.TextScale = 1.5;
            clone.Settings.Width = 1;
            clone.Items[0].Text = "snapshot-only edit";
            clone.Items.Clear();
            Assert(window.CurrentState.Settings.Appearance.Matches(saved), "Changing a snapshot mutated the committed appearance.");
            Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Changing a snapshot mutated committed tasks or baseline geometry.");
            RenderWindow(window, "saved-dark.png", true);
            AssertNoHandle(window);
        }
        finally { window.Close(); }
        Report.AppendLine("Persistence: color/text-scale preview and restore leave bytes unchanged; save survives a new window loaded from disk; locked-file failure rolls back display/model/disk; snapshots isolate tasks, appearance and baseline fields.");
    }

    private static void ValidateTextScaleRendering()
    {
        DateTime now = new DateTime(2026, 11, 20, 14, 0, 0);
        double[] scales = { 0.9, 1.25, 1.5 };
        foreach (double scale in scales)
        {
            TodoState state = new TodoState();
            state.Settings.Width = 430; state.Settings.Height = 710; state.Settings.AutoExpand = false;
            state.Settings.Appearance.TextScale = scale;
            state.Items.Add(new TodoItem { Id = "scale-overdue", Text = "提交报销材料", DueDate = now.AddDays(-1),
                HasTime = true, CreatedAt = now.AddDays(-3), DeadlineSetAt = now.AddDays(-3) });
            state.Items.Add(new TodoItem { Id = "scale-urgent", Text = "向李老师汇报研究进展", DueDate = now.AddHours(3),
                HasTime = true, Priority = TaskPriority.High, CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2) });
            state.Items.Add(new TodoItem { Id = "scale-normal", Text = "整理实验数据", DueDate = now.Date.AddDays(1),
                CreatedAt = now.AddDays(-2), DeadlineSetAt = now.AddDays(-2) });
            state.Items.Add(new TodoItem { Id = "scale-unscheduled", Text = "阅读两篇参考文献", HasDeadline = false,
                Priority = TaskPriority.Low, CreatedAt = now.AddDays(-1) });
            state.Items.Add(new TodoItem { Id = "scale-completed", Text = "归档会议纪要", DueDate = now.AddDays(-2),
                IsCompleted = true, CompletedAt = now.AddHours(-1), CreatedAt = now.AddDays(-3) });
            MainWindow window = new MainWindow(new TodoStore(Path.Combine(isolatedRoot, "scale-render-" + scale.ToString("0.##", CultureInfo.InvariantCulture))),
                state, true, true);
            try
            {
                window.Panel.Render(state, now, true, state.Settings.Appearance);
                RenderWindow(window, scale == 1.25 ? "text-scale-panel-125.png" : null, true);
                var titles = new List<TextBlock>();
                foreach (TodoItem item in state.Items)
                {
                    Grid row = Descendants(window.Panel.Surface).OfType<Grid>().Single(grid => Object.Equals(grid.Tag, item.Id));
                    TextBlock title = Descendants(row).OfType<TextBlock>().Single(text => text.Text == item.Text);
                    double baseSize = item.Id == "scale-overdue" ? 21 : item.Id == "scale-urgent" ? 19 : 15;
                    Near(baseSize * scale, title.FontSize, "Scaled task hierarchy: " + item.Id);
                    titles.Add(title);
                    TextBlock date = Descendants(row).OfType<TextBlock>().Single(text => Object.Equals(text.Tag, "deadline"));
                    Near((item.Id == "scale-overdue" || item.Id == "scale-urgent" ? 13 : 12) * scale,
                        date.FontSize, "Scaled task date: " + item.Id);
                    if (item.Id == "scale-overdue" || item.Id == "scale-urgent")
                    {
                        Assert(title.FontWeight == FontWeights.SemiBold, "Scaling removed attention emphasis.");
                        Assert(date.Text.Contains(item.Id == "scale-overdue" ? "已逾期" : "剩余"),
                            "Scaling removed a textual attention state.");
                    }
                    if (item.IsCompleted)
                        Assert(title.TextDecorations != null && title.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough),
                            "Scaling removed completed-task strikethrough.");
                }
                Assert(titles[0].FontSize > titles[1].FontSize && titles[1].FontSize > titles[2].FontSize,
                    "The overdue, urgent and ordinary task hierarchy collapsed at a scale endpoint.");
                Near(25, window.Panel.TitleText.FontSize, "The panel heading should keep its fixed size");

                window.Width = 320; window.Height = 360;
                state.Items[2].Text = "整理全部实验数据并核对原始记录，确认每一组测量结果以及对应的图表编号，补充说明后再提交研究小组审阅。";
                for (int index = 0; index < 10; index++)
                    state.Items.Add(new TodoItem { Id = "scale-extra-" + index, Text = "后续事项 " + (index + 1) + "：核对测量记录与分析结果",
                        DueDate = now.Date.AddDays(3), CreatedAt = now, DeadlineSetAt = now });
                window.Panel.Render(state, now, true, state.Settings.Appearance);
                RenderWindow(window, null, true);
                TextBlock longText = Descendants(window.Panel.Surface).OfType<TextBlock>().Single(text => text.Text == state.Items[2].Text);
                Assert(longText.TextWrapping == TextWrapping.Wrap && longText.ActualHeight > longText.FontSize * 2,
                    "Long scaled task text did not wrap in the narrow panel.");
                Assert(window.Panel.Scroll.ScrollableHeight > 0 && window.Panel.Scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                    "Long scaled rows did not keep a vertical-only scroll range.");
                window.Panel.Scroll.ScrollToBottom();
                RenderWindow(window, null, true);
                Near(window.Panel.Scroll.ScrollableHeight, window.Panel.Scroll.VerticalOffset, "Scrolling to the bottom of long scaled rows");
                AssertNoHandle(window);
            }
            finally { window.Close(); }
        }
        Report.AppendLine("Text scale rendering: 90%, 125% and 150% retain ordinary/urgent/overdue size hierarchy, proportional dates, completion marks, fixed headings and narrow-panel long-text scrolling.");
    }

    private static void ValidateEditorInteractions()
    {
        AppearanceSettings original = AppearanceSettings.CreateLight();
        List<AppearanceSettings> previews = new List<AppearanceSettings>();
        int saves = 0;
        AppearanceSettings savedValue = null;
        AppearanceEditor editor = new AppearanceEditor(original,
            delegate(AppearanceSettings value) { previews.Add(value); },
            delegate(AppearanceSettings value) { saves++; savedValue = value; return true; });
        try
        {
            Assert(editor.ResizeMode == ResizeMode.NoResize,
                "The modal appearance editor must not expose a minimize button without a taskbar entry.");
            RenderWindow(editor, "editor-light.png", false);
            AppearanceSettings clone = editor.CurrentAppearance; clone.BackgroundColor = "#010203";
            Assert(editor.CurrentAppearance.Matches(original), "Editor CurrentAppearance exposes mutable internal state.");
            Click(Find<Button>(editor, "预设：清晰深色"), false);
            Assert(editor.CurrentAppearance.Matches(AppearanceSettings.CreateDark()), "Dark preset button did not select the dark appearance.");
            Click(Find<Button>(editor, "预设：原版通透"), false);
            Assert(editor.CurrentAppearance.Matches(AppearanceSettings.CreateOriginal()), "Original preset button did not restore original values.");
            Click(Find<Button>(editor, "预设：清晰浅色"), false);
            Assert(editor.CurrentAppearance.Matches(AppearanceSettings.CreateLight()), "Light preset button did not select the light appearance.");
            editor.SelectPreset("dark");
            Click(Find<Button>(editor, "恢复默认"), false);
            Assert(editor.CurrentAppearance.Matches(AppearanceSettings.CreateLight()), "Reset button did not restore the light default.");
            Slider transparency = Find<Slider>(editor, "背景透明度");
            transparency.Value = 63;
            Near(63, editor.CurrentAppearance.BackgroundTransparency, "Transparency slider callback");
            Assert(previews.Count > 0 && Math.Abs(previews.Last().BackgroundTransparency - 63) < 0.001, "Slider did not send an updated preview.");
            previews.Last().PrimaryTextColor = "#010203";
            Assert(editor.CurrentAppearance.PrimaryTextColor == original.PrimaryTextColor, "The preview callback received the mutable editor draft.");

            TextBox backgroundHex = Find<TextBox>(editor, "面板底色 HEX");
            backgroundHex.Text = "#315274";
            Assert(editor.CurrentAppearance.BackgroundColor == "#315274", "Typing a valid background HEX value did not update the draft.");
            TextBox primaryHex = Find<TextBox>(editor, "主要文字 HEX");
            primaryHex.Text = "#F5F6F7";
            Assert(editor.CurrentAppearance.PrimaryTextColor == "#F5F6F7", "Typing primary text color did not update the draft.");
            Assert(editor.TrySetColor("secondary", "#abcdef"), "Valid secondary color was rejected.");
            Assert(editor.CurrentAppearance.SecondaryTextColor == "#ABCDEF", "Valid color was not normalized.");
            AppearanceSettings validDraft = editor.CurrentAppearance;
            backgroundHex.Text = "#NOTHEX";
            Assert(!editor.TrySave(), "Invalid HEX input was allowed to save.");
            Assert(saves == 0, "Invalid HEX input reached the save callback.");
            Assert(editor.CurrentAppearance.BackgroundColor == validDraft.BackgroundColor, "Invalid HEX input overwrote the last valid preview color.");
            backgroundHex.Text = "#315274";
            RenderWindow(editor, "editor-custom.png", false);
            Click(Find<Button>(editor, "保存外观设置"), true);
            Assert(saves == 1 && savedValue != null && savedValue.Matches(editor.CurrentAppearance), "Save button did not submit the current draft exactly once.");
            savedValue.BackgroundColor = "#010203";
            Assert(editor.CurrentAppearance.BackgroundColor == "#315274", "The save callback received the mutable editor draft.");
            Assert(original.Matches(AppearanceSettings.CreateLight()), "Editing or saving mutated the constructor input.");
            AssertNoHandle(editor);
        }
        finally { editor.Close(); }

        int failedCalls = 0;
        AppearanceEditor failed = new AppearanceEditor(original, delegate { }, delegate { failedCalls++; return false; });
        try
        {
            RenderWindow(failed, null, false);
            failed.SelectPreset("dark");
            Find<Slider>(failed, "文字大小").Value = 150;
            Click(Find<Button>(failed, "保存外观设置"), false);
            Assert(failedCalls == 1, "Save failure callback was not invoked exactly once.");
            AppearanceSettings failedDraft = AppearanceSettings.CreateDark(); failedDraft.TextScale = 1.5;
            Assert(failed.CurrentAppearance.Matches(failedDraft), "Failed editor save discarded the editable color/text-scale draft.");
            TextBlock failureMessage = Find<TextBlock>(failed, "外观设置错误提示");
            Assert(failureMessage.Visibility == Visibility.Visible && failureMessage.Text.Contains("保存失败"),
                "A failed save should leave a visible explanation in the editor.");
            RenderWindow(failed, "editor-save-failure.png", false);
            AssertNoHandle(failed);
        }
        finally { failed.Close(); }

        int cancelSaves = 0;
        AppearanceEditor cancelled = new AppearanceEditor(original, delegate { }, delegate { cancelSaves++; return true; });
        try
        {
            RenderWindow(cancelled, null, false);
            cancelled.SelectPreset("dark");
            Find<Slider>(cancelled, "文字大小").Value = 125;
            Click(Find<Button>(cancelled, "取消外观设置"), true);
            Assert(cancelSaves == 0, "Cancel invoked the save callback.");
            Assert(original.Matches(AppearanceSettings.CreateLight()), "Cancel mutated the committed input.");
            AssertNoHandle(cancelled);
        }
        finally { cancelled.Close(); }
        Report.AppendLine("Editor: preset buttons, slider, valid/invalid HEX, save success/failure and cancel callbacks passed on unshown windows.");
    }

    private static void ValidateTextScaleEditor()
    {
        AppearanceSettings original = AppearanceSettings.CreateLight(); original.TextScale = 1.25;
        var previews = new List<AppearanceSettings>();
        AppearanceEditor editor = new AppearanceEditor(original,
            delegate(AppearanceSettings value) { previews.Add(value); }, delegate { return true; });
        try
        {
            RenderWindow(editor, null, false);
            Slider scale = Find<Slider>(editor, "文字大小");
            Near(90, scale.Minimum, "Text scale slider minimum");
            Near(150, scale.Maximum, "Text scale slider maximum");
            Near(125, scale.Value, "Saved text scale slider value");
            Assert(Find<TextBlock>(editor, "文字大小数值").Text == "125%", "The text scale label does not show the saved percentage.");
            Assert(Find<Button>(editor, "预设：清晰浅色").BorderThickness.Left == 2,
                "A custom text scale cleared the matching light color preset selection.");
            Near(14, editor.FontSize, "The appearance editor's own text scale");
            Near(28, Descendants((DependencyObject)editor.Content).OfType<TextBlock>().Single(text => text.Text == "外观设置").FontSize,
                "The appearance heading should keep its original size");
            AppearanceSettings clone = editor.CurrentAppearance; clone.TextScale = 0.9;
            Near(1.25, editor.CurrentAppearance.TextScale, "The editor exposes the mutable text-scale draft");

            scale.Value = 150;
            Near(1.5, editor.CurrentAppearance.TextScale, "Text scale slider callback");
            Assert(previews.Count > 0, "The text scale slider did not send a preview.");
            Near(1.5, previews.Last().TextScale, "Text scale preview value");
            previews.Last().TextScale = 0.9;
            Near(1.5, editor.CurrentAppearance.TextScale, "The preview callback aliases the current text-scale draft");

            foreach (string name in new[] { "清晰深色", "原版通透", "清晰浅色" })
            {
                Button preset = Find<Button>(editor, "预设：" + name);
                Click(preset, false);
                Near(1.5, editor.CurrentAppearance.TextScale, "Changing a color preset reset text scale");
                Near(150, scale.Value, "Changing a color preset reset the text scale control");
                Assert(preset.BorderThickness.Left == 2, "The active color preset is not selected at 150% text scale.");
            }
            Assert(editor.TrySetColor("primary", "#274F63"), "The custom color used for reset testing was rejected.");
            AppearanceSettings beforeReset = editor.CurrentAppearance;
            Click(Find<Button>(editor, "恢复100%"), false);
            Near(1, editor.CurrentAppearance.TextScale, "Restore standard text scale");
            Near(100, scale.Value, "Restore standard text scale slider");
            Assert(editor.CurrentAppearance.PrimaryTextColor == beforeReset.PrimaryTextColor &&
                editor.CurrentAppearance.BackgroundColor == beforeReset.BackgroundColor &&
                editor.CurrentAppearance.BackgroundTransparency == beforeReset.BackgroundTransparency,
                "Restoring standard text scale changed the chosen colors or transparency.");
            Near(1, previews.Last().TextScale, "Restore standard text scale did not preview");

            scale.Value = 90;
            Near(0.9, editor.CurrentAppearance.TextScale, "Small text scale");
            Click(Find<Button>(editor, "恢复默认"), false);
            Assert(editor.CurrentAppearance.Matches(AppearanceSettings.CreateLight()), "Restore all defaults did not restore colors and 100% text scale.");
            Near(100, scale.Value, "Restore all defaults slider value");
            Near(1.25, original.TextScale, "Editing text scale changed the constructor input");

            scale.Value = 125;
            RenderWindow(editor, "editor-text-scale-125.png", false);
            editor.Width = 440; editor.Height = 340;
            RenderWindow(editor, null, false);
            ScrollViewer body = Find<ScrollViewer>(editor, "外观设置内容");
            Assert(body.ScrollableHeight > 0 && body.ViewportHeight > 0,
                "The small appearance editor did not keep its settings in a scrollable body.");
            foreach (string name in new[] { "取消外观设置", "保存外观设置", "恢复默认" })
            {
                Button button = Find<Button>(editor, name);
                Rect bounds = button.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(button.RenderSize));
                Assert(button.Visibility == Visibility.Visible && bounds.Left >= 0 && bounds.Top >= 0 &&
                    bounds.Right <= ((FrameworkElement)editor.Content).ActualWidth + 0.1 &&
                    bounds.Bottom <= ((FrameworkElement)editor.Content).ActualHeight + 0.1,
                    "The small appearance editor clipped its fixed footer action: " + name);
            }
            AssertNoHandle(editor);
        }
        finally { editor.Close(); }
        Report.AppendLine("Text scale editor: 90-150% slider, isolated preview, color presets retain scale/selection, restore100% preserves colors, restore-all resets everything, fixed-size editor typography and small-window footer passed.");
    }

    private static void CheckTaskTextScale(MainWindow window, double scale)
    {
        TextBlock pending = Descendants(window.Panel.Surface).OfType<TextBlock>().Single(text => text.Text == "向李老师汇报研究进展");
        Near(15 * scale, pending.FontSize, "The main task text did not use the selected scale");
        TextBlock completed = Descendants(window.Panel.Surface).OfType<TextBlock>().Single(text => text.Text == "完成阶段实验记录归档");
        Near(15 * scale, completed.FontSize, "Completed task text did not use the selected scale");
        Assert(completed.TextDecorations != null && completed.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough),
            "Changing text scale removed completed-task strikethrough.");
    }

    private static void CheckPanel(MainWindow window, AppearanceSettings expected)
    {
        // ScrollViewer templates are lazy on an unshown window. Lay out the
        // real subtree before inspecting task rows; do not create an HWND.
        RenderWindow(window, null, true);
        Border surface = window.Panel.Surface;
        SolidColorBrush fill = surface.Background as SolidColorBrush;
        Assert(fill != null, "The panel fill is not a solid color brush.");
        Color color = ParseColor(expected.BackgroundColor);
        Assert(fill.Color.R == color.R && fill.Color.G == color.G && fill.Color.B == color.B, "The panel background RGB differs from the selected value.");
        double expectedAlpha = 255 * (1 - expected.BackgroundTransparency / 100);
        Assert(Math.Abs(fill.Color.A - expectedAlpha) <= 1, "The panel alpha differs from background-only transparency.");
        Near(1, surface.Opacity, "Panel container opacity");
        Near(1, window.Opacity, "Window opacity");
        List<TextBlock> texts = Descendants(surface).OfType<TextBlock>().ToList();
        TextBlock pending = texts.Single(t => t.Text == "向李老师汇报研究进展");
        CheckTextColor(pending, expected.PrimaryTextColor, "Primary task color");
        CheckTextColor(texts.Single(t => t.Text == "待办清单"), expected.PrimaryTextColor, "Title color");
        CheckTextColor(window.Panel.DateText, expected.SecondaryTextColor, "Secondary date color");
        Near(1, pending.Opacity, "Pending text opacity");
        foreach (UIElement ancestor in Ancestors(pending).OfType<UIElement>())
            Near(1, ancestor.Opacity, "Pending text ancestor opacity");
    }

    private static Dictionary<string, string> TextAppearance(MainWindow window)
    {
        RenderWindow(window, null, true);
        List<TextBlock> texts = Descendants(window.Panel.Surface).OfType<TextBlock>().ToList();
        Dictionary<string, string> values = new Dictionary<string, string>();
        for (int i = 0; i < texts.Count; i++)
        {
            TextBlock text = texts[i];
            SolidColorBrush brush = text.Foreground as SolidColorBrush;
            values.Add(i.ToString(CultureInfo.InvariantCulture) + ":" + text.Text,
                (brush == null ? "null" : brush.Color.ToString()) + ":" + text.Opacity.ToString("R", CultureInfo.InvariantCulture));
        }
        return values;
    }

    private static void CheckTextColor(TextBlock text, string hex, string description)
    {
        SolidColorBrush brush = text.Foreground as SolidColorBrush;
        Assert(brush != null && brush.Color == ParseColor(hex) && brush.Color.A == 255, description + " does not match the opaque selected color.");
    }

    private static Color ParseColor(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

    private static void RenderWindow(Window window, string name, bool wallpaper)
    {
        FrameworkElement content = (FrameworkElement)window.Content;
        Assert(content != null, "The window has no renderable content.");
        window.Content = null;
        Grid canvas = null;
        try
        {
            TextElement.SetFontFamily(content, window.FontFamily);
            TextElement.SetFontSize(content, window.FontSize);
            TextElement.SetForeground(content, window.Foreground);
            content.Language = window.Language;
            content.UseLayoutRounding = window.UseLayoutRounding;
            content.SnapsToDevicePixels = window.SnapsToDevicePixels;
            double width = window.Width; double height = window.Height;
            canvas = new Grid { Width = width, Height = height, Background = wallpaper ?
                (Brush)new LinearGradientBrush(Color.FromRgb(174, 210, 221), Color.FromRgb(74, 130, 145), 90) : window.Background };
            canvas.Children.Add(content);
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
            canvas.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            canvas.UpdateLayout();
            Assert(content.ActualWidth > 0 && content.ActualHeight > 0, "Rendered content has empty layout dimensions.");
            if (name != null)
            {
                const double scale = 1.5;
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale),
                    (int)Math.Ceiling(height * scale), 144, 144, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Assert(pixels.Where((value, index) => index % 4 == 3).Any(alpha => alpha > 0), "Rendered PNG contains no visible pixels.");
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(Path.Combine(outputDirectory, name))) encoder.Save(stream);
                Report.AppendLine("  render " + name + ": " + bitmap.PixelWidth + "x" + bitmap.PixelHeight + " px at 144 dpi");
            }
            AssertNoHandle(window);
        }
        finally
        {
            if (canvas != null) canvas.Children.Clear();
            window.Content = content;
        }
    }

    private static string StateWithoutAppearance(TodoState state)
    {
        WidgetSettings settings = state.Settings;
        StringBuilder value = new StringBuilder();
        value.Append(state.Version).Append('|').Append(settings.Width.ToString("R", CultureInfo.InvariantCulture))
            .Append('|').Append(settings.Height.ToString("R", CultureInfo.InvariantCulture))
            .Append('|').Append(settings.Left).Append('|').Append(settings.Top)
            .Append('|').Append(settings.AutoExpand).Append('|').Append(settings.Collapsed).Append('|').Append(settings.CompletedCollapsed);
        foreach (TodoItem item in state.Items)
            value.Append('|').Append(item.Id).Append('|').Append(item.Text).Append('|').Append(item.IsCompleted)
                .Append('|').Append(item.CreatedAt.ToString("O")).Append('|').Append(item.DueDate.ToString("O"))
                .Append('|').Append(item.HasTime).Append('|').Append(item.CompletedAt).Append('|').Append(item.DeadlineSetAt);
        return value.ToString();
    }

    private static void Preview(MainWindow window, AppearanceSettings appearance) { Invoke(window, "PreviewAppearance", appearance); }
    private static bool Save(MainWindow window, AppearanceSettings appearance) { return (bool)Invoke(window, "TrySaveAppearance", appearance); }

    private static T Find<T>(Window window, string name) where T : DependencyObject
    {
        T value = Descendants((DependencyObject)window.Content).OfType<T>().SingleOrDefault(control => AutomationProperties.GetName(control) == name);
        Assert(value != null, "Missing automated control: " + name);
        return value;
    }

    private static void Click(Button button, bool allowUnshownDialogResult)
    {
        try { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button)); }
        catch (InvalidOperationException error)
        {
            // Only the Window.DialogResult setter is invalid on an intentionally
            // unshown test window; callback/validation failures must still fail.
            if (!allowUnshownDialogResult || error.StackTrace == null ||
                error.StackTrace.IndexOf("System.Windows.Window.set_DialogResult", StringComparison.Ordinal) < 0) throw;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        DependencyObject current = child;
        while ((current = VisualTreeHelper.GetParent(current)) != null) yield return current;
    }

    private static object Invoke(object instance, string name, params object[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (method == null) throw new InvalidOperationException("Missing test method: " + name);
        try { return method.Invoke(instance, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }

    private static void AssertNoHandle(Window window)
    {
        Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "An offscreen test unexpectedly created an HWND.");
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
