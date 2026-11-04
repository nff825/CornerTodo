using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CornerTodo;

internal static class ReminderTests
{
    private static readonly DateTime Epoch = new DateTime(2026, 11, 2, 10, 0, 0);
    private static DateTime clock;
    private static string root;
    private static int passed;
    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "CornerTodo-ReminderTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            Run("Days, hours and minutes compose an exact reminder instant including zero and UTC dates", Arithmetic);
            Run("Reminders are opt-in and invalid input never reaches the writer", DefaultsAndValidation);
            Run("Due plans claim once and text-only changes preserve receipts", ClaimAndTextEdit);
            Run("Rescheduling and changing lead time create future plans without past surprise popups", Replan);
            Run("Completion, reopening and disabling cancel or rearm only future reminders", Lifecycle);
            Run("Failed claims and plan saves preserve state and retry exactly once", FailedTransactions);
            Run("Persisted receipts prevent restart and clock-rollback repeats", Persistence);
            Run("Only the recent 24-hour window is recovered and expired plans are consumed", LateRecovery);
            Run("Reminder collection is bounded and aggregates multiple simultaneous plans", BoundedBatch);
            Run("Weekly reminder materializes a future occurrence and deletion never recreates it", EarlyWeekly);
            Run("A failed early claim cannot leave generated tasks or future tombstones behind", FailedEarlyClaim);
            Run("Daily reminder generation preserves pending periods and does not advance calendar history", EarlyDaily);
            Run("Monthly reminders handle short months before the occurrence exists", EarlyMonthly);
            Run("Prepared periods coexist with unresolved history and future-only rule edits", PreparedAndHistory);
            Run("Today's reminder survives unresolved calendar history without resurrecting deleted periods", TodayWithHistory);
            Run("Changing a recurrence schedule does not replay its newly past reminder", ChangedRecurrenceSchedule);
            Run("Pause and stop preserve generated plans but prevent ungenerated reminder periods", PauseStop);
            Run("Version-one through three reads remain untouched and reminders default off", Legacy);
            Run("Malformed reminder plans and marker metadata cannot replace valid data", InvalidPersistence);
            Run("Snapshots and previews isolate reminder metadata and future-period tombstones", CopyAndPreview);
            Console.WriteLine("PASS: " + passed + " reminder behavior groups; no WPF or production data.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally {
            string full = Path.GetFullPath(root), prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("CornerTodo-ReminderTests-", StringComparison.Ordinal))
                try { Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private static void Run(string name, Action test) { clock = Epoch; test(); passed++; Console.WriteLine("  OK " + name); }
    private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static TodoSession Session(Writer writer) { return new TodoSession(writer, new TodoState(), false, delegate { return clock; }); }
    private static TaskDraft Draft(DateTime due, int lead)
    { return new TaskDraft { Text = "向李老师汇报研究进展", HasDeadline = true, HasTime = true, DueDate = due, ReminderEnabled = true, ReminderMinutesBefore = lead }; }
    private static RecurrenceOptions Repeat(DateTime start, RecurrenceFrequency frequency)
    { return new RecurrenceOptions { StartDate = start.Date, Frequency = frequency, WeekDays = 32, DayOfMonth = 31 }; }
    private static TodoItem Item(TodoSession s, string id) { return s.Snapshot().Items.Single(i => i.Id == id); }
    private static void Arithmetic()
    {
        var item = new TodoItem { HasDeadline = true, HasTime = true, ReminderEnabled = true, DueDate = new DateTime(2026, 11, 20, 17, 0, 0), ReminderMinutesBefore = 1590 };
        Check(ReminderRules.GetReminderTime(item).Value == new DateTime(2026, 11, 19, 14, 30, 0), "Combined lead time did not match the approved example.");
        Check(ReminderRules.FormatLeadTime(1590) == "提前1天2小时30分钟" && ReminderRules.FormatLeadTime(0) == "到期时", "Lead-time labels are ambiguous.");
        item.ReminderMinutesBefore = 0; Check(ReminderRules.GetReminderTime(item).Value == item.DueDate, "Zero lead is not at deadline.");
        item.DueDate = new DateTime(2026, 3, 9, 1, 0, 0, DateTimeKind.Utc); item.ReminderMinutesBefore = 1440;
        Check(ReminderRules.ToUtc(item.DueDate) - ReminderRules.ToUtc(ReminderRules.GetReminderTime(item).Value) == TimeSpan.FromHours(24), "UTC durations changed across local-calendar/DST conversion.");
        item.ReminderMinutesBefore = -1; Check(!ReminderRules.GetReminderTime(item).HasValue, "Negative lead preview should be safely empty.");
        item.ReminderMinutesBefore = ReminderRules.MaxMinutes + 1; Check(!ReminderRules.GetReminderTime(item).HasValue, "Huge lead preview should be safely empty.");
        item.ReminderMinutesBefore = 1440; item.DueDate = DateTime.MinValue.AddHours(1); Check(!ReminderRules.GetReminderTime(item).HasValue, "Date underflow was not safe.");
        item.ReminderMinutesBefore = 0; item.DueDate = DateTime.MinValue; Check(!ReminderRules.GetReminderTime(item).HasValue, "An empty date preview exposed year one.");
        item.DueDate = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Local);
        DateTime? boundary = ReminderRules.GetReminderTime(item);
        Check(!boundary.HasValue || boundary.Value == item.DueDate, "A clamped maximum deadline became an earlier reminder.");
    }
    private static void DefaultsAndValidation()
    {
        var w = new Writer(); var s = Session(w); var plain = Draft(clock.AddHours(2), 15); plain.ReminderEnabled = false;
        TodoItem item = s.Add(plain); Check(!item.ReminderEnabled && item.ReminderPlanId == null && item.ReminderMinutesBefore == 0 && s.ClaimDueReminders().Count == 0, "Ordinary tasks opted into reminders.");
        int writes = w.Calls;
        foreach (int value in new[] { -1, ReminderRules.MaxMinutes + 1 }) { TaskDraft bad = Draft(clock.AddHours(2), value); Throws<ArgumentException>(delegate { s.Add(bad); }); }
        TaskDraft noTime = Draft(clock.AddHours(2), 0); noTime.HasTime = false; Throws<ArgumentException>(delegate { s.Add(noTime); });
        noTime.HasTime = true; noTime.HasDeadline = false; Throws<ArgumentException>(delegate { s.Edit(item.Id, noTime); });
        Check(w.Calls == writes, "Invalid reminder input reached persistence.");
    }
    private static void ClaimAndTextEdit()
    {
        var s = Session(new Writer()); var draft = Draft(clock.AddHours(2), 60); TodoItem item = s.Add(draft);
        clock = clock.AddMinutes(59); Check(s.ClaimDueReminders().Count == 0, "Reminder fired early.");
        clock = clock.AddMinutes(1); List<TodoItem> batch = s.ClaimDueReminders();
        Check(batch.Count == 1 && batch[0].Id == item.Id && batch[0].ReminderHandledAt.HasValue && s.ClaimDueReminders().Count == 0, "A due plan was not claimed exactly once.");
        draft.Text = "仅修改文字"; s.Edit(item.Id, draft);
        Check(Item(s, item.Id).ReminderPlanId == item.ReminderPlanId && Item(s, item.Id).ReminderHandledAt.HasValue && s.ClaimDueReminders().Count == 0, "Text edit rearmed a consumed reminder.");
    }
    private static void Replan()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(clock.AddHours(2), 60));
        clock = clock.AddHours(1); s.ClaimDueReminders();
        var draft = Draft(clock.AddHours(3), 30); s.Edit(item.Id, draft); TodoItem newer = Item(s, item.Id);
        Check(newer.ReminderPlanId != item.ReminderPlanId && !newer.ReminderHandledAt.HasValue, "Changed schedule did not create a future plan.");
        clock = clock.AddHours(2.5); Check(s.ClaimDueReminders().Count == 1, "Replacement plan failed to fire.");
        s.Edit(item.Id, Draft(clock.AddMinutes(10), 60));
        Check(Item(s, item.Id).ReminderHandledAt.HasValue && s.ClaimDueReminders().Count == 0, "Saving an already-past reminder unexpectedly popped up.");
    }
    private static void Lifecycle()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(clock.AddHours(2), 60));
        s.SetCompleted(item.Id, true); clock = clock.AddMinutes(10); Check(s.ClaimDueReminders().Count == 0, "Completed task notified.");
        s.SetCompleted(item.Id, false); clock = Epoch.AddHours(1); Check(s.ClaimDueReminders().Count == 1, "Reopened future plan was not rearmed.");
        s.SetCompleted(item.Id, true); s.SetCompleted(item.Id, false); Check(s.ClaimDueReminders().Count == 0, "Reopening replayed an old reminder.");
        var draft = Draft(clock.AddHours(3), 15); s.Edit(item.Id, draft); draft.ReminderEnabled = false; s.Edit(item.Id, draft);
        clock = clock.AddDays(1); Check(s.ClaimDueReminders().Count == 0 && Item(s, item.Id).ReminderPlanId == null, "Disabled reminder retained an active plan.");
        item = s.Add(Draft(clock.AddHours(2), 60)); s.Delete(item.Id); clock = clock.AddHours(2); Check(s.ClaimDueReminders().Count == 0, "Deleted task notified.");
    }
    private static void FailedTransactions()
    {
        var w = new Writer(); var s = Session(w); TodoItem item = s.Add(Draft(clock.AddHours(2), 60)); clock = clock.AddHours(1);
        w.Fail = true; Throws<IOException>(delegate { s.ClaimDueReminders(); });
        Check(!Item(s, item.Id).ReminderHandledAt.HasValue, "A failed claim consumed the reminder.");
        w.Fail = false; Check(s.ClaimDueReminders().Count == 1 && s.ClaimDueReminders().Count == 0, "A failed claim did not retry once.");
        w.Fail = true; string previous = Item(s, item.Id).ReminderPlanId; Throws<IOException>(delegate { s.Edit(item.Id, Draft(clock.AddHours(3), 30)); });
        Check(Item(s, item.Id).ReminderPlanId == previous, "Failed editing changed a committed plan.");
    }
    private static void Persistence()
    {
        var store = new TodoStore(Path.Combine(root, "persist")); var s = new TodoSession(store, new TodoState(), false, delegate { return clock; });
        TodoItem item = s.Add(Draft(clock.AddHours(2), 60)); clock = clock.AddHours(1); s.ClaimDueReminders();
        byte[] bytes = File.ReadAllBytes(store.DataFilePath); var restarted = new TodoSession(store, store.Load(), false, delegate { return clock; });
        Check(restarted.ClaimDueReminders().Count == 0 && Item(restarted, item.Id).ReminderHandledAt.HasValue, "Restart repeated an existing plan.");
        clock = clock.AddHours(-2); restarted.ClaimDueReminders(); clock = clock.AddHours(4);
        Check(restarted.ClaimDueReminders().Count == 0 && bytes.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "Clock rollback rearmed or rewrote a handled plan.");
    }
    private static void LateRecovery()
    {
        var s = Session(new Writer()); s.Add(Draft(clock.AddHours(2), 0)); clock = clock.AddHours(25);
        Check(s.ClaimDueReminders().Count == 1, "Recent missed reminder was not recovered.");
        clock = Epoch; s.Add(Draft(clock.AddHours(3), 0)); clock = Epoch.AddHours(28);
        Check(s.ClaimDueReminders().Count == 0 && s.Snapshot().Items.All(i => i.ReminderHandledAt.HasValue), "Stale reminder was shown or left to repeat.");
    }
    private static void BoundedBatch()
    {
        var s = Session(new Writer()); for (int i = 0; i < 370; i++) s.Add(Draft(clock.AddHours(1), 0));
        clock = clock.AddHours(1); Check(s.ClaimDueReminders().Count == 366 && s.ClaimDueReminders().Count == 4 && s.ClaimDueReminders().Count == 0, "Batch cap or aggregate handling is wrong.");
    }
    private static void EarlyWeekly()
    {
        var store = new TodoStore(Path.Combine(root, "weekly")); var s = new TodoSession(store, new TodoState(), false, delegate { return clock; });
        var d = Draft(Epoch.Date.AddDays(4).AddHours(17), 1440); d.Recurrence = Repeat(Epoch.Date.AddDays(4), RecurrenceFrequency.Weekly); s.Add(d);
        Check(s.Snapshot().Items.Count == 0, "Future period was generated before its reminder.");
        clock = Epoch.Date.AddDays(3).AddHours(17); var claimed = s.ClaimDueReminders();
        Check(claimed.Count == 1 && claimed[0].OccurrenceDate == Epoch.Date.AddDays(4) && s.Snapshot().RecurringRules[0].ProcessedThrough == null, "Weekly early reminder lost future identity or advanced calendar progress.");
        string id = claimed[0].Id; s.Delete(id);
        var restarted = new TodoSession(store, store.Load(), false, delegate { return clock; });
        Check(restarted.Snapshot().Items.Count == 0 && restarted.Snapshot().RecurringRules.Single().PreparedOccurrences.Count == 1,
            "Restart before the occurrence date lost its deletion tombstone.");
        clock = Epoch.Date.AddDays(4).AddHours(10); restarted.GenerateCurrentRecurrences();
        Check(restarted.Snapshot().Items.Count == 0 && restarted.GenerateCurrentRecurrences() == 0 && restarted.ClaimDueReminders().Count == 0, "Deleted prepared occurrence was recreated after restart.");
    }
    private static void FailedEarlyClaim()
    {
        var w = new Writer(); var s = Session(w); var d = Draft(Epoch.Date.AddDays(1).AddHours(17), 1440);
        d.Recurrence = Repeat(Epoch.Date.AddDays(1), RecurrenceFrequency.Daily); s.Add(d); clock = Epoch.Date.AddHours(17);
        w.Fail = true; Throws<IOException>(delegate { s.ClaimDueReminders(); });
        Check(s.Snapshot().Items.Count == 0 && s.Snapshot().RecurringRules.Single().PreparedOccurrences.Count == 0,
            "A failed early claim committed an occurrence or tombstone.");
        w.Fail = false; Check(s.ClaimDueReminders().Count == 1 && s.ClaimDueReminders().Count == 0, "Early claim did not retry exactly once.");
    }
    private static void EarlyDaily()
    {
        var s = Session(new Writer()); var d = Draft(Epoch.Date.AddDays(1).AddHours(17), 1440); d.Recurrence = Repeat(Epoch.Date.AddDays(1), RecurrenceFrequency.Daily); s.Add(d);
        clock = Epoch.Date.AddHours(17); Check(s.ClaimDueReminders().Count == 1, "Tomorrow's daily task did not remind today.");
        clock = Epoch.Date.AddDays(1).AddHours(10); Check(s.GenerateCurrentRecurrences() == 0 && s.GetRecurrenceSummary().TotalCount == 0, "A prepared daily occurrence became a duplicate or missed period.");
        clock = clock.Date.AddHours(17); Check(s.ClaimDueReminders().Count == 1 && s.Snapshot().Items.Count == 2, "Next early period overwrote unfinished history.");
    }
    private static void EarlyMonthly()
    {
        clock = new DateTime(2027, 1, 1, 10, 0, 0); var s = Session(new Writer()); var d = Draft(new DateTime(2027, 1, 31, 17, 0, 0), 1440);
        d.Recurrence = Repeat(new DateTime(2027, 1, 31), RecurrenceFrequency.Monthly); s.Add(d);
        clock = new DateTime(2027, 1, 30, 17, 0, 0); Check(s.ClaimDueReminders().Single().OccurrenceDate == new DateTime(2027, 1, 31), "January lead day wrong.");
        clock = new DateTime(2027, 1, 31, 10, 0, 0); s.GenerateCurrentRecurrences();
        clock = new DateTime(2027, 2, 27, 17, 0, 0); Check(s.ClaimDueReminders().Single().OccurrenceDate == new DateTime(2027, 2, 28), "Short month early reminder drifted.");
    }
    private static void PreparedAndHistory()
    {
        var store = new TodoStore(Path.Combine(root, "history")); var s = new TodoSession(store, new TodoState(), false, delegate { return clock; });
        var d = Draft(Epoch.Date.AddHours(17), 1440); d.Recurrence = Repeat(Epoch.Date, RecurrenceFrequency.Daily); s.Add(d);
        clock = Epoch.Date.AddDays(3).AddHours(17); s.ClaimDueReminders();
        Check(s.GetRecurrenceSummary().MissedCount == 2 && s.Snapshot().RecurringRules[0].ProcessedThrough == Epoch.Date, "Early generation consumed unresolved calendar history.");
        s.ResolveMissedRecurrences(false); RecurringTaskRule rule = s.Snapshot().RecurringRules.Single(); var prepared = s.Snapshot().Items.Single(i => i.OccurrenceDate == Epoch.Date.AddDays(4));
        d.Text = "只改未来"; d.DueDate = clock.Date.AddHours(18); d.Recurrence.StartDate = clock.Date; s.EditRecurringRule(rule.Id, d);
        Check(Item(s, prepared.Id).Text != d.Text && store.Load().Items.Any(i => i.Id == prepared.Id), "Future rule editing rewrote a generated early occurrence or made its marker invalid.");
    }
    private static void TodayWithHistory()
    {
        foreach (int lead in new[] { 0, 15 }) {
            clock = Epoch;
            var store = new TodoStore(Path.Combine(root, "today-history-" + lead));
            var s = new TodoSession(store, new TodoState(), false, delegate { return clock; });
            var d = Draft(Epoch.Date.AddHours(17), lead); d.Recurrence = Repeat(Epoch.Date, RecurrenceFrequency.Daily); s.Add(d);
            clock = Epoch.Date.AddDays(2).AddHours(17).AddMinutes(-lead);
            Check(s.GenerateCurrentRecurrences() == 0 && s.GetRecurrenceSummary().MissedCount == 1, "Unresolved history did not block the ordinary calendar path as expected.");
            TodoItem today = s.ClaimDueReminders().Single(); RecurringTaskRule rule = s.Snapshot().RecurringRules.Single();
            Check(today.OccurrenceDate == clock.Date && rule.ProcessedThrough == Epoch.Date && rule.PreparedOccurrences.Contains(clock.Date),
                "Today's due reminder was lost or consumed unresolved history.");
            s.Delete(today.Id);
            var restarted = new TodoSession(store, store.Load(), false, delegate { return clock; });
            Check(restarted.ClaimDueReminders().Count == 0 && restarted.GenerateCurrentRecurrences() == 0 && !restarted.Snapshot().Items.Any(i => i.Id == today.Id),
                "Restart recreated a deleted current period while history remained unresolved.");
            restarted.ResolveMissedRecurrences(false);
            Check(restarted.GenerateCurrentRecurrences() == 0 && restarted.ClaimDueReminders().Count == 0 && !restarted.Snapshot().Items.Any(i => i.Id == today.Id),
                "Resolving history recreated the already deleted current period.");
        }
    }
    private static void ChangedRecurrenceSchedule()
    {
        foreach (string change in new[] { "weekday", "frequency", "start", "month-day", "month-last" }) {
            clock = Epoch; var s = Session(new Writer()); DateTime start = Epoch.Date.AddDays(4);
            var d = Draft(start.AddHours(17), 2880); d.Recurrence = Repeat(start, RecurrenceFrequency.Weekly);
            if (change.StartsWith("month", StringComparison.Ordinal)) {
                d.Recurrence.Frequency = RecurrenceFrequency.Monthly; d.Recurrence.DayOfMonth = start.Day;
            }
            s.Add(d); string id = s.Snapshot().RecurringRules.Single().Id;
            clock = Epoch.Date.AddDays(3).AddHours(18);
            if (change == "weekday") d.Recurrence.WeekDays = 64;
            else if (change == "frequency") d.Recurrence.Frequency = RecurrenceFrequency.Daily;
            else if (change == "start") { d.Recurrence.StartDate = start.AddDays(1); d.DueDate = d.Recurrence.StartDate.AddHours(17); }
            else if (change == "month-day") d.Recurrence.DayOfMonth = start.Day + 1;
            else d.Recurrence.UseLastDay = true;
            s.EditRecurringRule(id, d);
            Check(s.GetRecurringRule(id).ReminderSetAt == clock, "Changing " + change + " retained an obsolete reminder activation time.");
            Check(s.ClaimDueReminders().Count == 0, "Changing " + change + " unexpectedly replayed a past reminder.");
        }
        clock = Epoch; var unchanged = Session(new Writer());
        var original = Draft(Epoch.Date.AddDays(4).AddHours(17), 2880); original.Recurrence = Repeat(Epoch.Date.AddDays(4), RecurrenceFrequency.Weekly); unchanged.Add(original);
        string ruleId = unchanged.Snapshot().RecurringRules.Single().Id;
        clock = Epoch.Date.AddDays(2).AddHours(18); original.Text = "只修改规则文字"; unchanged.EditRecurringRule(ruleId, original);
        Check(unchanged.GetRecurringRule(ruleId).ReminderSetAt == Epoch && unchanged.ClaimDueReminders().Single().OccurrenceDate == Epoch.Date.AddDays(4),
            "A text-only rule edit reset an existing recent reminder plan.");
    }
    private static void PauseStop()
    {
        var s = Session(new Writer()); var d = Draft(Epoch.Date.AddHours(17), 60); d.Recurrence = Repeat(Epoch.Date, RecurrenceFrequency.Daily); TodoItem existing = s.Add(d);
        string ruleId = s.Snapshot().RecurringRules.Single().Id; s.SetRecurringPaused(ruleId, true);
        clock = Epoch.Date.AddHours(16); Check(s.ClaimDueReminders().Single().Id == existing.Id, "Pausing future generation cancelled an existing task's reminder.");
        clock = Epoch.Date.AddDays(1).AddHours(16); Check(s.ClaimDueReminders().Count == 0, "Paused rule generated a new reminder period.");
        s.SetRecurringPaused(ruleId, false); s.StopRecurringRule(ruleId); clock = clock.AddDays(1);
        Check(s.ClaimDueReminders().Count == 0, "Stopped rule created a future period.");
    }
    private static void Legacy()
    {
        for (int version = 1; version <= 3; version++) {
            var store = new TodoStore(Path.Combine(root, "legacy-" + version)); Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
            string raw = "{\"Version\":" + version + ",\"Items\":[{\"Id\":\"old\",\"Text\":\"旧任务\",\"CreatedAt\":\"2026-10-20T10:00:00.0000000\",\"DueDate\":\"2026-11-20T17:00:00.0000000\",\"HasTime\":true}]}";
            File.WriteAllText(store.DataFilePath, raw, new UTF8Encoding(false)); byte[] before = File.ReadAllBytes(store.DataFilePath);
            TodoState loaded = store.Load(); Check(loaded.Version == 4 && !loaded.Items.Single().ReminderEnabled && before.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "Legacy load enabled reminders or rewrote bytes.");
            store.Save(loaded); Check(File.ReadAllText(store.DataFilePath).Contains("\"Version\":4"), "First save did not use protected schema four.");
        }
    }
    private static void InvalidPersistence()
    {
        var store = new TodoStore(Path.Combine(root, "invalid")); var s = new TodoSession(store, new TodoState(), false, delegate { return clock; });
        TodoItem item = s.Add(Draft(clock.AddHours(2), 15)); byte[] before = File.ReadAllBytes(store.DataFilePath);
        TodoState bad = s.Snapshot(); bad.Items[0].ReminderPlanId = null; Throws<ArgumentException>(delegate { store.Save(bad); });
        bad = s.Snapshot(); bad.Items[0].HasTime = false; Throws<ArgumentException>(delegate { store.Save(bad); });
        bad = s.Snapshot(); bad.Items[0].ReminderMinutesBefore = -1; Throws<ArgumentException>(delegate { store.Save(bad); });
        Check(before.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "Invalid reminder state replaced valid bytes.");
    }
    private static void CopyAndPreview()
    {
        var w = new Writer(); var s = new TodoSession(w, new TodoState(), true, delegate { return clock; });
        var d = Draft(Epoch.Date.AddDays(1).AddHours(17), 1440); d.Recurrence = Repeat(Epoch.Date.AddDays(1), RecurrenceFrequency.Daily); s.Add(d);
        clock = Epoch.Date.AddHours(17); TodoItem plan = s.ClaimDueReminders().Single(); TodoState copy = s.Snapshot(); copy.Items[0].ReminderPlanId = "external"; copy.RecurringRules[0].PreparedOccurrences.Clear();
        Check(s.Snapshot().Items[0].ReminderPlanId == plan.ReminderPlanId && s.Snapshot().RecurringRules[0].PreparedOccurrences.Count == 1 && w.Calls == 0, "Preview or snapshot exposed reminder state.");
    }
    private sealed class Writer : ITodoStore
    {
        internal bool Fail; internal int Calls;
        public void Save(TodoState state) { Calls++; if (Fail) throw new IOException("Injected reminder save failure"); }
    }
}
