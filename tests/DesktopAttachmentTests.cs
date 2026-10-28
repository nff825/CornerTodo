using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CornerTodo;

internal static class DesktopAttachmentTests
{
    private static int passed;

    [STAThread]
    public static int Main()
    {
        Application application = null;
        try {
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Run("The first attachment waits for ContentRendered and then ContextIdle", InitialRender);
            Run("Repeated render and Start notifications do not duplicate attachment", RepeatedRender);
            Run("Windowed mode never subscribes to or invokes native desktop operations", Windowed);
            Run("A failed attachment reports pending once and a successful retry clears it", PendingRetry);
            Run("An explicit restore preserves current bounds and replaces the queued first attachment", ManualRestore);
            Run("Disposal aborts queued attachment, unsubscribes and detaches exactly once", DisposeBeforeRender);
            Run("Closing before ContextIdle cannot reattach the closed window", ClosedBeforeRender);
            Run("Handle-loss events are filtered by window and forwarded only once", HandleLoss);
            Run("A queued first attachment is cancelled when the native handle is lost", HandleLossBeforeRender);
            Run("An externally lost host blocks both immediate and retry operations", ExternalHostLoss);
            Run("Disposal after an active attachment prevents retry, callbacks and later events", DisposeAfterAttach);
            Console.WriteLine("PASS: " + passed + " desktop-attachment tests; native operations used fakes and no windows were shown.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { if (application != null) application.Shutdown(); }
    }

    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("  OK " + name); }

    private static void InitialRender()
    {
        using (Fixture fixture = new Fixture(false)) {
            Assert(fixture.Desktop.OperationCalls == 0 && fixture.Desktop.SubscriberCount == 1,
                "Construction must only register the desktop event.");
            fixture.Controller.Start(); fixture.Controller.RetryNow();
            Assert(fixture.Desktop.OperationCalls == 0, "Start/retry attached before rendering.");
            fixture.Window.Rendered();
            Assert(fixture.Desktop.AttachCalls == 0, "ContentRendered attached synchronously.");
            Pump();
            Assert(fixture.Desktop.AttachCalls == 1 && fixture.Desktop.SetCalls == 1,
                "The ContextIdle operation did not attach exactly once.");
            Bounds(new Rect(1500, 18, 430, 475), fixture.Desktop.Bounds, "Initial bounds changed");
            Assert(fixture.Pending.Count == 1 && !fixture.Pending[0], "Initial success did not clear the pending state.");
            Assert(fixture.Desktop.DiagnosticCalls == 1 && fixture.Desktop.ErrorReads == 1,
                "Native diagnostics were omitted from the rendering/attachment paths.");
            NoHandle(fixture.Window);
        }
    }

    private static void RepeatedRender()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Controller.Start();
            fixture.Window.Rendered(); fixture.Window.Rendered(); Pump();
            fixture.Window.Rendered(); Pump();
            Assert(fixture.Desktop.AttachCalls == 1, "ContentRendered scheduled repeated initial attachments.");
            fixture.Controller.RetryNow();
            Assert(fixture.Desktop.RetryCalls == 1 && fixture.Desktop.AttachCalls == 1,
                "The retry path must delegate to ReattachIfNeeded.");
        }
    }

    private static void Windowed()
    {
        using (Fixture fixture = new Fixture(true)) {
            fixture.Controller.Start(); fixture.Window.Rendered();
            fixture.Controller.AttachNow(); fixture.Controller.RetryNow(); Pump();
            fixture.Desktop.RaiseLoss(fixture.Window);
            fixture.Controller.Dispose(); fixture.Controller.Dispose();
            Assert(fixture.Desktop.OperationCalls == 0 && fixture.Desktop.DiagnosticCalls == 0 &&
                fixture.Desktop.SubscriberCount == 0 && fixture.Desktop.DetachCalls == 0,
                "Windowed mode touched the native attachment service.");
            Assert(fixture.Pending.Count == 0 && fixture.Losses == 0, "Windowed mode emitted desktop state callbacks.");
            NoHandle(fixture.Window);
        }
    }

    private static void PendingRetry()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Desktop.AttachSucceeds = false; fixture.Desktop.RetrySucceeds = false;
            fixture.Desktop.MutateBoundsDuringAttach = true;
            fixture.Controller.Start(); fixture.Window.Rendered(); Pump();
            Assert(fixture.Pending.Count == 1 && fixture.Pending[0], "A failed attachment did not report pending.");
            Bounds(new Rect(1500, 18, 430, 475), fixture.Desktop.Bounds, "Failed attachment lost bounds");
            fixture.Controller.RetryNow(); fixture.Controller.RetryNow();
            Assert(fixture.Desktop.RetryCalls == 2 && fixture.Pending.Count == 1,
                "Repeated failure should neither duplicate status nor call Attach directly.");
            fixture.Desktop.RetrySucceeds = true; fixture.Controller.RetryNow();
            Assert(fixture.Pending.Count == 2 && !fixture.Pending[1], "Recovery did not clear the stale pending state.");
            fixture.Controller.RetryNow();
            Assert(fixture.Pending.Count == 2 && fixture.Desktop.SetCalls == 1,
                "Stable retries repeated a status callback or rewrote geometry.");
        }
    }

    private static void ManualRestore()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Window.Rendered();
            Rect restored = new Rect(-810, 125, 720, 530);
            fixture.Desktop.Bounds = restored; fixture.Desktop.MutateBoundsDuringAttach = true;
            fixture.Controller.AttachNow(); Pump();
            Bounds(restored, fixture.Desktop.Bounds, "Immediate restore did not preserve screen DIP bounds");
            Assert(fixture.Desktop.AttachCalls == 1 && fixture.Desktop.SetCalls == 1,
                "The queued initial attachment ran again after an explicit restore.");
            fixture.Desktop.Bounds = new Rect(100, 90, 320, 280); fixture.Controller.AttachNow();
            Bounds(new Rect(100, 90, 320, 280), fixture.Desktop.Bounds, "A second restore used stale bounds");
            Assert(fixture.Desktop.AttachCalls == 2 && fixture.Pending.Count == 1,
                "Explicit restore was skipped or repeated an unchanged pending state.");
            NoHandle(fixture.Window);
        }
    }

    private static void DisposeBeforeRender()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Window.Rendered();
            fixture.Controller.Dispose(); fixture.Controller.Dispose();
            int calls = fixture.Desktop.OperationCalls;
            fixture.Controller.Start(); fixture.Controller.AttachNow(); fixture.Controller.RetryNow();
            fixture.Window.Rendered(); fixture.Desktop.RaiseLoss(fixture.Window); Pump();
            Assert(fixture.Desktop.AttachCalls == 0 && fixture.Desktop.OperationCalls == calls,
                "A disposed controller executed queued or subsequent native work.");
            Assert(fixture.Desktop.DetachCalls == 1 && fixture.Desktop.SubscriberCount == 0,
                "Disposal did not unsubscribe/detach exactly once.");
            Assert(fixture.Pending.Count == 0 && fixture.Losses == 0, "Disposed controller delivered callbacks.");
        }
    }

    private static void ClosedBeforeRender()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Window.Rendered(); fixture.Window.Close();
            Pump(); fixture.Controller.AttachNow(); fixture.Controller.RetryNow(); fixture.Controller.Dispose();
            Assert(fixture.Desktop.AttachCalls == 0 && fixture.Desktop.DetachCalls == 1 && fixture.Desktop.SubscriberCount == 0,
                "Closing left pending work or a native event subscription.");
            Assert(fixture.Pending.Count == 0 && fixture.Losses == 0, "Closing emitted a pending/loss callback.");
            NoHandle(fixture.Window);
        }
    }

    private static void HandleLoss()
    {
        using (Fixture fixture = new Fixture(false)) {
            Window other = new Window();
            try {
                fixture.Controller.Start(); fixture.Controller.AttachNow();
                fixture.Desktop.RaiseLoss(other);
                Assert(fixture.Losses == 0, "Another window's native loss was forwarded.");
                fixture.Desktop.RaiseLoss(fixture.Window); fixture.Desktop.RaiseLoss(fixture.Window);
                Assert(fixture.Losses == 1 && Object.ReferenceEquals(fixture.LossSender, fixture.Window),
                    "Own-window loss was not forwarded exactly once with its original sender.");
                int calls = fixture.Desktop.OperationCalls;
                fixture.Controller.Start(); fixture.Controller.AttachNow(); fixture.Controller.RetryNow(); Pump();
                Assert(fixture.Desktop.OperationCalls == calls, "The lost window received further native operations.");
                NoHandle(other); NoHandle(fixture.Window);
            }
            finally { other.Close(); }
        }
    }

    private static void HandleLossBeforeRender()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Window.Rendered(); fixture.Desktop.RaiseLoss(fixture.Window); Pump();
            Assert(fixture.Desktop.AttachCalls == 0 && fixture.Losses == 1 && fixture.Pending.Count == 0,
                "A native loss did not cancel the queued attachment.");
        }
    }

    private static void ExternalHostLoss()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Window.Rendered(); fixture.HostLost = true; Pump();
            fixture.Controller.AttachNow(); fixture.Controller.RetryNow(); fixture.Controller.Start();
            Assert(fixture.Desktop.OperationCalls == 0 && fixture.Pending.Count == 0,
                "External HostLost did not block a queued/immediate/retry attachment.");
        }
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Controller.AttachNow(); fixture.HostLost = true;
            int calls = fixture.Desktop.OperationCalls; fixture.Controller.RetryNow(); fixture.Controller.AttachNow();
            Assert(fixture.Desktop.OperationCalls == calls, "HostLost did not block work after an existing attachment.");
        }
    }

    private static void DisposeAfterAttach()
    {
        using (Fixture fixture = new Fixture(false)) {
            fixture.Controller.Start(); fixture.Controller.AttachNow(); fixture.Controller.Dispose();
            int calls = fixture.Desktop.OperationCalls; int reports = fixture.Pending.Count;
            fixture.Desktop.RaiseLoss(fixture.Window); fixture.Window.Rendered();
            fixture.Controller.RetryNow(); fixture.Controller.AttachNow(); Pump(); fixture.Window.Close();
            Assert(fixture.Desktop.OperationCalls == calls && fixture.Desktop.DetachCalls == 1 &&
                fixture.Desktop.SubscriberCount == 0 && fixture.Pending.Count == reports && fixture.Losses == 0,
                "A disposed active controller retained timer, render or loss handlers.");
        }
    }

    private static void Pump()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        DispatcherFrame frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }
    private static void Bounds(Rect expected, Rect actual, string message)
    { Assert(expected == actual, message + ": expected " + expected + ", got " + actual); }
    private static void NoHandle(Window window)
    { Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "A test created a native window handle."); }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class TestWindow : Window
    {
        public void Rendered() { OnContentRendered(EventArgs.Empty); }
    }

    private sealed class FakeDesktop : IDesktopAttachmentAdapter
    {
        private EventHandler handleLost;
        public int SubscriberCount;
        public int AttachCalls, RetryCalls, GetCalls, SetCalls, IsAttachedCalls, DetachCalls, DiagnosticCalls, ErrorReads;
        public bool AttachSucceeds = true, RetrySucceeds = true, Attached, MutateBoundsDuringAttach;
        public Rect Bounds = new Rect(1500, 18, 430, 475);
        public int OperationCalls { get { return AttachCalls + RetryCalls + GetCalls + SetCalls + IsAttachedCalls + DetachCalls; } }
        public event EventHandler WindowHandleLost
        {
            add { handleLost += value; SubscriberCount++; }
            remove { handleLost -= value; SubscriberCount--; }
        }
        public string LastError { get { ErrorReads++; return Attached ? null : "Explorer is not ready"; } }
        public bool Attach(Window window)
        {
            AttachCalls++; Attached = AttachSucceeds;
            if (MutateBoundsDuringAttach) Bounds = new Rect(0, 0, 12, 12);
            return Attached;
        }
        public bool IsAttached(Window window) { IsAttachedCalls++; return Attached; }
        public void ReattachIfNeeded(Window window) { RetryCalls++; Attached = RetrySucceeds; }
        public Rect GetBounds(Window window) { GetCalls++; return Bounds; }
        public void SetBounds(Window window, Rect bounds) { SetCalls++; Bounds = bounds; }
        public string Diagnostics(Window window) { DiagnosticCalls++; return "fake desktop"; }
        public void Detach(Window window) { DetachCalls++; Attached = false; }
        public void RaiseLoss(Window window)
        { EventHandler handler = handleLost; if (handler != null) handler(window, EventArgs.Empty); }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly TestWindow Window = new TestWindow { Content = new Border() };
        public readonly FakeDesktop Desktop = new FakeDesktop();
        public readonly List<bool> Pending = new List<bool>();
        public readonly DesktopAttachmentController Controller;
        public bool HostLost;
        public int Losses;
        public object LossSender;
        public Fixture(bool windowed)
        {
            Controller = new DesktopAttachmentController(Window, windowed, delegate(bool pending) { Pending.Add(pending); },
                delegate { return HostLost; }, Desktop);
            Controller.HandleLost += delegate(object sender, EventArgs args) { Losses++; LossSender = sender; };
        }
        public void Dispose()
        {
            NoHandle(Window); Controller.Dispose(); Window.Close();
        }
    }
}
