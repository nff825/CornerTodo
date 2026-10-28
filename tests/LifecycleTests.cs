using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CornerTodo;

internal static class LifecycleTests
{
    private static int passed;
    private static int assertions;

    [STAThread]
    public static int Main()
    {
        try
        {
            Run("Interaction scopes nest and exit requests remain explicit", InteractionScopes);
            Run("First presentation preserves state and automatic launch never activates", FirstPresentation);
            Run("Explicit exit saves, detaches and closes once before shutdown", ExplicitExit);
            Run("An ordinary window close exits without scheduling recovery", OrdinaryClose);
            Run("Editing and nested modal work protect exit and normal closing", BusyExit);
            Run("Only the current lost window schedules one two-second recovery", RecoveryIdentity);
            Run("Recovery reads the latest committed state when rebuilding", LatestRecoveryState);
            Run("Recovery waits for editing and modal work to finish", BusyRecovery);
            Run("Repeated restore requests during recovery apply once to the replacement", PendingRestore);
            Run("Exit invalidates a scheduled recovery and queued restore", ExitDuringRecovery);
            Run("Disposal cancels work, removes callbacks and closes without shutdown", Disposal);
            Run("Late events and recovery callbacks cannot affect a replacement", StaleCallbacks);
            Run("Initial show failure cleans up and remains visible to the composition root", ShowFailure);
            Run("Recovery construction failure stops the application with code one", RecoveryFailure);
            Run("An unexpected closed event recovers instead of exiting", UnexpectedClose);
            Console.WriteLine("PASS: " + passed + " application-lifecycle groups, " + assertions + " assertions.");
            LifecycleUiTests.Run();
            Console.WriteLine("Core checks used fake windows and manual scheduling; UI wiring used an unshown real MainWindow. No HWND, process, Data, Startup or desktop attachment was created.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    private static void InteractionScopes()
    {
        var interaction = new ApplicationInteraction();
        Assert(!interaction.EditorOpen && !interaction.IsBusy, "A new interaction guard was busy.");
        IDisposable outer = interaction.EnterModal();
        IDisposable inner = interaction.EnterModal();
        Assert(interaction.IsBusy && !interaction.EditorOpen, "Modal work incorrectly changed the editor flag.");
        outer.Dispose(); outer.Dispose();
        Assert(interaction.IsBusy, "Disposing an outer scope released another active modal scope.");
        inner.Dispose();
        Assert(!interaction.IsBusy, "All disposed modal scopes left the guard busy.");
        interaction.EditorOpen = true;
        Assert(interaction.IsBusy, "An open editor did not block lifecycle work.");
        using (interaction.EnterModal())
        {
            interaction.EditorOpen = false;
            Assert(interaction.IsBusy, "Clearing the editor flag released a modal scope.");
        }
        Assert(!interaction.IsBusy, "The guard did not become idle after editing and modal work.");
        int requests = 0;
        interaction.ExitRequested += delegate { requests++; };
        interaction.RequestExit();
        Assert(requests == 1, "The interaction guard did not forward one explicit exit request.");
    }

    private static void FirstPresentation()
    {
        foreach (bool startup in new[] { false, true })
        {
            using (var fixture = new Fixture())
            {
                TodoState state = State("initial");
                state.Settings.Collapsed = true;
                fixture.Controller.Start(state, startup);
                FakeWindow window = fixture.Windows.Single();
                Assert(window.ShowCalls == 1 && fixture.MainWindows.Single() == window, "Initial window was not assigned and shown exactly once.");
                Assert(window.ShowActivated == !startup, "Automatic launch activated the window or ordinary launch changed the factory policy.");
                Assert(window.State.Settings.Collapsed && window.State.Settings.Left == 1170 && window.State.Settings.Top == 18,
                    "Initial presentation changed saved collapse or position.");
                Assert(window.RestoreCalls == 0 && window.SaveCalls == 0, "Initial presentation restored or saved a panel unnecessarily.");
                Assert(fixture.Log.Take(3).SequenceEqual(new[] { "create:1", "main:1", "show:1" }), "Initial construction, assignment and presentation were out of order.");
                Assert(fixture.Scheduler.Tickets.Count == 0 && fixture.ShutdownCodes.Count == 0, "Initial presentation scheduled recovery or shutdown.");
            }
        }
    }

    private static void ExplicitExit()
    {
        using (var fixture = Started())
        {
            FakeWindow window = fixture.Windows.Single(); fixture.Log.Clear();
            fixture.Interaction.RequestExit();
            Assert(fixture.Log.SequenceEqual(new[] { "save:1", "dispose:1", "close:1", "shutdown:0" }), "Explicit exit did not save, detach, close and shut down in order.");
            Assert(window.SaveCalls == 1 && window.DisposeCalls == 1 && window.IsClosed, "Explicit exit did not clean up once.");
            Assert(window.HandlerCount == 0, "Exit retained window lifecycle subscriptions.");
            fixture.Controller.Exit(); fixture.Controller.Fail(); fixture.Interaction.RequestExit();
            Assert(fixture.ShutdownCodes.SequenceEqual(new[] { 0 }) && window.CloseCalls == 1, "A repeated exit caused another close or shutdown.");
        }
    }

    private static void OrdinaryClose()
    {
        using (var fixture = Started())
        {
            FakeWindow window = fixture.Windows.Single();
            window.Close();
            Assert(window.IsClosed && window.SaveCalls == 1, "An ordinary close did not save the baseline once.");
            Assert(fixture.ShutdownCodes.SequenceEqual(new[] { 0 }), "An ordinary close did not shut down successfully once.");
            Assert(fixture.Scheduler.Tickets.Count == 0, "An ordinary close scheduled recovery.");
            Assert(window.HandlerCount == 0, "An ordinarily closed window retained handlers.");
        }
    }

    private static void BusyExit()
    {
        using (var fixture = Started())
        {
            FakeWindow window = fixture.Windows.Single();
            fixture.Interaction.EditorOpen = true;
            fixture.Controller.Exit(); window.Close();
            Assert(!window.IsClosed && window.SaveCalls == 0 && fixture.ShutdownCodes.Count == 0,
                "Editing did not protect explicit exit and normal closing.");
            fixture.Interaction.EditorOpen = false;
            using (fixture.Interaction.EnterModal())
            using (fixture.Interaction.EnterModal())
            {
                fixture.Interaction.RequestExit(); window.Close();
                Assert(!window.IsClosed && window.SaveCalls == 0 && fixture.ShutdownCodes.Count == 0,
                    "Nested modal work did not protect exit and closing.");
            }
            fixture.Controller.Exit();
            Assert(window.IsClosed && window.SaveCalls == 1 && fixture.ShutdownCodes.SequenceEqual(new[] { 0 }),
                "Exit stayed blocked after all work finished.");
        }
    }

    private static void RecoveryIdentity()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single();
            old.RaiseHandleLost(); old.RaiseHandleLost();
            Assert(old.HostLost && fixture.Scheduler.Tickets.Count == 1, "Repeated handle loss scheduled multiple recoveries.");
            Assert(fixture.Scheduler.Tickets[0].Delay == TimeSpan.FromSeconds(2), "The recovery delay changed.");
            Assert(old.SnapshotCalls == 0, "Recovery captured state before the delay elapsed.");
            fixture.Scheduler.FireNext();
            Assert(fixture.Windows.Count == 2 && fixture.MainWindows.Last() == fixture.Windows[1], "Recovery did not assign one replacement.");
            Assert(old.IsClosed && old.DisposeCalls == 1 && old.HandlerCount == 0, "Recovery retained the old window or its handlers.");
            Assert(fixture.Windows[1].ShowCalls == 1 && fixture.ShutdownCodes.Count == 0, "Successful recovery did not show its replacement normally.");
        }
    }

    private static void LatestRecoveryState()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single();
            old.RaiseHandleLost();
            old.State.Items[0].Text = "committed while recovery was pending";
            old.State.Settings.Collapsed = true;
            old.State.Settings.Appearance = AppearanceSettings.CreateDark();
            fixture.Scheduler.FireNext();
            FakeWindow replacement = fixture.Windows[1];
            Assert(replacement.State.Items[0].Text == "committed while recovery was pending", "Recovery recreated an early task snapshot.");
            Assert(replacement.State.Settings.Collapsed && replacement.State.Settings.Appearance.Matches(AppearanceSettings.CreateDark()),
                "Recovery lost settings committed during the delay.");
            Assert(old.SnapshotCalls == 1, "A successful rebuild did not take one latest-state snapshot.");
            Assert(old.SaveCalls == 0, "A lost native host caused an old-window geometry save.");
        }
        using (var fixture = new Fixture())
        {
            fixture.Controller.Start(State("automatic"), true);
            fixture.Windows[0].RaiseHandleLost(); fixture.Scheduler.FireNext();
            Assert(!fixture.Windows[1].ShowActivated, "A logon-started window activated during recovery.");
        }
    }

    private static void BusyRecovery()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single();
            fixture.Interaction.EditorOpen = true; old.RaiseHandleLost();
            fixture.Scheduler.FireNext();
            Assert(fixture.Windows.Count == 1 && old.SnapshotCalls == 0 && !old.IsClosed, "Recovery interrupted an active editor.");
            Assert(fixture.Scheduler.ActiveCount == 1 && fixture.Scheduler.Tickets.Last().Delay == TimeSpan.FromSeconds(2), "Busy recovery did not schedule one later attempt.");
            IDisposable outer = fixture.Interaction.EnterModal();
            IDisposable inner = fixture.Interaction.EnterModal();
            fixture.Interaction.EditorOpen = false; outer.Dispose();
            fixture.Scheduler.FireNext();
            Assert(fixture.Windows.Count == 1 && old.SnapshotCalls == 0, "Recovery ignored a remaining modal scope.");
            old.State.Items[0].Text = "saved before the final modal scope ended";
            inner.Dispose(); fixture.Scheduler.FireNext();
            Assert(fixture.Windows.Count == 2 && fixture.Windows[1].State.Items[0].Text == "saved before the final modal scope ended",
                "Recovery did not resume with the latest state after all work ended.");
        }
    }

    private static void PendingRestore()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single();
            old.RaiseHandleLost();
            fixture.Controller.Restore(); fixture.Controller.Restore(); fixture.Controller.Restore();
            Assert(old.RestoreCalls == 0, "A queued restore targeted the lost window.");
            fixture.Scheduler.FireNext();
            Assert(fixture.Windows[1].RestoreCalls == 1, "Pending requests were lost or applied repeatedly to the replacement.");
            fixture.Controller.Restore();
            Assert(fixture.Windows[1].RestoreCalls == 2, "An ordinary restore was not forwarded to the current window.");
        }
    }

    private static void ExitDuringRecovery()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single(); old.RaiseHandleLost();
            Scheduled ticket = fixture.Scheduler.Tickets.Single();
            fixture.Controller.Restore(); fixture.Controller.Exit();
            Assert(ticket.Cancelled && fixture.ShutdownCodes.SequenceEqual(new[] { 0 }), "Exit did not cancel pending recovery and shut down once.");
            ticket.InvokeEvenIfCancelled();
            Assert(fixture.Windows.Count == 1 && old.RestoreCalls == 0 && fixture.ShutdownCodes.Count == 1,
                "A late recovery or restore revived the application after exit.");
        }
    }

    private static void Disposal()
    {
        var fixture = Started();
        FakeWindow old = fixture.Windows.Single(); old.RaiseHandleLost();
        Scheduled ticket = fixture.Scheduler.Tickets.Single();
        fixture.Controller.Dispose(); fixture.Controller.Dispose();
        Assert(ticket.Cancelled && old.DisposeCalls == 1 && old.IsClosed && old.HandlerCount == 0,
            "Disposal did not cancel work, unsubscribe and clean up the window once.");
        ticket.InvokeEvenIfCancelled();
        fixture.Controller.Restore(); fixture.Controller.Exit(); fixture.Controller.Fail(); fixture.Interaction.RequestExit();
        Assert(fixture.Windows.Count == 1 && fixture.ShutdownCodes.Count == 0 && old.RestoreCalls == 0,
            "Disposed lifecycle code still ran callbacks or shut down its caller.");
        fixture.Dispose();
    }

    private static void StaleCallbacks()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single();
            Action lateClosed = old.CaptureClosed();
            Action lateLost = old.CaptureHandleLost();
            Action lateClosing = old.CaptureClosing();
            old.RaiseHandleLost();
            Scheduled ticket = fixture.Scheduler.Tickets.Single(); fixture.Scheduler.FireNext();
            FakeWindow replacement = fixture.Windows[1];
            int schedules = fixture.Scheduler.Tickets.Count;
            lateClosing(); lateClosed(); lateLost(); ticket.InvokeEvenIfCancelled();
            Assert(fixture.Windows.Count == 2 && fixture.MainWindows.Last() == replacement && !replacement.IsClosed,
                "A stale callback replaced or closed the current window.");
            Assert(fixture.Scheduler.Tickets.Count == schedules && fixture.ShutdownCodes.Count == 0,
                "A stale old-window event scheduled work or shut down the new lifecycle.");
        }
    }

    private static void ShowFailure()
    {
        using (var fixture = new Fixture())
        {
            fixture.FailNextShow = true;
            Throws<InvalidOperationException>(delegate { fixture.Controller.Start(State("show-failure"), false); });
            FakeWindow failed = fixture.Windows.Single();
            Assert(failed.DisposeCalls == 1 && failed.IsClosed && failed.HandlerCount == 0,
                "Initial show failure retained a failed window or event subscriptions.");
            Assert(fixture.ShutdownCodes.Count == 0 && fixture.Scheduler.Tickets.Count == 0,
                "The composition root did not retain control of an initial show error.");
        }
    }

    private static void RecoveryFailure()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single(); old.RaiseHandleLost();
            fixture.FailNextCreate = true; fixture.Scheduler.FireNext();
            Assert(fixture.ShutdownCodes.SequenceEqual(new[] { 1 }), "A recovery factory error did not fail the application once.");
            Assert(old.IsClosed && old.DisposeCalls == 1 && old.HandlerCount == 0, "A failed recovery retained the old window.");
            fixture.Controller.Restore(); fixture.Controller.Fail();
            Assert(fixture.ShutdownCodes.Count == 1 && fixture.Windows.Count == 1, "A failed lifecycle restarted or shut down twice.");
        }
    }

    private static void UnexpectedClose()
    {
        using (var fixture = Started())
        {
            FakeWindow old = fixture.Windows.Single(); old.RaiseClosedWithoutClosing();
            Assert(fixture.Scheduler.ActiveCount == 1 && fixture.ShutdownCodes.Count == 0, "An unexpected close was treated as a requested exit.");
            fixture.Scheduler.FireNext();
            Assert(fixture.Windows.Count == 2 && fixture.Windows[1].ShowCalls == 1, "An unexpectedly closed window was not recreated.");
        }
    }

    private static Fixture Started()
    {
        var fixture = new Fixture(); fixture.Controller.Start(State("initial"), false); return fixture;
    }
    private static TodoState State(string text)
    {
        var state = new TodoState();
        state.Settings.Width = 430; state.Settings.Height = 475; state.Settings.Left = 1170; state.Settings.Top = 18;
        state.Items.Add(new TodoItem { Id = "lifecycle-fixture", Text = text, DueDate = new DateTime(2026, 11, 1),
            CreatedAt = new DateTime(2026, 10, 28), DeadlineSetAt = new DateTime(2026, 10, 28) });
        return state;
    }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("  OK " + name); }
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { assertions++; return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " was not thrown.");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly List<string> Log = new List<string>();
        public readonly List<FakeWindow> Windows = new List<FakeWindow>();
        public readonly List<IApplicationWindow> MainWindows = new List<IApplicationWindow>();
        public readonly List<int> ShutdownCodes = new List<int>();
        public readonly ApplicationInteraction Interaction = new ApplicationInteraction();
        public readonly ManualScheduler Scheduler = new ManualScheduler();
        public readonly ApplicationController Controller;
        public bool FailNextCreate, FailNextShow;
        public Fixture()
        {
            Controller = new ApplicationController(delegate(TodoState state) {
                if (FailNextCreate) { FailNextCreate = false; throw new InvalidOperationException("fixture construction failure"); }
                var window = new FakeWindow(Windows.Count + 1, state, Log) { FailShow = FailNextShow };
                FailNextShow = false; Windows.Add(window); Log.Add("create:" + window.Id); return window;
            }, delegate(IApplicationWindow window) {
                MainWindows.Add(window); Log.Add("main:" + ((FakeWindow)window).Id);
            }, delegate(int code) { ShutdownCodes.Add(code); Log.Add("shutdown:" + code); }, Interaction, Scheduler);
        }
        public void Dispose() { Controller.Dispose(); }
    }

    private sealed class FakeWindow : IApplicationWindow
    {
        private readonly List<string> log;
        private CancelEventHandler closing;
        private EventHandler closed, lost;
        public readonly int Id;
        public readonly TodoState State;
        public bool FailShow, IsClosed;
        public int ShowCalls, CloseCalls, DisposeCalls, SaveCalls, RestoreCalls, SnapshotCalls;
        public FakeWindow(int id, TodoState state, List<string> log)
        { Id = id; State = TodoStateCopy.Copy(state); this.log = log; ShowActivated = true; }
        public event CancelEventHandler Closing { add { closing += value; } remove { closing -= value; } }
        public event EventHandler Closed { add { closed += value; } remove { closed -= value; } }
        public event EventHandler HandleLost { add { lost += value; } remove { lost -= value; } }
        public bool ShowActivated { get; set; }
        public bool HostLost { get; set; }
        public TodoState CurrentState { get { SnapshotCalls++; return TodoStateCopy.Copy(State); } }
        public int HandlerCount { get { return Count(closing) + Count(closed) + Count(lost); } }
        public void Show()
        {
            ShowCalls++; log.Add("show:" + Id);
            if (FailShow) throw new InvalidOperationException("fixture show failure");
        }
        public void Close()
        {
            if (IsClosed) return;
            CloseCalls++; log.Add("close:" + Id);
            var args = new CancelEventArgs();
            CancelEventHandler handler = closing; if (handler != null) handler(this, args);
            if (args.Cancel) return;
            IsClosed = true;
            EventHandler done = closed; if (done != null) done(this, EventArgs.Empty);
        }
        public void SaveGeometry() { SaveCalls++; log.Add("save:" + Id); }
        public void RestoreToDesktop() { RestoreCalls++; log.Add("restore:" + Id); }
        public void Dispose() { DisposeCalls++; log.Add("dispose:" + Id); }
        public void RaiseHandleLost() { EventHandler handler = lost; if (handler != null) handler(this, EventArgs.Empty); }
        public void RaiseClosedWithoutClosing()
        { IsClosed = true; EventHandler handler = closed; if (handler != null) handler(this, EventArgs.Empty); }
        public Action CaptureClosed() { EventHandler handler = closed; return delegate { if (handler != null) handler(this, EventArgs.Empty); }; }
        public Action CaptureHandleLost() { EventHandler handler = lost; return delegate { if (handler != null) handler(this, EventArgs.Empty); }; }
        public Action CaptureClosing()
        { CancelEventHandler handler = closing; return delegate { if (handler != null) handler(this, new CancelEventArgs()); }; }
        private static int Count(Delegate handler) { return handler == null ? 0 : handler.GetInvocationList().Length; }
    }

    private sealed class ManualScheduler : IRecoveryScheduler
    {
        public readonly List<Scheduled> Tickets = new List<Scheduled>();
        public int ActiveCount { get { return Tickets.Count(ticket => !ticket.Cancelled && !ticket.Fired); } }
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var ticket = new Scheduled(delay, callback); Tickets.Add(ticket); return ticket;
        }
        public void FireNext()
        {
            Scheduled ticket = Tickets.FirstOrDefault(value => !value.Cancelled && !value.Fired);
            if (ticket == null) throw new InvalidOperationException("No active recovery callback is scheduled.");
            ticket.Fired = true; ticket.Callback();
        }
    }

    private sealed class Scheduled : IDisposable
    {
        public readonly TimeSpan Delay;
        public readonly Action Callback;
        public bool Cancelled, Fired;
        public Scheduled(TimeSpan delay, Action callback) { Delay = delay; Callback = callback; }
        public void InvokeEvenIfCancelled() { Callback(); }
        public void Dispose() { Cancelled = true; }
    }
}
