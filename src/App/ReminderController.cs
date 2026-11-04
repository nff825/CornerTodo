using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace CornerTodo
{
    internal interface IReminderPresenter : IDisposable
    {
        void Present(IList<TodoItem> items, Action<TodoItem> view, Action<TodoItem> complete, Action dismissed);
        void Hide();
        void Clear();
        void ShowError(string text);
    }

    internal interface IReminderTimer : IDisposable
    {
        event Action Tick;
        void Start();
        void Stop();
    }

    internal sealed class DispatcherReminderTimer : IReminderTimer
    {
        private readonly DispatcherTimer timer;
        public event Action Tick;
        public DispatcherReminderTimer(Dispatcher dispatcher)
        {
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += OnTick;
        }
        private void OnTick(object sender, EventArgs e) { Action callback = Tick; if (callback != null) callback(); }
        public void Start() { timer.Start(); }
        public void Stop() { timer.Stop(); }
        public void Dispose() { timer.Stop(); timer.Tick -= OnTick; Tick = null; }
    }

    /// <summary>Owns only running-app reminder delivery; durable scheduling belongs to TodoSession.</summary>
    internal sealed class ReminderController : IDisposable
    {
        private readonly Func<IList<TodoItem>> claim;
        private readonly Func<TodoState> snapshot;
        private readonly Func<bool> blocked;
        private readonly Action stateChanged;
        private readonly Action<TodoItem> view;
        private readonly Func<TodoItem, bool> complete;
        private readonly Action<Exception> report;
        private readonly IReminderPresenter presenter;
        private readonly IReminderTimer timer;
        private readonly List<TodoItem> active = new List<TodoItem>();
        private bool started, disposed, checking;

        internal ReminderController(Func<IList<TodoItem>> claim, Func<TodoState> snapshot,
            Func<bool> blocked, Action stateChanged, Action<TodoItem> view, Func<TodoItem, bool> complete,
            Action<Exception> report, IReminderPresenter presenter, IReminderTimer timer)
        {
            if (claim == null || snapshot == null || blocked == null || stateChanged == null || view == null || complete == null || report == null || presenter == null || timer == null)
                throw new ArgumentNullException("Reminder controller dependencies must be provided.");
            this.claim = claim; this.snapshot = snapshot; this.blocked = blocked; this.stateChanged = stateChanged;
            this.view = view; this.complete = complete; this.report = report; this.presenter = presenter; this.timer = timer;
            timer.Tick += CheckNow;
        }

        internal int ActiveCount { get { return active.Count; } }
        internal bool IsRunning { get { return started && !disposed; } }
        internal void Start()
        {
            if (disposed || started) return;
            started = true; timer.Start(); CheckNow();
        }

        internal void CheckNow()
        {
            if (disposed || !started || checking) return;
            checking = true;
            try {
                Prune();
                if (blocked()) { presenter.Hide(); return; }
                // The session saves every receipt before any UI is displayed.
                IList<TodoItem> due = null;
                try { due = claim(); }
                catch (Exception error) {
                    // A new receipt save must not prevent retrying an already
                    // committed reminder whose earlier presentation failed.
                    SafeReport(error);
                }
                if (disposed) return;
                foreach (TodoItem item in due ?? new List<TodoItem>()) {
                    active.RemoveAll(candidate => candidate.Id == item.Id);
                    active.Add(TodoStateCopy.Copy(item));
                }
                stateChanged();
                Prune(); Present();
            }
            catch (Exception error) { SafeReport(error); }
            finally { checking = false; }
        }

        internal void Refresh()
        {
            if (disposed || checking) return;
            try { Prune(); Present(); }
            catch (Exception error) { SafeReport(error); }
        }

        private void Prune()
        {
            TodoState current = snapshot();
            for (int index = active.Count - 1; index >= 0; index--) {
                TodoItem latest = Current(current, active[index]);
                if (latest == null) active.RemoveAt(index);
                else active[index] = TodoStateCopy.Copy(latest);
            }
        }

        private static TodoItem Current(TodoState state, TodoItem notification)
        {
            if (state == null || state.Items == null || notification == null || String.IsNullOrEmpty(notification.ReminderPlanId)) return null;
            return state.Items.FirstOrDefault(item => item.Id == notification.Id && !item.IsCompleted && item.HasDeadline && item.HasTime
                && item.ReminderEnabled && String.Equals(item.ReminderPlanId, notification.ReminderPlanId, StringComparison.Ordinal));
        }

        private void Present()
        {
            if (disposed) return;
            if (active.Count == 0) { presenter.Clear(); return; }
            if (blocked()) { presenter.Hide(); return; }
            presenter.Present(active.Select(TodoStateCopy.Copy).ToList(), ViewRequested, CompleteRequested, Dismissed);
        }

        private void ViewRequested(TodoItem notification)
        {
            if (disposed) return;
            try {
                TodoItem current = Current(snapshot(), notification);
                if (current != null && !blocked()) view(TodoStateCopy.Copy(current));
                Refresh();
            }
            catch (Exception error) { SafeReport(error); }
        }

        private void CompleteRequested(TodoItem notification)
        {
            if (disposed) return;
            try {
                TodoItem current = Current(snapshot(), notification);
                if (current != null && !blocked() && !complete(TodoStateCopy.Copy(current)))
                    presenter.ShowError("完成状态未保存，请稍后重试。");
                Refresh();
            }
            catch (Exception error) {
                SafeReport(error);
                if (!disposed) presenter.ShowError("完成状态未保存，请稍后重试。");
            }
        }

        private void Dismissed() { if (!disposed) active.Clear(); }
        private void SafeReport(Exception error) { if (!disposed) report(error); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; started = false;
            timer.Tick -= CheckNow; timer.Stop(); timer.Dispose();
            active.Clear(); presenter.Dispose();
        }
    }
}
