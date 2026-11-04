using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CornerTodo;

internal static class ReminderControllerTests
{
    private static int checks;
    private static readonly DateTime Now = new DateTime(2026, 11, 19, 14, 30, 0);
    [STAThread]
    public static int Main(string[] args)
    {
        Application app = null;
        try {
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAndDismiss(); BlockingAndReconcile(); SaveAndPresentationFailures(); ClaimFailureDoesNotBlockRetry(); ActionsAndStalePlans(); Disposal(); MainWindowWiring();
            string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reminder-qa");
            ReminderWindowTests.Run(output);
            Console.WriteLine("PASS: " + checks + " reminder controller/timer/main-window assertions; fake presenters, no shown windows or production data.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { if (app != null) app.Shutdown(); }
    }

    internal static TodoItem Item(string id)
    {
        return new TodoItem { Id = id, Text = "向李老师汇报研究进展", CreatedAt = Now.AddDays(-3), DueDate = Now.AddDays(1).AddHours(2).AddMinutes(30),
            HasDeadline = true, HasTime = true, ReminderEnabled = true, ReminderMinutesBefore = 1590,
            ReminderPlanId = "plan-" + id, ReminderSetAt = Now.AddDays(-2) };
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }

    private static void MergeAndDismiss()
    {
        using (var fixture = new Fixture()) {
            fixture.Controller.CheckNow(); Check(fixture.Claims == 0, "Unstarted controller claimed a reminder.");
            fixture.AddDue("one"); fixture.AddDue("two"); fixture.Controller.Start();
            Check(fixture.Timer.Starts == 1 && fixture.Presenter.Visible && fixture.Presenter.Items.Count == 2 && fixture.Changed == 1,
                "Simultaneous reminders were not merged into one presentation.");
            fixture.Controller.Start(); fixture.Timer.Fire();
            Check(fixture.Timer.Starts == 1 && fixture.Presenter.Items.Count == 2, "Repeated timer starts duplicated cards.");
            fixture.Presenter.UserDismiss(); fixture.Timer.Fire();
            Check(!fixture.Presenter.Visible && fixture.Controller.ActiveCount == 0 && fixture.State.Items.All(i => !i.IsCompleted),
                "Dismissing a reminder completed a task or replayed its handled notification.");
            fixture.AddDue("three"); fixture.Timer.Fire();
            Check(fixture.Presenter.Items.Count == 1 && fixture.Presenter.Items[0].Id == "three", "New reminders replayed an old dismissed card.");
        }
    }
    private static void BlockingAndReconcile()
    {
        using (var fixture = new Fixture()) {
            fixture.AddDue("one"); fixture.Blocked = true; fixture.Controller.Start();
            Check(fixture.Claims == 0 && !fixture.Presenter.Visible, "An app-owned modal allowed claiming/presentation.");
            fixture.Blocked = false; fixture.Timer.Fire(); Check(fixture.Presenter.Visible, "A reminder did not resume after a modal closed.");
            fixture.Blocked = true; fixture.Controller.Refresh(); Check(!fixture.Presenter.Visible && fixture.Controller.ActiveCount == 1, "Hiding for an editor discarded the reminder.");
            fixture.Blocked = false; fixture.Timer.Fire(); Check(fixture.Presenter.Visible && fixture.Controller.ActiveCount == 1, "Deferred reminder was duplicated or lost.");
            fixture.State.Items[0].Text = "更新后的文字"; fixture.Controller.Refresh();
            Check(fixture.Presenter.Items.Single().Text == "更新后的文字", "A text-only edit left stale notification content.");
            fixture.State.Items[0].ReminderPlanId = "rescheduled"; fixture.Controller.Refresh();
            Check(!fixture.Presenter.Visible && fixture.Controller.ActiveCount == 0, "Rescheduling left a stale notification visible.");
            fixture.AddDue("two"); fixture.Timer.Fire(); fixture.State.Items.Last().IsCompleted = true; fixture.Controller.Refresh();
            Check(fixture.Controller.ActiveCount == 0, "Completing a task left its reminder visible.");
            fixture.AddDue("three"); fixture.Timer.Fire(); fixture.State.Items.RemoveAll(i => i.Id == "three"); fixture.Controller.Refresh();
            Check(fixture.Controller.ActiveCount == 0, "Deleting a task left its reminder visible.");
            fixture.AddDue("four"); fixture.Timer.Fire(); fixture.State.Items.Last().ReminderEnabled = false; fixture.Controller.Refresh();
            Check(fixture.Controller.ActiveCount == 0, "Disabling a reminder left its card visible.");
        }
    }
    private static void ClaimFailureDoesNotBlockRetry()
    {
        using (var fixture = new Fixture()) {
            fixture.AddDue("committed"); fixture.Presenter.FailPresent = true; fixture.Controller.Start();
            Check(fixture.Controller.ActiveCount == 1 && !fixture.Presenter.Visible && fixture.Errors == 1,
                "A presentation failure did not retain the committed reminder for retry.");
            fixture.AddDue("uncommitted"); fixture.Presenter.FailPresent = false; fixture.FailClaim = true;
            fixture.Timer.Fire(); fixture.Timer.Fire();
            Check(fixture.Presenter.Visible && fixture.Presenter.Items.Single().Id == "committed" &&
                fixture.Due.Count == 1 && fixture.Controller.ActiveCount == 1 && fixture.Errors == 3,
                "New receipt failures blocked the committed card retry or exposed uncommitted reminders.");
            fixture.FailClaim = false; fixture.Timer.Fire();
            Check(fixture.Presenter.Items.Count == 2 && fixture.Due.Count == 0 && fixture.Controller.ActiveCount == 2,
                "Receipt saving recovery lost or duplicated either reminder.");
        }
    }

    private static void SaveAndPresentationFailures()
    {
        using (var fixture = new Fixture()) {
            fixture.AddDue("one"); fixture.FailClaim = true; fixture.Controller.Start();
            Check(!fixture.Presenter.Visible && fixture.Errors == 1 && fixture.Due.Count == 1, "A failed durable claim displayed or discarded a reminder.");
            fixture.FailClaim = false; fixture.Presenter.FailPresent = true; fixture.Timer.Fire();
            Check(fixture.Controller.ActiveCount == 1 && fixture.Errors == 2, "A transient presentation error lost the in-memory claimed reminder.");
            fixture.Presenter.FailPresent = false; fixture.Timer.Fire();
            Check(fixture.Presenter.Visible && fixture.Presenter.Items.Count == 1, "A presentation retry did not reuse the claimed reminder.");
            fixture.FailComplete = true; fixture.Presenter.Complete();
            Check(!fixture.State.Items[0].IsCompleted && fixture.Presenter.Error != null && fixture.Presenter.Visible,
                "A failed completion hid the card or failed to show a retry message.");
            fixture.FailComplete = false; fixture.Presenter.Complete(); Check(fixture.State.Items[0].IsCompleted && !fixture.Presenter.Visible, "Completion could not be retried.");
        }
    }
    private static void ActionsAndStalePlans()
    {
        using (var fixture = new Fixture()) {
            fixture.AddDue("one"); fixture.Controller.Start();
            Action<TodoItem> oldComplete = fixture.Presenter.CompleteAction; TodoItem old = fixture.Presenter.Items[0];
            fixture.Presenter.View(); Check(fixture.Views == 1 && !fixture.State.Items[0].IsCompleted, "Viewing a task completed it.");
            fixture.State.Items[0].ReminderPlanId = "new-plan"; oldComplete(old);
            Check(!fixture.State.Items[0].IsCompleted && fixture.Controller.ActiveCount == 0, "An old notification action applied to a changed reminder plan.");
        }
    }
    private static void Disposal()
    {
        var fixture = new Fixture(); fixture.AddDue("one"); fixture.Controller.Start();
        Action<TodoItem> stale = fixture.Presenter.CompleteAction; TodoItem item = fixture.Presenter.Items[0];
        int claims = fixture.Claims; fixture.Controller.Dispose(); fixture.Timer.Fire(); stale(item); fixture.Controller.CheckNow(); fixture.Controller.Refresh();
        Check(fixture.Claims == claims && !fixture.State.Items[0].IsCompleted && fixture.Timer.Disposed && fixture.Presenter.Disposed,
            "Disposal left a timer, card action or presenter active.");
        fixture.Controller.Dispose();
    }
    private static void MainWindowWiring()
    {
        TodoState state = new TodoState(); state.Items.Add(Item("main"));
        var store = new MemoryStore(); var session = new TodoSession(store, state, false, delegate { return Now; });
        var interaction = new ApplicationInteraction(); var presenter = new Presenter(); var timer = new Timer();
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reminder-unwritten-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(new TodoStore(path), session, true, true, new ForbiddenStartup(), new Screen(), interaction, null, presenter, timer);
        try {
            interaction.EditorOpen = true; window.Reminders.Start();
            Check(!presenter.Visible && store.Writes == 0, "The real main-window editor guard did not defer reminders.");
            interaction.EditorOpen = false; timer.Fire();
            Check(presenter.Visible && store.Writes == 1 && window.CurrentState.Items[0].ReminderHandledAt.HasValue,
                "Main-window reminder wiring did not persist and present a due reminder.");
            Check(!window.CurrentState.Settings.AlwaysOnTop && !window.Topmost, "A reminder changed the main panel's display mode.");
            typeof(MainWindow).GetField("menuOpen", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
            window.Reminders.Refresh(); Check(!presenter.Visible, "An open menu failed to defer the card.");
            typeof(MainWindow).GetField("menuOpen", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, false);
            timer.Fire(); presenter.Complete();
            Check(window.CurrentState.Items[0].IsCompleted && !presenter.Visible, "The real completion action did not update the session.");
            Check(new WindowInteropHelper(window).Handle == IntPtr.Zero && !Directory.Exists(path), "Reminder wiring created an HWND or data directory.");
        }
        finally { window.Dispose(); window.Close(); }
        Check(timer.Disposed && presenter.Disposed, "Main-window disposal left reminder resources alive.");
    }

    private sealed class MemoryStore : ITodoStore { internal int Writes; public void Save(TodoState state) { Writes++; } }
    private sealed class ForbiddenStartup : IStartupManager
    {
        public StartupRegistration Inspect() { throw new InvalidOperationException("Unexpected startup access."); }
        public void Enable() { throw new InvalidOperationException("Unexpected startup change."); }
        public void Disable() { throw new InvalidOperationException("Unexpected startup change."); }
    }
    private sealed class Screen : IPanelHost
    {
        private Rect bounds = new Rect(1100, 18, 430, 475);
        public Rect GetBounds() { return bounds; } public void SetBounds(Rect value) { bounds = value; }
        public Rect WorkArea() { return new Rect(0, 0, 1920, 1080); }
    }
    private sealed class Timer : IReminderTimer
    {
        public event Action Tick; internal int Starts; internal bool Disposed;
        public void Start() { Starts++; } public void Stop() { }
        public void Dispose() { Disposed = true; Tick = null; }
        internal void Fire() { Action action = Tick; if (action != null) action(); }
    }
    private sealed class Presenter : IReminderPresenter
    {
        internal IList<TodoItem> Items = new List<TodoItem>(); internal bool Visible, Disposed, FailPresent;
        internal Action<TodoItem> ViewAction, CompleteAction; private Action dismissed; internal string Error;
        public void Present(IList<TodoItem> items, Action<TodoItem> view, Action<TodoItem> complete, Action close)
        {
            if (FailPresent) throw new InvalidOperationException("Injected presentation failure.");
            Items = items; ViewAction = view; CompleteAction = complete; dismissed = close; Visible = true;
        }
        public void Hide() { Visible = false; } public void Clear() { Visible = false; Items = new List<TodoItem>(); }
        public void ShowError(string text) { Error = text; } public void Dispose() { Disposed = true; Clear(); }
        internal void View() { ViewAction(Items[0]); } internal void Complete() { CompleteAction(Items[0]); }
        internal void UserDismiss() { Visible = false; dismissed(); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly TodoState State = new TodoState(); internal readonly List<TodoItem> Due = new List<TodoItem>();
        internal readonly Presenter Presenter = new Presenter(); internal readonly Timer Timer = new Timer(); internal readonly ReminderController Controller;
        internal bool Blocked, FailClaim, FailComplete; internal int Claims, Changed, Errors, Views;
        internal Fixture()
        {
            Controller = new ReminderController(delegate {
                Claims++; if (FailClaim) throw new IOException("Injected receipt save failure.");
                var result = Due.Select(TodoStateCopy.Copy).ToList(); Due.Clear(); return result;
            }, delegate { return TodoStateCopy.Copy(State); }, delegate { return Blocked; }, delegate { Changed++; },
            delegate { Views++; }, delegate(TodoItem item) { if (FailComplete) return false; State.Items.Single(i => i.Id == item.Id).IsCompleted = true; return true; },
            delegate { Errors++; }, Presenter, Timer);
        }
        internal void AddDue(string id) { TodoItem item = Item(id); State.Items.Add(item); Due.Add(item); }
        public void Dispose() { Controller.Dispose(); }
    }
}
