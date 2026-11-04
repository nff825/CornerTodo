using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CornerTodo;

internal static class DisplayModeTests
{
    private static int assertions;

    internal static void Run()
    {
        DefaultAndMenu();
        IndependentGrowthAndBaseline();
        SaveFailureAndBusyGuards();
        SavedModeAndChildren();
        FloatingEdgeCapsule();
        Console.WriteLine("PASS: " + assertions + " display-mode/menu/growth/rollback/edge assertions (unshown WPF windows, fake geometry and memory-only writes).");
    }

    private static void DefaultAndMenu()
    {
        using (Fixture fixture = new Fixture(false, false)) {
            ContextMenu menu = fixture.Menu;
            Open(menu);
            MenuItem desktop = Find(menu, "display-desktop"), floating = Find(menu, "display-floating");
            Check(desktop.IsChecked && !floating.IsChecked && !fixture.Window.Topmost,
                "New and upgraded users were opted into floating mode.");
            Click(floating);
            Check(!desktop.IsChecked && floating.IsChecked && fixture.Window.Topmost && fixture.State.AlwaysOnTop,
                "The actual menu did not select and save floating mode.");
            Open(menu);
            Check(fixture.State.AutoExpand && !fixture.State.FloatingAutoExpand && !Find(menu, "auto-expand").IsChecked,
                "Floating inherited desktop growth instead of starting compact.");
            Open(menu);
            Check(!Find(menu, "auto-expand").IsChecked, "Menu refresh did not reflect the active mode's growth setting.");
            Click(floating);
            Check(floating.IsChecked && !desktop.IsChecked, "Reselecting the current radio option left both modes unchecked.");
            Click(desktop); Open(menu);
            Check(desktop.IsChecked && !floating.IsChecked && !fixture.Window.Topmost && Find(menu, "auto-expand").IsChecked,
                "Returning to desktop lost the original display/growth selection.");
            Check(fixture.Window.CurrentState.Items.Single().Text == "display-fixture", "Display preferences changed task content.");
        }
    }

    private static void IndependentGrowthAndBaseline()
    {
        using (Fixture fixture = new Fixture(false, false)) {
            Rect baseline = fixture.Window.Interaction.BaselineBounds;
            UpdateExpansion(fixture.Window);
            Check(fixture.Host.Bounds.Width > baseline.Width, "The overdue desktop fixture did not enlarge.");
            Check(fixture.Window.TrySetAlwaysOnTop(true), "Could not enter floating mode.");
            Check(fixture.Host.Bounds.Width == baseline.Width && fixture.Host.Bounds.Height == baseline.Height,
                "First floating mode did not return to the ordinary size.");
            fixture.Window.SaveGeometry();
            Check(fixture.State.Width == 430 && fixture.State.Height == 475,
                "Switching from an enlarged panel persisted enlarged dimensions as the baseline.");
            MenuItem growth = Find(fixture.Menu, "auto-expand");
            growth.IsChecked = true; Click(growth);
            Check(fixture.State.FloatingAutoExpand && fixture.State.AutoExpand && fixture.Host.Bounds.Width > 430,
                "Floating growth did not take effect independently.");
            Check(fixture.Window.TrySetAlwaysOnTop(false), "Could not return to desktop.");
            growth.IsChecked = false; Click(growth);
            Check(!fixture.State.AutoExpand && fixture.State.FloatingAutoExpand && fixture.Host.Bounds.Width == 430,
                "Desktop growth changed floating preferences or retained expanded bounds.");
            Check(fixture.Window.TrySetAlwaysOnTop(true) && fixture.Host.Bounds.Width > 430,
                "Returning to floating did not restore the user's saved growth preference.");
            fixture.Window.SaveGeometry();
            Check(fixture.State.Width == 430 && fixture.State.Height == 475 &&
                fixture.Window.Interaction.BaselineBounds == baseline, "Mode switching changed the ordinary-size baseline.");
        }
    }

    private static void SaveFailureAndBusyGuards()
    {
        using (Fixture fixture = new Fixture(false, false)) {
            Rect previous = fixture.Host.Bounds;
            fixture.Store.Fail = true;
            Click(Find(fixture.Menu, "display-floating"));
            Check(!fixture.State.AlwaysOnTop && !fixture.Window.Topmost && Find(fixture.Menu, "display-desktop").IsChecked,
                "A failed save left the floating property or menu changed.");
            Check(fixture.Host.Bounds == previous && fixture.Window.Panel.StatusText.Text.Contains("未保存"),
                "Failed saving lost geometry or failed to explain the result.");
            fixture.Store.Fail = false;
            Check(fixture.Window.TrySetAlwaysOnTop(true), "A failed save prevented retry.");
            fixture.Store.Fail = true;
            Check(!fixture.Window.TrySetAlwaysOnTop(false) && fixture.State.AlwaysOnTop && fixture.Window.Topmost,
                "Failed return-to-desktop saving did not restore floating.");
            fixture.Store.Fail = false;
            fixture.Interaction.EditorOpen = true;
            int writes = fixture.Store.Writes;
            Check(!fixture.Window.TrySetAlwaysOnTop(false) && fixture.Store.Writes == writes && fixture.Window.Topmost,
                "Display mode changed while a task editor was open.");
            fixture.Interaction.EditorOpen = false;
            fixture.Window.HostLost = true;
            Check(!fixture.Window.TrySetAlwaysOnTop(false) && fixture.Store.Writes == writes,
                "A lost native host changed display settings.");
            fixture.Window.HostLost = false;
        }
    }

    private static void SavedModeAndChildren()
    {
        using (Fixture fixture = new Fixture(true, false)) {
            Check(fixture.Window.Topmost && fixture.State.AlwaysOnTop && !fixture.State.EffectiveAutoExpand,
                "A saved floating mode was not applied during construction.");
            Window child = new Window();
            try {
                fixture.Window.PrepareChildWindow(child);
                Check(child.Topmost && child.Owner == null && NoHandle(child),
                    "Preparing an unshown floating editor created a native window or failed its layer.");
                Check(fixture.Window.TrySetAlwaysOnTop(false), "Saved floating mode could not switch to desktop.");
                fixture.Window.PrepareChildWindow(child);
                Check(!child.Topmost && NoHandle(child), "A desktop editor retained floating Topmost.");
            }
            finally { child.Close(); }
        }
    }

    private static void FloatingEdgeCapsule()
    {
        using (Fixture fixture = new Fixture(true, true)) {
            fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            fixture.Time = fixture.Time.AddMilliseconds(800); fixture.Window.Interaction.PollEdgeAutoHide();
            Check(fixture.Window.Interaction.IsEdgeHidden && fixture.Window.Topmost && fixture.Host.Bounds.Width == 48,
                "Floating mode did not preserve the existing edge capsule interaction.");
            fixture.Window.SaveGeometry();
            Check(fixture.State.Width == 430 && fixture.State.Height == 475, "The capsule size was persisted as ordinary size.");
            Check(fixture.Window.TrySetAlwaysOnTop(false) && fixture.Window.Interaction.IsEdgeHidden && !fixture.Window.Topmost,
                "Switching a hidden capsule forced an expansion or kept it topmost.");
            Check(fixture.Window.TrySetAlwaysOnTop(true) && fixture.Window.Topmost && fixture.Window.Interaction.IsEdgeHidden,
                "Switching back lost the capsule or floating mode.");
            fixture.Window.Interaction.RevealFromEdge(0, false);
            Check(!fixture.Window.Interaction.IsEdgeHidden && fixture.Host.Bounds.Width == 430 && fixture.Window.Topmost,
                "The floating capsule did not reveal to ordinary dimensions.");
        }
    }

    private static MenuItem Find(ContextMenu menu, string tag)
    {
        return menu.Items.OfType<MenuItem>().SelectMany(item => new[] { item }.Concat(item.Items.OfType<MenuItem>()))
            .Single(item => Object.Equals(item.Tag, tag));
    }
    private static void Open(ContextMenu menu) { menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent)); }
    private static void Click(MenuItem item) { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
    private static void UpdateExpansion(MainWindow window)
    { typeof(MainWindow).GetMethod("UpdateExpansion", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true, true }); }
    private static bool NoHandle(Window window) { return new WindowInteropHelper(window).Handle == IntPtr.Zero; }
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }

    private sealed class MemoryStore : ITodoStore
    {
        internal bool Fail;
        internal int Writes;
        public void Save(TodoState state) { Writes++; if (Fail) throw new IOException("Injected display-settings write failure."); }
    }
    private sealed class Screen : IPanelHost
    {
        internal Rect Bounds = new Rect(2106, 18, 430, 475);
        public Rect GetBounds() { return Bounds; }
        public void SetBounds(Rect value) { Bounds = value; }
        public Rect WorkArea() { return new Rect(0, 0, 2560, 1528); }
    }
    private sealed class ForbiddenStartup : IStartupManager
    {
        public StartupRegistration Inspect() { throw new InvalidOperationException("Preview accessed startup registration."); }
        public void Enable() { throw new InvalidOperationException("Preview changed startup registration."); }
        public void Disable() { throw new InvalidOperationException("Preview changed startup registration."); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MemoryStore Store = new MemoryStore();
        internal readonly Screen Host = new Screen();
        internal readonly ApplicationInteraction Interaction = new ApplicationInteraction();
        internal readonly MainWindow Window;
        internal DateTime Time = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
        internal WidgetSettings State { get { return Window.CurrentState.Settings; } }
        internal ContextMenu Menu { get { return ((FrameworkElement)Window.Content).ContextMenu; } }
        internal Fixture(bool floating, bool edge)
        {
            var state = new TodoState();
            state.Settings.Left = 2106; state.Settings.Top = 18; state.Settings.AlwaysOnTop = floating; state.Settings.EdgeAutoHide = edge;
            state.Items.Add(new TodoItem { Id = "display-fixture", Text = "display-fixture", HasDeadline = true, HasTime = true,
                DueDate = DateTime.Now.AddHours(-1), CreatedAt = DateTime.Now.AddDays(-1), DeadlineSetAt = DateTime.Now.AddDays(-1) });
            var session = new TodoSession(Store, state, false);
            var store = new TodoStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "display-test-unwritten-" + Guid.NewGuid().ToString("N")));
            Window = new MainWindow(store, session, true, true, new ForbiddenStartup(), Host, Interaction, delegate { return Time; });
            Window.Interaction.Initialize(state.Settings);
            // Real startup applies expansion after initialization; use the same path without Show/Loaded.
            UpdateExpansion(Window);
        }
        public void Dispose() { Check(NoHandle(Window), "A display-mode test created an HWND."); Window.Dispose(); Window.Close(); }
    }
}
