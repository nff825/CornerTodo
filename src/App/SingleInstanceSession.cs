using System;
using System.Diagnostics;
using System.Threading;

namespace CornerTodo
{
    /// <summary>Owns the legacy named instance token and Wake/Ack handshake without a UI dependency.</summary>
    internal sealed class SingleInstanceSession : IDisposable
    {
        internal const int DefaultNotifyTimeoutMilliseconds = 4000;
        private readonly object gate = new object();
        private readonly string key;
        private readonly int notifyTimeoutMilliseconds;
        private readonly bool primary;
        private Mutex instanceToken;
        private EventWaitHandle wake;
        private EventWaitHandle acknowledged;
        private RegisteredWaitHandle registration;
        private Action<Action> dispatch;
        private Action restore;
        private Action<Exception> onError;
        private bool listening;
        private bool disposed;

        internal SingleInstanceSession(string key)
            : this(key, DefaultNotifyTimeoutMilliseconds)
        {
        }

        internal SingleInstanceSession(string key, int notifyTimeoutMilliseconds)
        {
            if (String.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '\\', '/', '\0' }) >= 0)
                throw new ArgumentException("A nonempty instance key without namespace separators is required.", "key");
            if (notifyTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException("notifyTimeoutMilliseconds");
            this.key = key;
            this.notifyTimeoutMilliseconds = notifyTimeoutMilliseconds;
            try
            {
                bool created;
                // The name's lifetime is the instance token, as in the original
                // createdNew guard. No thread owns the mutex, so closing from a
                // dispatcher or shutdown thread cannot strand a thread-owned lock.
                instanceToken = new Mutex(false, "Local\\CornerTodo-" + key, out created);
                primary = created;
                if (!created)
                {
                    // A duplicate must not keep the primary's token alive after
                    // that primary exits, even if this duplicate object remains.
                    instanceToken.Dispose(); instanceToken = null;
                    return;
                }
                wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CornerTodo-Wake-" + key);
                acknowledged = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CornerTodo-Ack-" + key);
                wake.Reset(); acknowledged.Reset();
            }
            catch { Dispose(); throw; }
        }

        internal bool IsPrimary
        {
            get { lock (gate) { return primary && !disposed; } }
        }

        internal bool NotifyExisting(bool startup)
        {
            lock (gate)
            {
                if (disposed) return false;
                // Automatic duplicates are intentionally silent and must not
                // reset Ack, signal Wake, dispatch, expand or move a window.
                if (startup) return true;
                if (primary) return false;
            }
            try
            {
                using (var signal = EventWaitHandle.OpenExisting("Local\\CornerTodo-Wake-" + key))
                using (var ack = EventWaitHandle.OpenExisting("Local\\CornerTodo-Ack-" + key))
                {
                    lock (gate)
                    {
                        if (disposed) return false;
                        ack.Reset(); signal.Set();
                    }
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (true)
                    {
                        lock (gate) { if (disposed) return false; }
                        long remaining = notifyTimeoutMilliseconds - elapsed.ElapsedMilliseconds;
                        if (remaining <= 0) return false;
                        // Short waits allow disposing a duplicate to cancel its
                        // notification without closing handles under a native wait.
                        if (ack.WaitOne((int)Math.Min(remaining, 50)))
                        {
                            lock (gate) { return !disposed; }
                        }
                    }
                }
            }
            catch (Exception) { return false; }
        }

        internal void Listen(Action<Action> dispatch, Action restore, Action<Exception> onError = null)
        {
            if (dispatch == null) throw new ArgumentNullException("dispatch");
            if (restore == null) throw new ArgumentNullException("restore");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("SingleInstanceSession");
                if (!primary) throw new InvalidOperationException("Only the primary instance can listen for wake requests.");
                if (listening) throw new InvalidOperationException("The instance listener is already registered.");
                this.dispatch = dispatch; this.restore = restore; this.onError = onError;
                try
                {
                    registration = ThreadPool.RegisterWaitForSingleObject(wake, WakeRequested, null, Timeout.Infinite, false);
                    listening = true;
                }
                catch
                {
                    this.dispatch = null; this.restore = null; this.onError = null;
                    throw;
                }
            }
        }

        private void WakeRequested(object state, bool timedOut)
        {
            Action<Action> target;
            lock (gate)
            {
                if (disposed || !listening) return;
                target = dispatch;
            }
            // Dispatch outside the gate: the delegate may synchronously marshal
            // to another thread. Its queued action independently checks disposal.
            try { target(RestoreAndAcknowledge); }
            catch (Exception error) { Report(error); }
        }

        private void RestoreAndAcknowledge()
        {
            lock (gate)
            {
                if (disposed || !listening) return;
                // Dispose waits for an already-running restore, and queued work
                // cannot enter after it marks the session disposed. Reentrant
                // disposal from restore itself is also safe and suppresses Ack.
                try { restore(); }
                catch (Exception error) { Report(error); }
                finally
                {
                    if (!disposed)
                    {
                        try { acknowledged.Set(); }
                        catch (ObjectDisposedException) { }
                        catch (InvalidOperationException) { }
                    }
                }
            }
        }

        private void Report(Exception error)
        {
            Action<Exception> reporter;
            lock (gate) { reporter = disposed ? null : onError; }
            if (reporter == null) return;
            try { reporter(error); }
            catch (Exception) { } // Diagnostics must not escape a thread-pool callback.
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; listening = false;
                RegisteredWaitHandle wait = registration;
                EventWaitHandle signal = wake, ack = acknowledged;
                Mutex token = instanceToken;
                registration = null; wake = null; acknowledged = null; instanceToken = null;
                dispatch = null; restore = null; onError = null;
                // null makes Unregister nonblocking. Holding the gate through
                // closure also makes concurrent Dispose callers wait until the
                // first caller has released the native resources.
                try { if (wait != null) wait.Unregister(null); }
                finally
                {
                    try { if (signal != null) signal.Dispose(); }
                    finally
                    {
                        try { if (ack != null) ack.Dispose(); }
                        finally { if (token != null) token.Dispose(); }
                    }
                }
            }
        }
    }
}
