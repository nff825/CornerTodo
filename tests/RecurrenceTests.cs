using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CornerTodo;

internal static class RecurrenceTests
{
    private static DateTime clock;
    private static readonly DateTime Epoch = new DateTime(2026, 11, 2, 10, 0, 0);
    private static int passed;
    private static string root;
    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "CornerTodo-RecurrenceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            Run("Ordinary tasks default to no repeat and retain their existing lifecycle", Ordinary);
            Run("Weekly masks select multiple weekdays including Sunday without drifting", Weekly);
            Run("Monthly 31 and last-day schedules handle short months and leap years", Monthly);
            Run("Large calendar ranges are counted arithmetically and extrema terminate", ExtremeRanges);
            Run("Future starts save only a rule and first launch generation is exact-once", FutureStart);
            Run("Each period is independent of previous completion and snapshot labels", IndependentPeriods);
            Run("Deleted occurrences and clock rollback cannot regenerate a processed period", DeletedAndClockRollback);
            Run("No-deadline repeats stay out of attention and deadline repeats preserve offsets", DeadlineTemplates);
            Run("Missed periods require an explicit choice while unaffected rules can run", MissedDeferral);
            Run("Rule-level summaries isolate pending periods and never mutate or save state", RuleSummary);
            Run("Explicit catch-up is capped and remaining periods remain pending", BoundedCatchUp);
            Run("Explicit skip retains all existing history and never regenerates skipped periods", SkipMissed);
            Run("Pause and resume skip only paused periods and preserve current occurrences", PauseResume);
            Run("Unresolved history cannot be silently discarded by pause or future-only edit", ProtectMissedHistory);
            Run("Stopping a rule preserves history and cancels future generation", Stop);
            Run("Future-only editing preserves all occurrences and same-day uniqueness", EditFuture);
            Run("Editing only this occurrence leaves its template and scheduling intact", EditInstance);
            Run("An ordinary item can become the first occurrence without a duplicate", ConvertOrdinary);
            Run("Converting a past start leaves every other missed period pending", ConvertPastStart);
            Run("Future edits and pauses preserve a current period before the timer runs", UnprocessedCurrentPeriod);
            Run("All recurrence mutations are save-before-commit and retry exactly once", FailedTransactions);
            Run("Rule and pattern snapshots isolate every mutable reference", SnapshotIsolation);
            Run("Version-three persistence survives restart and deleted-period history", Persistence);
            Run("Legacy version-one and two reads migrate without touching bytes", LegacyVersions);
            Run("Invalid rule metadata and duplicate periods are preserved and rejected", InvalidState);
            Run("Unknown future versions never fall back over the main data", FutureVersion);
            Run("Preview generation and recurrence edits never touch the writer", Preview);
            Console.WriteLine("PASS: " + passed + " recurrence behavior groups.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally {
            string full = Path.GetFullPath(root), prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("CornerTodo-RecurrenceTests-", StringComparison.Ordinal))
                try { Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private static void Run(string name, Action test) { clock = Epoch; test(); passed++; Console.WriteLine("  OK " + name); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static RecurrenceOptions Options(RecurrenceFrequency type)
    { return new RecurrenceOptions { Frequency = type, StartDate = Epoch.Date, WeekDays = 34, DayOfMonth = 31 }; }
    private static TaskDraft Draft(RecurrenceOptions repeat)
    { return new TaskDraft { Text = "整理研究记录", Notes = "保留每期历史", HasDeadline = false, Recurrence = repeat }; }
    private static TodoSession Session(Writer writer)
    { return new TodoSession(writer, new TodoState(), false, delegate { return clock; }); }
    private static RecurringTaskRule Rule(RecurrenceOptions options)
    { return new RecurringTaskRule { Id = "rule", Text = "每期任务", Recurrence = options }; }
    private static RecurringTaskRule OnlyRule(TodoSession session) { return session.Snapshot().RecurringRules.Single(); }
    private static string Id(TodoSession session) { return OnlyRule(session).Id; }
    private static void Ordinary()
    {
        var s = Session(new Writer()); TaskDraft draft = Draft(null);
        TodoItem item = s.Add(draft); s.Edit(item.Id, draft); s.SetCompleted(item.Id, true);
        Assert(s.Snapshot().RecurringRules.Count == 0 && s.Snapshot().Items.Single().RecurringRuleId == null, "Default created a repeat.");
        Assert(s.GenerateCurrentRecurrences() == 0 && s.GetRecurrenceSummary().TotalCount == 0, "Ordinary tasks entered scheduler.");
    }
    private static void Weekly()
    {
        RecurrenceOptions o = Options(RecurrenceFrequency.Weekly); RecurringTaskRule r = Rule(o);
        Assert(RecurringTasks.Label(o) == "每周一、周五", "Wrong compact weekly label.");
        Assert(RecurringTasks.NextOccurrence(r, Epoch) == Epoch.Date, "Monday missing.");
        Assert(RecurringTasks.NextOccurrence(r, Epoch.AddDays(1)) == Epoch.Date.AddDays(4), "Friday missing.");
        Assert(RecurringTasks.Count(r, Epoch.Date, Epoch.Date.AddDays(6)) == 2, "Weekly count wrong.");
        o.WeekDays = 1; Assert(RecurringTasks.NextOccurrence(r, Epoch) == Epoch.Date.AddDays(6), "Sunday bit wrong.");
        o.WeekDays = 0; Throws<ArgumentException>(delegate { RecurringTasks.Validate(o); });
        o.WeekDays = 128; Throws<ArgumentException>(delegate { RecurringTasks.Validate(o); });
    }
    private static void Monthly()
    {
        RecurrenceOptions o = Options(RecurrenceFrequency.Monthly); o.StartDate = new DateTime(2027, 1, 31);
        RecurringTaskRule r = Rule(o);
        Assert(RecurringTasks.NextOccurrence(r, new DateTime(2027, 2, 1)) == new DateTime(2027, 2, 28), "February clamp wrong.");
        Assert(RecurringTasks.NextOccurrence(r, new DateTime(2027, 3, 1)) == new DateTime(2027, 3, 31), "31 anchor drifted.");
        Assert(RecurringTasks.NextOccurrence(r, new DateTime(2028, 2, 1)) == new DateTime(2028, 2, 29), "Leap day lost.");
        Assert(RecurringTasks.Count(r, new DateTime(2027, 2, 1), new DateTime(2027, 3, 30)) == 1, "Monthly boundary count wrong.");
        o.UseLastDay = true; o.DayOfMonth = 1;
        Assert(RecurringTasks.NextOccurrence(r, new DateTime(2027, 4, 1)) == new DateTime(2027, 4, 30), "Last day used numeric day.");
        Assert(RecurringTasks.Label(o) == "每月最后一天", "Last day label wrong.");
    }
    private static void ExtremeRanges()
    {
        var o = Options(RecurrenceFrequency.Daily); o.StartDate = new DateTime(1, 1, 2); var r = Rule(o);
        Assert(RecurringTasks.Count(r, o.StartDate, DateTime.MaxValue.Date) == 3652058, "Large daily range wrong.");
        o.Frequency = RecurrenceFrequency.Monthly;
        Assert(RecurringTasks.Count(r, o.StartDate, DateTime.MaxValue.Date) == 119988, "Large monthly range wrong.");
        r.ProcessedThrough = DateTime.MaxValue.Date;
        Assert(!RecurringTasks.NextOccurrence(r, Epoch).HasValue && !RecurringTasks.PendingStart(r).HasValue, "Maximum cursor overflowed.");
        r.ProcessedThrough = null; o.Frequency = RecurrenceFrequency.Weekly; o.WeekDays = 2; o.StartDate = DateTime.MaxValue.Date;
        Assert(!RecurringTasks.NextOccurrence(r, DateTime.MaxValue.Date).HasValue, "Weekly maximum should terminate.");
    }
    private static void FutureStart()
    {
        var w = new Writer(); var s = Session(w); var o = Options(RecurrenceFrequency.Daily); o.StartDate = Epoch.Date.AddDays(2);
        Assert(s.Add(Draft(o)) == null && s.Snapshot().Items.Count == 0 && w.Calls == 1, "Future start created an early row or failed to save rule.");
        clock = clock.AddDays(2); Assert(s.GenerateCurrentRecurrences() == 1, "First period not generated.");
        Assert(s.GenerateCurrentRecurrences() == 0 && s.Snapshot().Items.Count == 1, "Restart-equivalent check duplicated period.");
        var restarted = new TodoSession(w, s.Snapshot(), false, delegate { return clock; });
        Assert(restarted.GenerateCurrentRecurrences() == 0, "Session restart duplicated.");
    }
    private static void IndependentPeriods()
    {
        var s = Session(new Writer()); TodoItem one = s.Add(Draft(Options(RecurrenceFrequency.Daily)));
        s.SetCompleted(one.Id, true); clock = clock.AddDays(1); s.GenerateCurrentRecurrences();
        clock = clock.AddDays(1); s.GenerateCurrentRecurrences();
        TodoState state = s.Snapshot();
        Assert(state.Items.Count == 3 && state.Items.Count(i => i.IsCompleted) == 1 && state.Items[0].CompletedAt == Epoch, "Completion history changed.");
        Assert(state.Items.Select(i => i.Id).Distinct().Count() == 3 && state.Items[2].OccurrenceDate == clock.Date, "Occurrences share identity or drifted.");
    }
    private static void DeletedAndClockRollback()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(Options(RecurrenceFrequency.Daily))); s.Delete(item.Id);
        Assert(s.GenerateCurrentRecurrences() == 0, "Deleted current period reappeared.");
        clock = clock.AddDays(-2); Assert(s.GetRecurrenceSummary().TotalCount == 0 && s.GenerateCurrentRecurrences() == 0, "Rollback regenerated old dates.");
        clock = Epoch.AddDays(1); Assert(s.GenerateCurrentRecurrences() == 1, "Forward clock failed after rollback.");
    }
    private static void DeadlineTemplates()
    {
        var s = Session(new Writer()); TaskDraft draft = Draft(Options(RecurrenceFrequency.Daily));
        TodoItem first = s.Add(draft);
        Assert(!first.HasDeadline && first.DueDate == DateTime.MinValue && !first.DeadlineSetAt.HasValue && TodoRules.Expansion(s.Snapshot(), clock.AddYears(3)) == 0, "Undated repeat gained urgency.");
        var dated = Session(new Writer()); draft.HasDeadline = draft.HasTime = true;
        draft.DueDate = Epoch.Date.AddDays(2).AddHours(17).AddMinutes(23).AddSeconds(45).AddTicks(1234);
        first = dated.Add(draft); clock = clock.AddDays(1); dated.GenerateCurrentRecurrences();
        TodoItem second = dated.Snapshot().Items.Last();
        Assert(first.DueDate == draft.DueDate && second.DueDate == draft.DueDate.AddDays(1) && second.DeadlineSetAt == clock, "Relative deadline or precision drifted.");
        draft.DueDate = Epoch.Date.AddDays(-1); Throws<ArgumentException>(delegate { dated.Add(draft); });
    }
    private static void MissedDeferral()
    {
        var s = Session(new Writer()); s.Add(Draft(Options(RecurrenceFrequency.Daily))); clock = clock.AddDays(3);
        Assert(s.GetRecurrenceSummary().MissedCount == 2 && s.GetRecurrenceSummary().DueTodayCount == 1, "Missed summary boundary wrong.");
        int before = s.Snapshot().Items.Count; Assert(s.GenerateCurrentRecurrences() == 0 && s.Snapshot().Items.Count == before, "Auto silently skipped/caught up missed periods.");
        var o = Options(RecurrenceFrequency.Daily); o.StartDate = clock.Date; s.Add(Draft(o));
        Assert(s.Snapshot().Items.Count == before + 1 && s.GetRecurrenceSummary().MissedCount == 2, "Unrelated current rule blocked or missed state changed.");
    }
    private static void RuleSummary()
    {
        var w = new Writer(); var state = new TodoState();
        var missed = Rule(Options(RecurrenceFrequency.Daily)); missed.Id = "missed"; missed.Recurrence.StartDate = Epoch.Date.AddDays(-3);
        var selected = Rule(Options(RecurrenceFrequency.Daily)); selected.Id = "selected";
        var paused = RecurringTasks.Copy(missed); paused.Id = "paused"; paused.IsPaused = true;
        var stopped = RecurringTasks.Copy(missed); stopped.Id = "stopped"; stopped.IsStopped = true;
        state.RecurringRules.AddRange(new[] { missed, selected, paused, stopped });
        var s = new TodoSession(w, state, false, delegate { return clock; });
        RecurrenceSummary all = s.GetRecurrenceSummary(), one = s.GetRecurrenceSummary(selected.Id), backlog = s.GetRecurrenceSummary(missed.Id);
        Assert(all.MissedCount == 3 && all.DueTodayCount == 2 && one.MissedCount == 0 && one.DueTodayCount == 1,
            "Another rule's missed periods contaminated the selected rule summary.");
        Assert(backlog.MissedCount == 3 && backlog.DueTodayCount == 1 && s.GetRecurrenceSummary(paused.Id).TotalCount == 0 && s.GetRecurrenceSummary(stopped.Id).TotalCount == 0,
            "Rule summary diverged from global active-rule semantics.");
        clock = clock.AddDays(1);
        Assert(s.GetRecurrenceSummary(selected.Id).MissedCount == 1, "Rule summary ignored the session clock.");
        Throws<KeyNotFoundException>(delegate { s.GetRecurrenceSummary("missing"); });
        Assert(w.Calls == 0 && s.Snapshot().Items.Count == 0 && s.Snapshot().RecurringRules.All(r => !r.ProcessedThrough.HasValue),
            "Reading recurrence summaries wrote data or advanced a cursor.");
    }
    private static void BoundedCatchUp()
    {
        var s = Session(new Writer()); s.Add(Draft(Options(RecurrenceFrequency.Daily))); clock = clock.AddDays(1000);
        Assert(s.ResolveMissedRecurrences(true) == 366 && s.Snapshot().Items.Count == 367, "Catch-up was not bounded.");
        Assert(s.GetRecurrenceSummary().TotalCount == 634, "Remaining periods were lost.");
        s.ResolveMissedRecurrences(true); s.ResolveMissedRecurrences(true);
        Assert(s.Snapshot().Items.Count == 1001 && s.GetRecurrenceSummary().TotalCount == 0, "Catch-up did not finish exact-once.");
    }
    private static void SkipMissed()
    {
        var s = Session(new Writer()); TodoItem first = s.Add(Draft(Options(RecurrenceFrequency.Daily))); clock = clock.AddDays(7);
        Assert(s.ResolveMissedRecurrences(false) == 1 && s.Snapshot().Items.Count == 2, "Skip created historical rows or lost first.");
        Assert(s.Snapshot().Items[0].Id == first.Id && s.GetRecurrenceSummary().TotalCount == 0, "Skip lost history or left cursor behind.");
        clock = clock.AddDays(-3); Assert(s.GenerateCurrentRecurrences() == 0, "Skipped date reappeared after clock rollback.");
    }
    private static void PauseResume()
    {
        var s = Session(new Writer()); s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s);
        s.SetRecurringPaused(id, true); clock = clock.AddDays(10);
        Assert(s.GetRecurrenceSummary().TotalCount == 0 && s.GenerateCurrentRecurrences() == 0, "Paused rule generated.");
        s.SetRecurringPaused(id, false);
        Assert(s.GetRecurrenceSummary().MissedCount == 0 && s.GenerateCurrentRecurrences() == 1, "Resume caught up paused days or missed today.");
        s.SetRecurringPaused(id, true); s.SetRecurringPaused(id, false);
        Assert(s.GenerateCurrentRecurrences() == 0 && s.Snapshot().Items.Count == 2, "Same-day resume duplicated.");
    }
    private static void ProtectMissedHistory()
    {
        var s = Session(new Writer()); s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s); clock = clock.AddDays(3);
        Throws<InvalidOperationException>(delegate { s.SetRecurringPaused(id, true); });
        Throws<InvalidOperationException>(delegate { s.EditRecurringRule(id, Draft(Options(RecurrenceFrequency.Weekly))); });
        Assert(!OnlyRule(s).IsPaused && s.GetRecurrenceSummary().MissedCount == 2, "Future-only operation discarded unprocessed history.");
        s.ResolveMissedRecurrences(false); s.SetRecurringPaused(id, true); Assert(OnlyRule(s).IsPaused, "Resolved history still blocked pause.");
    }
    private static void Stop()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s);
        s.SetCompleted(item.Id, true); s.StopRecurringRule(id); clock = clock.AddDays(20);
        Assert(s.GetRecurrenceSummary().TotalCount == 0 && s.GenerateCurrentRecurrences() == 0 && s.Snapshot().Items.Single().IsCompleted, "Stop deleted history or generated.");
        Throws<InvalidOperationException>(delegate { s.SetRecurringPaused(id, false); });
        Throws<InvalidOperationException>(delegate { s.EditRecurringRule(id, Draft(Options(RecurrenceFrequency.Daily))); });
    }
    private static void EditFuture()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s);
        var draft = Draft(Options(RecurrenceFrequency.Weekly)); draft.Text = "后续新内容"; s.EditRecurringRule(id, draft);
        Assert(s.Snapshot().Items.Single().Text == item.Text && s.Snapshot().Items.Single().RepeatLabel == "每日", "Future edit changed history.");
        Assert(s.GenerateCurrentRecurrences() == 0, "Future edit duplicated same day.");
        clock = clock.AddDays(4); s.GenerateCurrentRecurrences();
        Assert(s.Snapshot().Items.Last().Text == "后续新内容" && s.Snapshot().Items.Last().RepeatLabel == "每周一、周五", "Future edit not applied.");
    }
    private static void EditInstance()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s);
        var draft = Draft(null); draft.Text = "仅本次"; s.Edit(item.Id, draft);
        Assert(OnlyRule(s).Text != draft.Text && s.Snapshot().Items.Single().RecurringRuleId == id, "Instance changed template or detached history.");
        Throws<ArgumentException>(delegate { s.Edit(item.Id, Draft(Options(RecurrenceFrequency.Weekly))); });
    }
    private static void ConvertOrdinary()
    {
        var s = Session(new Writer()); TodoItem item = s.Add(Draft(null)); s.Edit(item.Id, Draft(Options(RecurrenceFrequency.Daily)));
        Assert(s.Snapshot().Items.Single().Id == item.Id && s.Snapshot().RecurringRules.Count == 1, "Conversion duplicated or replaced item.");
        Assert(s.GenerateCurrentRecurrences() == 0, "Conversion regenerated first instance.");
        clock = clock.AddDays(1); Assert(s.GenerateCurrentRecurrences() == 1, "Converted future not generated.");
    }
    private static void ConvertPastStart()
    {
        var s = Session(new Writer()); TodoItem original = s.Add(Draft(null));
        RecurrenceOptions options = Options(RecurrenceFrequency.Daily); options.StartDate = Epoch.Date.AddDays(-4);
        s.Edit(original.Id, Draft(options));
        Assert(s.Snapshot().Items.Single().Id == original.Id && OnlyRule(s).ProcessedThrough == options.StartDate, "Conversion advanced over ungenerated history.");
        Assert(s.GetRecurrenceSummary().MissedCount == 3 && s.GetRecurrenceSummary().DueTodayCount == 1, "Conversion lost past periods.");
        Assert(s.GenerateCurrentRecurrences() == 0, "Past conversion silently resolved missed periods.");
        Assert(s.ResolveMissedRecurrences(true) == 4 && s.Snapshot().Items.Count == 5, "Conversion catch-up changed/duplicated original occurrence.");
    }
    private static void UnprocessedCurrentPeriod()
    {
        var s = Session(new Writer()); s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s);
        clock = clock.AddDays(1);
        var newDraft = Draft(Options(RecurrenceFrequency.Weekly)); newDraft.Text = "只影响未来";
        s.EditRecurringRule(id, newDraft);
        TodoState state = s.Snapshot();
        Assert(state.Items.Count == 2 && state.Items.Last().Text == "整理研究记录" && state.Items.Last().RepeatLabel == "每日", "Future edit skipped or rewrote today's old period.");
        Assert(state.RecurringRules.Single().Text == newDraft.Text && s.GenerateCurrentRecurrences() == 0, "Future edit did not preserve exactly-once cursor.");
        clock = Epoch;
        var paused = Session(new Writer()); paused.Add(Draft(Options(RecurrenceFrequency.Daily))); id = Id(paused);
        clock = clock.AddDays(1); paused.SetRecurringPaused(id, true);
        Assert(paused.Snapshot().Items.Count == 2 && OnlyRule(paused).IsPaused, "Pause discarded current period before timer.");
        clock = Epoch;
        var invalid = Session(new Writer()); invalid.Add(Draft(Options(RecurrenceFrequency.Daily))); id = Id(invalid); clock = clock.AddDays(1);
        newDraft.Recurrence.WeekDays = 0;
        Throws<ArgumentException>(delegate { invalid.EditRecurringRule(id, newDraft); });
        Assert(invalid.Snapshot().Items.Count == 1 && OnlyRule(invalid).ProcessedThrough == Epoch.Date, "Invalid draft committed today's period or cursor.");
    }
    private static void FailedTransactions()
    {
        var w = new Writer(); var s = Session(w); w.Fail = true;
        Throws<IOException>(delegate { s.Add(Draft(Options(RecurrenceFrequency.Daily))); });
        Assert(s.Snapshot().Items.Count == 0 && s.Snapshot().RecurringRules.Count == 0, "Failed add committed partial rule.");
        w.Fail = false; s.Add(Draft(Options(RecurrenceFrequency.Daily))); string id = Id(s); clock = clock.AddDays(1); w.Fail = true;
        Throws<IOException>(delegate { s.GenerateCurrentRecurrences(); });
        Assert(OnlyRule(s).ProcessedThrough == Epoch.Date && s.Snapshot().Items.Count == 1, "Failed generation moved cursor.");
        Throws<IOException>(delegate { s.SetRecurringPaused(id, true); });
        Throws<IOException>(delegate { s.StopRecurringRule(id); });
        Throws<IOException>(delegate { s.EditRecurringRule(id, Draft(Options(RecurrenceFrequency.Weekly))); });
        Assert(!OnlyRule(s).IsPaused && !OnlyRule(s).IsStopped && OnlyRule(s).Recurrence.Frequency == RecurrenceFrequency.Daily, "Failed management committed.");
        w.Fail = false; Assert(s.GenerateCurrentRecurrences() == 1 && s.GenerateCurrentRecurrences() == 0, "Retry duplicated/lost period.");
        clock = clock.AddDays(3); w.Fail = true;
        Throws<IOException>(delegate { s.ResolveMissedRecurrences(true); });
        Assert(s.Snapshot().Items.Count == 2 && s.GetRecurrenceSummary().MissedCount == 2, "Failed catchup committed.");
        w.Fail = false; Assert(s.ResolveMissedRecurrences(true) == 3, "Catchup retry wrong.");
    }
    private static void SnapshotIsolation()
    {
        var w = new Writer(); var s = Session(w); var input = Options(RecurrenceFrequency.Weekly); s.Add(Draft(input)); string id = Id(s);
        input.WeekDays = 1; TodoState copy = s.Snapshot(); copy.RecurringRules[0].Recurrence.WeekDays = 127; copy.RecurringRules.Clear();
        RecurringTaskRule rule = s.GetRecurringRule(id); rule.Recurrence.StartDate = Epoch.Date.AddYears(1); rule.Text = "变更";
        Assert(OnlyRule(s).Recurrence.WeekDays == 34 && OnlyRule(s).Recurrence.StartDate == Epoch.Date, "Snapshot retained mutable reference.");
        w.Last.RecurringRules[0].Text = "writer mutation"; Assert(OnlyRule(s).Text != "writer mutation", "Writer retained owned rule.");
    }
    private static void Persistence()
    {
        var store = new TodoStore(Path.Combine(root, "persist")); var s = new TodoSession(store, store.Load(), false, delegate { return clock; });
        TodoItem item = s.Add(Draft(Options(RecurrenceFrequency.Daily))); s.Delete(item.Id);
        string json = File.ReadAllText(store.DataFilePath); Assert(json.Contains("\"Version\":3") && json.Contains("\"RecurringRules\""), "v3 rule not explicit.");
        var restarted = new TodoSession(store, store.Load(), false, delegate { return clock; });
        Assert(restarted.GenerateCurrentRecurrences() == 0 && restarted.Snapshot().Items.Count == 0, "Deleted period reappeared after actual restart.");
        clock = clock.AddDays(1); restarted.GenerateCurrentRecurrences();
        TodoState loaded = store.Load(); Assert(loaded.Items.Single().RecurringRuleId == loaded.RecurringRules.Single().Id && loaded.Items.Single().RepeatLabel == "每日", "Occurrence metadata lost.");
    }
    private static void LegacyVersions()
    {
        for (int version = 1; version <= 2; version++) {
            var store = new TodoStore(Path.Combine(root, "legacy-" + version)); Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
            string raw = "{\"Version\":" + version + ",\"Items\":[{\"Id\":\"legacy\",\"Text\":\"原有事项\",\"CreatedAt\":\"2026-10-20T00:00:00.0000000\",\"DueDate\":\"2026-11-10T00:00:00.0000000\"}]}";
            File.WriteAllText(store.DataFilePath, raw, new UTF8Encoding(false)); byte[] before = File.ReadAllBytes(store.DataFilePath);
            TodoState loaded = store.Load(); Assert(loaded.Version == 3 && loaded.RecurringRules.Count == 0 && loaded.Items.Single().RecurringRuleId == null, "Legacy migration invented repeat.");
            Assert(before.SequenceEqual(File.ReadAllBytes(store.DataFilePath)) && !File.Exists(store.DataFilePath + ".bak"), "Legacy read wrote data.");
        }
    }
    private static void InvalidState()
    {
        var store = new TodoStore(Path.Combine(root, "invalid")); var s = new TodoSession(store, new TodoState(), false, delegate { return clock; }); s.Add(Draft(Options(RecurrenceFrequency.Daily)));
        byte[] before = File.ReadAllBytes(store.DataFilePath); TodoState invalid = s.Snapshot(); invalid.RecurringRules[0].Recurrence.Frequency = (RecurrenceFrequency)99;
        Throws<ArgumentException>(delegate { store.Save(invalid); });
        invalid = s.Snapshot(); invalid.Items.Add(TodoStateCopy.Copy(invalid.Items[0])); invalid.Items[1].Id += "-duplicate";
        Throws<InvalidDataException>(delegate { store.Save(invalid); });
        invalid = s.Snapshot(); invalid.Items[0].RecurringRuleId = "missing"; Throws<InvalidDataException>(delegate { store.Save(invalid); });
        invalid = s.Snapshot(); invalid.RecurringRules[0].ProcessedThrough = null; Throws<InvalidDataException>(delegate { store.Save(invalid); });
        Assert(before.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "Invalid recurrence replaced saved state.");
        string broken = Encoding.UTF8.GetString(before).Replace("\"Frequency\":1", "\"Frequency\":99"); File.WriteAllText(store.DataFilePath, broken, new UTF8Encoding(false));
        TodoState recovered = store.Load(); Assert(recovered.Items.Count == 0 && !String.IsNullOrEmpty(store.LoadWarning), "Malformed rule accepted.");
        Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "todos.json.corrupt-*").Length == 1, "Malformed rule bytes not preserved.");
    }
    private static void FutureVersion()
    {
        var store = new TodoStore(Path.Combine(root, "future")); store.Save(new TodoState()); store.Save(new TodoState());
        string future = File.ReadAllText(store.DataFilePath).Replace("\"Version\":3", "\"Version\":99"); File.WriteAllText(store.DataFilePath, future, new UTF8Encoding(false));
        byte[] main = File.ReadAllBytes(store.DataFilePath), backup = File.ReadAllBytes(store.DataFilePath + ".bak");
        Throws<NotSupportedException>(delegate { store.Load(); });
        Assert(main.SequenceEqual(File.ReadAllBytes(store.DataFilePath)) && backup.SequenceEqual(File.ReadAllBytes(store.DataFilePath + ".bak")), "Future version altered files.");
        Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "*.corrupt-*").Length == 0, "Future version archived as corruption.");
    }
    private static void Preview()
    {
        var w = new Writer(); var s = new TodoSession(w, new TodoState(), true, delegate { return clock; }); s.Add(Draft(Options(RecurrenceFrequency.Daily)));
        string id = Id(s); clock = clock.AddDays(2); s.ResolveMissedRecurrences(true); s.SetRecurringPaused(id, true); s.SetRecurringPaused(id, false); s.StopRecurringRule(id);
        Assert(w.Calls == 0 && s.Snapshot().Items.Count == 3, "Preview wrote files or failed memory commands.");
    }
    private sealed class Writer : ITodoStore
    {
        public int Calls; public bool Fail; public TodoState Last;
        public void Save(TodoState state) { Calls++; if (Fail) throw new IOException("simulated disk failure"); Last = state; }
    }
}
