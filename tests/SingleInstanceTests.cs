using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CornerTodo;

internal static class SingleInstanceTests
{
    private static int passed;

    public static int Main(string[] args)
    {
        if (args.Length > 0) return Child(args);
        try
        {
            Run("Legacy names and one primary remain valid across disposal", TokenAndNames);
            Run("Real child processes ACK manual requests and ignore automatic duplicates", CrossProcessHandshake);
            Run("Missing events and an injected timeout fail without inventing ACK", MissingAndTimeout);
            Run("The legacy default manual timeout remains four seconds", DefaultTimeout);
            Run("A queued action and later wake cannot restore or ACK after disposal", QueuedAfterDispose);
            Run("Restore and diagnostic failures still ACK safely", RestoreFailure);
            Run("A failed dispatch does not restore or invent ACK", DispatchFailure);
            Run("Disposal inside restore suppresses ACK and releases the token", DisposeInsideRestore);
            Run("Concurrent disposal waits for an already-running restore", DisposeDuringRestore);
            Run("Disposing a notifier cancels its outstanding wait", DisposeNotifier);
            Run("Listener lifecycle and argument contracts are explicit", ListenerContracts);
            Console.WriteLine("PASS: " + passed + " single-instance behavior groups.");
            Console.WriteLine("Only GUID test kernel names and hidden test child processes were used; no application, WPF, Data or startup registration was accessed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    private static string Key() { return "InstanceTests-" + Guid.NewGuid().ToString("N"); }
    private static string MutexName(string key) { return "Local\\CornerTodo-" + key; }
    private static string WakeName(string key) { return "Local\\CornerTodo-Wake-" + key; }
    private static string AckName(string key) { return "Local\\CornerTodo-Ack-" + key; }
    private static void Dispatch(Action work) { ThreadPool.QueueUserWorkItem(delegate { work(); }); }
    private static Task<bool> Notify(SingleInstanceSession instance)
    { return Task.Factory.StartNew(delegate { return instance.NotifyExisting(false); }, TaskCreationOptions.LongRunning); }

    private static void TokenAndNames()
    {
        string key = Key();
        using (var primary = new SingleInstanceSession(key, 150))
        using (var duplicate = new SingleInstanceSession(key, 150))
        {
            Assert(primary.IsPrimary && !duplicate.IsPrimary, "The same thread acquired two primary instance tokens.");
            using (Mutex.OpenExisting(MutexName(key))) { }
            using (EventWaitHandle.OpenExisting(WakeName(key))) { }
            using (EventWaitHandle.OpenExisting(AckName(key))) { }
            primary.Dispose(); primary.Dispose();
            Assert(!primary.IsPrimary, "Disposed primary still reports itself active.");
            using (var replacement = new SingleInstanceSession(key, 150))
                Assert(replacement.IsPrimary, "A live duplicate object retained the departed primary's mutex token.");
        }
        Assert(!EventExists(WakeName(key)) && !EventExists(AckName(key)), "Unlistened instance events were not released.");
    }

    private static void CrossProcessHandshake()
    {
        string key = Key(); int restores = 0;
        using (var primary = new SingleInstanceSession(key))
        using (var restored = new ManualResetEvent(false))
        using (var wake = EventWaitHandle.OpenExisting(WakeName(key)))
        using (var ack = EventWaitHandle.OpenExisting(AckName(key)))
        {
            primary.Listen(Dispatch, delegate { Interlocked.Increment(ref restores); restored.Set(); });
            RunChild(key, "startup");
            Assert(!restored.WaitOne(120) && restores == 0, "Automatic duplicate invoked restore.");
            Assert(!wake.WaitOne(0) && !ack.WaitOne(0), "Automatic duplicate signaled Wake or Ack.");
            RunChild(key, "manual");
            Assert(restored.WaitOne(1500) && restores == 1, "The real manual child did not restore exactly once.");
            Assert(!ack.WaitOne(0), "The child did not consume its auto-reset ACK.");
        }
    }

    private static void MissingAndTimeout()
    {
        string absent = Key(); bool created;
        using (var guard = new Mutex(false, MutexName(absent), out created))
        using (var duplicate = new SingleInstanceSession(absent, 100))
        {
            Assert(created && !duplicate.IsPrimary, "The missing-events fixture failed to own the legacy mutex name.");
            Assert(!duplicate.NotifyExisting(false), "Missing events were treated as a successful manual restore.");
            Assert(duplicate.NotifyExisting(true), "Automatic duplicate should stay silent even when events are unavailable.");
            Assert(!EventExists(WakeName(absent)) && !EventExists(AckName(absent)), "A notifier created missing primary events.");
        }
        string key = Key();
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key, 120))
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            Assert(!duplicate.NotifyExisting(false), "A primary that never listens generated a false ACK.");
            Assert(elapsed.ElapsedMilliseconds >= 90, "Injected manual timeout returned prematurely.");
        }
    }

    private static void DefaultTimeout()
    {
        string key = Key();
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key))
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            Assert(!duplicate.NotifyExisting(false), "A missing listener unexpectedly acknowledged the default wait.");
            Assert(elapsed.ElapsedMilliseconds >= 3900, "The default manual wait no longer preserves the four-second protocol.");
        }
    }

    private static void QueuedAfterDispose()
    {
        string key = Key(); Action queued = null; int restores = 0;
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key, 250))
        using (var scheduled = new ManualResetEvent(false))
        using (var wake = EventWaitHandle.OpenExisting(WakeName(key)))
        using (var ack = EventWaitHandle.OpenExisting(AckName(key)))
        {
            primary.Listen(delegate(Action work) { queued = work; scheduled.Set(); }, delegate { restores++; });
            Task<bool> notify = Notify(duplicate);
            Assert(scheduled.WaitOne(2000), "The wake was not queued to the dispatcher.");
            primary.Dispose(); queued();
            Assert(restores == 0 && !ack.WaitOne(0), "Queued work restored or acknowledged after disposal.");
            Assert(notify.Wait(2000) && !notify.Result, "A discarded queued restore was reported as handled.");
            scheduled.Reset(); wake.Set();
            Assert(!scheduled.WaitOne(120), "A disposed registration dispatched a later wake.");
            using (var replacement = new SingleInstanceSession(key))
                Assert(replacement.IsPrimary, "Disposing a listening primary failed to release its instance token.");
        }
    }

    private static void RestoreFailure()
    {
        string key = Key(); int errors = 0;
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key, 1500))
        {
            primary.Listen(Dispatch, delegate { throw new InvalidOperationException("test restore failure"); },
                delegate(Exception error) { Interlocked.Increment(ref errors); throw new ApplicationException("test logger failure"); });
            Assert(duplicate.NotifyExisting(false), "Restore failure skipped its finally ACK.");
            Assert(duplicate.NotifyExisting(false), "An exception disabled subsequent wake handling.");
            Assert(errors == 2, "Restore failures did not reach the optional diagnostic handler.");
        }
    }

    private static void DispatchFailure()
    {
        string key = Key(); int restores = 0, errors = 0;
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key, 150))
        {
            primary.Listen(delegate { throw new InvalidOperationException("test dispatcher shutdown"); },
                delegate { Interlocked.Increment(ref restores); }, delegate { Interlocked.Increment(ref errors); });
            Assert(!duplicate.NotifyExisting(false), "A failed dispatch invented a successful ACK.");
            Assert(restores == 0 && errors == 1, "Failed dispatch executed restore or lost its diagnostic.");
        }
    }

    private static void DisposeInsideRestore()
    {
        string key = Key(); int restores = 0;
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key, 200))
        {
            primary.Listen(Dispatch, delegate { Interlocked.Increment(ref restores); primary.Dispose(); });
            Assert(!duplicate.NotifyExisting(false), "A restore that disposed its session acknowledged after closure.");
            Assert(restores == 1 && !primary.IsPrimary, "Reentrant disposal did not complete safely.");
            using (var replacement = new SingleInstanceSession(key)) Assert(replacement.IsPrimary, "Reentrant disposal stranded the mutex token.");
        }
    }

    private static void DisposeDuringRestore()
    {
        string key = Key();
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key, 2000))
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        using (var disposing = new ManualResetEvent(false))
        {
            primary.Listen(Dispatch, delegate {
                entered.Set();
                if (!release.WaitOne(3000)) throw new TimeoutException("The controlled restore was not released.");
            });
            Task<bool> notify = Notify(duplicate); Task disposal = null;
            try
            {
                Assert(entered.WaitOne(2000), "The controlled restore did not begin.");
                disposal = Task.Factory.StartNew(delegate { disposing.Set(); primary.Dispose(); }, TaskCreationOptions.LongRunning);
                Assert(disposing.WaitOne(1000) && !disposal.Wait(80), "Dispose returned while restore was still running.");
            }
            finally { release.Set(); }
            Assert(disposal != null && disposal.Wait(2000), "Concurrent disposal did not finish after restore.");
            Assert(notify.Wait(2500), "The concurrent notifier failed to finish.");
            using (var replacement = new SingleInstanceSession(key)) Assert(replacement.IsPrimary, "Concurrent disposal retained the primary token.");
        }
    }

    private static void DisposeNotifier()
    {
        string key = Key(); Action queued = null;
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key))
        using (var scheduled = new ManualResetEvent(false))
        {
            primary.Listen(delegate(Action work) { queued = work; scheduled.Set(); }, delegate { });
            Task<bool> notify = Notify(duplicate);
            Assert(scheduled.WaitOne(2000), "Notifier cancellation fixture did not dispatch.");
            duplicate.Dispose(); duplicate.Dispose();
            Assert(notify.Wait(1500) && !notify.Result, "Disposed duplicate waited out the full timeout or reported success.");
            Assert(!duplicate.NotifyExisting(false) && !duplicate.NotifyExisting(true), "A disposed duplicate accepted another notification.");
            primary.Dispose(); queued();
        }
    }

    private static void ListenerContracts()
    {
        Expect<ArgumentException>(delegate { new SingleInstanceSession(""); });
        Expect<ArgumentException>(delegate { new SingleInstanceSession("not/a/key"); });
        Expect<ArgumentOutOfRangeException>(delegate { new SingleInstanceSession(Key(), 0); });
        string key = Key();
        using (var primary = new SingleInstanceSession(key))
        using (var duplicate = new SingleInstanceSession(key))
        {
            Expect<ArgumentNullException>(delegate { primary.Listen(null, delegate { }); });
            Expect<ArgumentNullException>(delegate { primary.Listen(Dispatch, null); });
            Expect<InvalidOperationException>(delegate { duplicate.Listen(Dispatch, delegate { }); });
            primary.Listen(Dispatch, delegate { });
            Expect<InvalidOperationException>(delegate { primary.Listen(Dispatch, delegate { }); });
            primary.Dispose();
            Expect<ObjectDisposedException>(delegate { primary.Listen(Dispatch, delegate { }); });
        }
    }

    private static int Child(string[] args)
    {
        Guid id;
        if (args.Length != 3 || args[0] != "--child" || !args[1].StartsWith("InstanceTests-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[1].Substring("InstanceTests-".Length), "N", out id) ||
            (args[2] != "manual" && args[2] != "startup")) return 7;
        using (var instance = new SingleInstanceSession(args[1], 1500))
        {
            if (instance.IsPrimary) return 8;
            return instance.NotifyExisting(args[2] == "startup") ? 0 : 9;
        }
    }

    private static void RunChild(string key, string mode)
    {
        string executable = Assembly.GetExecutingAssembly().Location;
        var start = new ProcessStartInfo {
            FileName = executable, Arguments = "--child " + key + " " + mode,
            WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (Process child = Process.Start(start))
        {
            if (!child.WaitForExit(5000))
            {
                // Only this test-owned process was started; no app lookup/kill.
                child.Kill(); child.WaitForExit(1000);
                throw new TimeoutException("The private instance-test child did not finish.");
            }
            string output = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
            Assert(child.ExitCode == 0, "The " + mode + " child failed (" + child.ExitCode + "): " + output);
        }
    }

    private static bool EventExists(string name)
    {
        try { using (EventWaitHandle.OpenExisting(name)) { return true; } }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("  OK " + name); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
