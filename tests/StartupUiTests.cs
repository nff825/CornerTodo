using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CornerTodo;

internal static class StartupUiTests
{
    private static int assertions;
    public static void Run()
    {
        Application ownedApplication = Application.Current == null ? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown } : null;
        string root = Path.Combine(Path.GetTempPath(), "CornerTodo-StartupUi-" + Guid.NewGuid().ToString("N"));
        var windows = new List<MainWindow>();
        try {
            var store = new TodoStore(root);
            var state = new TodoState(); state.Settings.Appearance = AppearanceSettings.CreateDark();
            state.Items.Add(new TodoItem { Id = "startup-fixture", Text = "界面测试示例", CreatedAt = DateTime.Today, DueDate = DateTime.Today.AddDays(7), DeadlineSetAt = DateTime.Today });
            store.Save(state);
            byte[] original = File.ReadAllBytes(store.DataFilePath);
            var manager = new FakeStartupManager();
            var window = new MainWindow(store, state, true, false, manager); windows.Add(window);
            Check(manager.EnableCalls == 0 && manager.DisableCalls == 0 && manager.InspectCalls == 0, "Constructing the window must never register or touch the startup folder.");
            var menu = ((FrameworkElement)window.Content).ContextMenu;
            MenuItem enabled = Find(menu, "login-startup"), repair = Find(menu, "repair-startup");
            Open(menu);
            Check(!enabled.IsChecked && enabled.IsEnabled && repair.Visibility == Visibility.Collapsed, "New and upgraded users must see startup off.");
            Toggle(enabled);
            Check(enabled.IsChecked && manager.EnableCalls == 1 && manager.Registration.MatchesCurrent, "An explicit user enable did not register the current location.");
            Open(menu);
            Check(manager.EnableCalls == 1 && manager.DisableCalls == 0, "Opening the menu silently rewrote the registration.");
            Toggle(enabled);
            Check(!enabled.IsChecked && manager.DisableCalls == 1 && !manager.Registration.Exists, "Disable did not remove the selected startup registration.");
            manager.FailEnable = true; Toggle(enabled);
            Check(!enabled.IsChecked && !manager.Registration.Exists && TextContains(window, "开机自启设置未更改"), "Failed enable must restore the prior check and explain the failure.");
            manager.FailEnable = false;
            manager.Registration = new StartupRegistration(true, true, "fixture.exe", "--startup", true); Open(menu);
            manager.FailDisable = true; Toggle(enabled);
            Check(enabled.IsChecked && manager.Registration.Exists, "Failed disable incorrectly claimed startup was off.");
            manager.FailDisable = false;
            manager.FailInspect = true; Open(menu);
            Check(!enabled.IsEnabled && enabled.IsChecked && enabled.ToolTip.ToString().Contains("无法检查"), "Unreadable registration must not display a false successful change.");
            manager.FailInspect = false; Open(menu);
            Check(enabled.IsEnabled && enabled.IsChecked, "A temporary inspection error prevented future recovery.");
            manager.Registration = new StartupRegistration(true, false, "old-location.exe", "old data", true); Open(menu);
            int enablesBefore = manager.EnableCalls;
            Check(enabled.IsChecked && repair.Visibility == Visibility.Visible && manager.EnableCalls == enablesBefore, "A moved portable app must show an explicit repair action without repairing itself.");
            manager.FailEnable = true; Click(repair);
            Check(enabled.IsChecked && repair.Visibility == Visibility.Visible && !manager.Registration.MatchesCurrent, "Failed migration discarded the old registration.");
            manager.FailEnable = false; Click(repair);
            Check(manager.Registration.MatchesCurrent && repair.Visibility == Visibility.Collapsed, "Explicit path repair did not update the registration.");
            manager.Registration = new StartupRegistration(true, false, "other-program.exe", "", false); Open(menu);
            Check(!enabled.IsEnabled && !enabled.IsChecked && repair.Visibility == Visibility.Collapsed, "An unrelated or damaged shortcut must not be offered for overwrite or deletion.");
            Check(File.ReadAllBytes(store.DataFilePath).SequenceEqual(original), "Startup preferences changed portable task data or appearance settings.");
            Check(window.CurrentState.Items.Single().Id == "startup-fixture" && window.CurrentState.Settings.Appearance.Matches(AppearanceSettings.CreateDark()), "Startup configuration changed live task or appearance state.");
            var previewManager = new FakeStartupManager();
            var preview = new MainWindow(store, state, true, true, previewManager); windows.Add(preview);
            ContextMenu previewMenu = ((FrameworkElement)preview.Content).ContextMenu; Open(previewMenu);
            Check(!Find(previewMenu, "login-startup").IsEnabled && !preview.TrySetStartup(true), "Preview mode must not allow changes to Windows startup.");
            Check(previewManager.InspectCalls == 0 && previewManager.EnableCalls == 0 && previewManager.DisableCalls == 0, "Preview mode accessed real startup state.");
            foreach (MainWindow testedWindow in windows)
                Check(new WindowInteropHelper(testedWindow).Handle == IntPtr.Zero, "The isolated UI test unexpectedly opened a native window.");
            Console.WriteLine("PASS: " + assertions + " startup-menu assertions (fake registration; production data and Windows startup untouched).");
        }
        finally {
            foreach (MainWindow window in windows) window.Close();
            if (ownedApplication != null) ownedApplication.Shutdown();
            string full = Path.GetFullPath(root), prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("CornerTodo-StartupUi-", StringComparison.Ordinal))
                if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }

    private static MenuItem Find(ContextMenu menu, string tag) { return menu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, tag)); }
    private static void Open(ContextMenu menu) { menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent)); }
    private static void Click(MenuItem item) { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
    private static void Toggle(MenuItem item) { item.IsChecked = !item.IsChecked; Click(item); }
    private static bool TextContains(DependencyObject element, string text)
    {
        TextBlock block = element as TextBlock;
        if (block != null && block.Text.Contains(text)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) if (TextContains(VisualTreeHelper.GetChild(element, i), text)) return true;
        // A window may not have applied its template before the offscreen test.
        Window window = element as Window;
        if (window != null && VisualTreeHelper.GetChildrenCount(window) == 0 && window.Content is DependencyObject)
            return TextContains((DependencyObject)window.Content, text);
        return false;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); assertions++; }

    private sealed class FakeStartupManager : IStartupManager
    {
        public StartupRegistration Registration = new StartupRegistration(false, false, "", "", false);
        public int InspectCalls, EnableCalls, DisableCalls;
        public bool FailInspect, FailEnable, FailDisable;
        public StartupRegistration Inspect() { InspectCalls++; if (FailInspect) throw new IOException("fixture read failure"); return Registration; }
        public void Enable() { EnableCalls++; if (FailEnable) throw new IOException("fixture write failure"); Registration = new StartupRegistration(true, true, "fixture.exe", "--startup", true); }
        public void Disable() { DisableCalls++; if (FailDisable) throw new IOException("fixture delete failure"); Registration = new StartupRegistration(false, false, "", "", false); }
    }
}
