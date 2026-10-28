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
            ValidateBackgroundOnlyTransparency();
            ValidateAttentionTheme();
            ValidatePreviewSaveAndFailure();
            ValidateEditorInteractions();
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
                TextBlock due = Descendants(row).OfType<TextBlock>().Single(t => t != text);
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
            Preview(window, draft);
            CheckPanel(window, draft);
            Assert(window.CurrentState.Settings.Appearance.Matches(committed), "Preview replaced committed appearance state.");
            Assert(File.ReadAllBytes(store.DataFilePath).SequenceEqual(originalBytes), "Preview wrote the data file.");
            Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Preview changed a task, deadline or baseline setting.");
            draft.BackgroundColor = "#112233";
            Assert(window.CurrentState.Settings.Appearance.Matches(committed), "The draft aliases committed settings.");
            Invoke(window, "Render");
            CheckPanel(window, AppearanceSettings.CreateDark());

            // Exercise the same restoration entry point used by the editor's finally path.
            Invoke(window, "CancelAppearancePreview");
            CheckPanel(window, committed);
            Assert(File.ReadAllBytes(store.DataFilePath).SequenceEqual(originalBytes), "Restoring a preview wrote data.");

            AppearanceSettings selected = AppearanceSettings.CreateDark();
            selected.BackgroundTransparency = 37;
            Assert(Save(window, selected), "Saving a valid appearance failed.");
            Assert(window.CurrentState.Settings.Appearance.Matches(selected), "Successful save did not commit the selected appearance.");
            Assert(store.Load().Settings.Appearance.Matches(selected), "Appearance did not survive loading from disk.");
            Assert(!Object.ReferenceEquals(selected, window.CurrentState.Settings.Appearance), "Saved appearance shares the mutable draft object.");
            Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Saving appearance changed tasks or baseline geometry.");
            Assert(File.Exists(store.DataFilePath + ".bak"), "Saving appearance did not retain the previous data backup.");
            CheckPanel(window, selected);

            byte[] savedBytes = File.ReadAllBytes(store.DataFilePath);
            AppearanceSettings saved = window.CurrentState.Settings.Appearance.Clone();
            AppearanceSettings rejected = AppearanceSettings.CreateLight(); rejected.BackgroundColor = "#EEDDCC";
            Preview(window, rejected);
            using (FileStream locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert(!Save(window, rejected), "Saving over a nonreplaceable data file unexpectedly succeeded.");
                Assert(window.CurrentState.Settings.Appearance.Matches(saved), "Failed save replaced committed appearance.");
                Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Failed save changed tasks or baseline geometry.");
                Assert(File.ReadAllBytes(store.DataFilePath).SequenceEqual(savedBytes), "Failed save altered the previous data file.");
                CheckPanel(window, saved);
            }
            Assert(store.Load().Settings.Appearance.Matches(saved), "Failed save changed the reloadable appearance.");
            Invoke(window, "CancelAppearancePreview");
            CheckPanel(window, saved);

            TodoState clone = window.CurrentState;
            Assert(StateWithoutAppearance(clone) == unchanged, "A current-state snapshot lost task or geometry fields.");
            Assert(!Object.ReferenceEquals(clone.Settings.Appearance, window.CurrentState.Settings.Appearance), "A current-state snapshot shares appearance settings.");
            clone.Settings.Appearance.BackgroundColor = "#010203";
            clone.Settings.Width = 1;
            clone.Items[0].Text = "snapshot-only edit";
            clone.Items.Clear();
            Assert(window.CurrentState.Settings.Appearance.Matches(saved), "Changing a snapshot mutated the committed appearance.");
            Assert(StateWithoutAppearance(window.CurrentState) == unchanged, "Changing a snapshot mutated committed tasks or baseline geometry.");
            RenderWindow(window, "saved-dark.png", true);
            AssertNoHandle(window);
        }
        finally { window.Close(); }
        Report.AppendLine("Persistence: preview and restore leave bytes unchanged; save persists an independent value; locked-file failure rolls back display/model/disk; current-state snapshots isolate tasks, appearance and baseline fields.");
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
            Click(Find<Button>(failed, "保存外观设置"), false);
            Assert(failedCalls == 1, "Save failure callback was not invoked exactly once.");
            Assert(failed.CurrentAppearance.Matches(AppearanceSettings.CreateDark()), "Failed editor save discarded the editable draft.");
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
            Click(Find<Button>(cancelled, "取消外观设置"), true);
            Assert(cancelSaves == 0, "Cancel invoked the save callback.");
            Assert(original.Matches(AppearanceSettings.CreateLight()), "Cancel mutated the committed input.");
            AssertNoHandle(cancelled);
        }
        finally { cancelled.Close(); }
        Report.AppendLine("Editor: preset buttons, slider, valid/invalid HEX, save success/failure and cancel callbacks passed on unshown windows.");
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
