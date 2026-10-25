using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CornerTodo;

internal static class StoreTests
{
    private static int passed;
    private static string root;

    public static int Main()
    {
        // Keep test data independent of the source/build directory depth.
        root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CornerTodo-StoreTests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            Run("First launch has usable defaults without writing files", Defaults);
            Run("Unicode, timestamps, status and window settings survive restart", UnicodeRoundTrip);
            Run("Every successful replacement preserves the previous generation", BackupGenerations);
            Run("A locked destination cannot destroy the last saved data", FailedSavePreservesData);
            Run("A corrupt main file is archived and recovered from backup", CorruptMainRecovery);
            Run("Invalid UTF-8 bytes are preserved and recover from backup", InvalidUtf8Recovery);
            Run("Unrecoverable JSON remains available and produces a warning", UnrecoverableJson);
            Run("Missing main data recovers the backup", MissingMainRecovery);
            Run("Missing settings use compatible defaults", SettingsCompatibility);
            Run("Semantically invalid tasks are preserved rather than discarded", InvalidTaskIsNotDropped);
            Run("Date-only and exact-time overdue boundaries are correct", DeadlineBoundaries);
            Run("Pending order uses the effective deadline; completed order uses completion time", Ordering);
            Run("Complete, restart, reopen, restart preserves task identity and date", CompleteAndReopen);
            Run("Due labels include only the time the user supplied", DueLabels);
            Run("Long tasks grow only in their last 24 hours and accelerate toward the deadline", LongTaskCurve);
            Run("A five-minute task grows across its original five-minute interval", ShortTaskCurve);
            Run("Six-hour urgent and overdue counts never overlap", AttentionCounts);
            Run("Overdue attention persists until completion and returns when reopened", PersistentOverdue);
            Run("Expansion uses only the top three contributions and stays bounded", ExpansionAggregation);
            Run("Invalid scheduling intervals remain finite before and after the deadline", InvalidSchedulingWindow);
            Run("Attention labels match remaining, overdue and completed states", AttentionLabels);
            Run("Original version-one files migrate without corruption or resetting scheduling", LegacyAttentionCompatibility);
            Run("Actual rescheduling resets progress while text and completion changes preserve it", ScheduleAndStateChanges);
            Run("Date-only attention uses next midnight, including after a reschedule", DateOnlyAttention);
            Run("Restart preserves timing, auto-expand preference and manual geometry without drift", AttentionRestartStability);
            Run("New users get independent readable light appearance defaults", AppearanceDefaults);
            Run("Old files retain their original appearance, even without Settings", LegacyAppearanceMigration);
            Run("Appearance fields save independently without changing task dates or identity", AppearanceRoundTrip);
            Run("Invalid optional appearance data cannot discard valid tasks", InvalidAppearanceRecovery);
            Run("Saving invalid appearance normalizes only the affected fields", InvalidAppearanceSave);
            Run("Opaque and fully transparent background endpoints survive restart", AppearanceTransparencyEndpoints);
            Run("Cloning and normalization isolate live appearance from draft edits", AppearanceCloneIsolation);
            Console.WriteLine("PASS: " + passed + " tests");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            // Delete only the unique directory created by this test in Temp.
            string tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            try {
                if (root.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(root).StartsWith("CornerTodo-StoreTests-", StringComparison.Ordinal))
                    Directory.Delete(root, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("  OK " + name);
    }

    private static TodoStore Store(string name) { return new TodoStore(Path.Combine(root, name)); }
    private static TodoItem Item(string id, DateTime due, bool hasTime)
    {
        return new TodoItem { Id = id, Text = "待办 " + id, CreatedAt = new DateTime(2026, 10, 20, 9, 0, 0), DueDate = due, HasTime = hasTime };
    }
    private static TodoState State(string text)
    {
        TodoState state = new TodoState();
        TodoItem item = Item("stable-id", new DateTime(2026, 10, 21), false);
        item.Text = text;
        state.Items.Add(item);
        return state;
    }

    private static void Defaults()
    {
        TodoStore store = Store("first-run");
        TodoState state = store.Load();
        Assert(state.Version == 1 && state.Items.Count == 0, "Default state is invalid.");
        Assert(state.Settings.Width == 430 && state.Settings.Height == 475 && !state.Settings.Collapsed, "Default geometry is invalid.");
        Assert(state.Settings.Left == null && state.Settings.Top == null, "First-run position should be unset.");
        Assert(store.LoadWarning == null && !File.Exists(store.DataFilePath), "First load should not write data or warn.");
    }

    private static void UnicodeRoundTrip()
    {
        TodoStore store = Store("unicode");
        TodoState state = State("联系小王：提交测量报告 📌\n第二行\t\"引用\" \\ 路径");
        TodoItem item = state.Items[0];
        item.CreatedAt = new DateTime(2026, 10, 20, 18, 20, 30, DateTimeKind.Local).AddTicks(1234);
        item.DueDate = new DateTime(2026, 11, 1, 0, 15, 0, DateTimeKind.Unspecified);
        item.HasTime = true;
        item.IsCompleted = true;
        item.CompletedAt = new DateTime(2026, 10, 20, 18, 30, 0, DateTimeKind.Utc);
        state.Settings.Left = -1280.5;
        state.Settings.Top = 12.25;
        state.Settings.Width = 456.5;
        state.Settings.Height = 610.75;
        state.Settings.CompletedCollapsed = true;
        state.Settings.Collapsed = true;
        store.Save(state);
        TodoStore restarted = new TodoStore(Path.GetDirectoryName(store.DataFilePath));
        TodoState loaded = restarted.Load();
        TodoItem actual = loaded.Items.Single();
        Assert(actual.Text == item.Text && actual.Id == item.Id, "Unicode text or identity changed.");
        Assert(actual.CreatedAt == item.CreatedAt && actual.CreatedAt.Kind == item.CreatedAt.Kind, "Creation timestamp or kind changed.");
        Assert(actual.DueDate == item.DueDate && actual.DueDate.Kind == item.DueDate.Kind, "Wall-clock due date changed.");
        Assert(actual.CompletedAt == item.CompletedAt && actual.CompletedAt.Value.Kind == DateTimeKind.Utc && actual.IsCompleted, "Completion state changed.");
        Assert(loaded.Settings.Left == -1280.5 && loaded.Settings.Top == 12.25 && loaded.Settings.Width == 456.5 && loaded.Settings.Height == 610.75 && loaded.Settings.CompletedCollapsed && loaded.Settings.Collapsed, "Window settings changed.");
        Assert(restarted.LoadWarning == null, "Valid data should not warn.");
    }

    private static void BackupGenerations()
    {
        TodoStore store = Store("backup-generations");
        store.Save(State("第一版"));
        store.Save(State("第二版"));
        store.Save(State("第三版"));
        Assert(store.Load().Items[0].Text == "第三版", "Newest state was not saved.");
        File.Delete(store.DataFilePath);
        Assert(store.Load().Items[0].Text == "第二版", "Backup did not retain the previous generation.");
        Assert(!String.IsNullOrEmpty(store.LoadWarning), "Backup recovery must be visible.");
    }

    private static void FailedSavePreservesData()
    {
        TodoStore store = Store("failed-save");
        store.Save(State("原始资料"));
        byte[] original = File.ReadAllBytes(store.DataFilePath);
        bool failed = false;
        using (FileStream blocker = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { store.Save(State("未成功的新资料")); }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }
        }
        Assert(failed, "The real destination lock did not trigger a write failure.");
        Assert(original.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "Failed save modified the original bytes.");
        Assert(store.Load().Items[0].Text == "原始资料", "Failed save lost the original task.");
        Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), ".todos-*.tmp").Length == 0, "Failed save left a partial temporary file.");
    }

    private static void CorruptMainRecovery()
    {
        TodoStore store = Store("corrupt-main");
        store.Save(State("安全备份"));
        store.Save(State("最新资料"));
        const string broken = "{\"Items\":[损坏";
        File.WriteAllText(store.DataFilePath, broken, Encoding.UTF8);
        TodoState recovered = store.Load();
        Assert(recovered.Items.Single().Text == "安全备份", "Valid backup was not recovered.");
        Assert(!String.IsNullOrEmpty(store.LoadWarning), "Corruption was silent.");
        string[] preserved = Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "todos.json.corrupt-*");
        Assert(preserved.Length == 1 && File.ReadAllText(preserved[0], Encoding.UTF8) == broken, "Corrupt input was not preserved verbatim.");
        Assert(store.Load().Items[0].Text == "安全备份" && store.LoadWarning == null, "Recovered main data did not survive the next load.");
    }

    private static void UnrecoverableJson()
    {
        TodoStore store = Store("unrecoverable");
        Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
        const string broken = "[无法读取的原始待办";
        File.WriteAllText(store.DataFilePath, broken, Encoding.UTF8);
        File.WriteAllText(store.DataFilePath + ".bak", "broken backup", Encoding.UTF8);
        Assert(store.Load().Items.Count == 0, "Unrecoverable data should yield a usable empty state.");
        Assert(!String.IsNullOrEmpty(store.LoadWarning), "Both corrupt files were silently lost.");
        string[] preserved = Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "*.corrupt-*");
        Assert(preserved.Length == 2, "Not all damaged inputs were archived.");
        store.Save(State("重新添加"));
        Assert(preserved.Any(path => File.ReadAllText(path, Encoding.UTF8) == broken), "Saving new data erased the damaged original.");
    }

    private static void InvalidUtf8Recovery()
    {
        TodoStore store = Store("invalid-utf8");
        store.Save(State("可以恢复的备份"));
        store.Save(State("第二份资料"));
        byte[] broken = new byte[] { 0x7B, 0x22, 0xC3, 0x28, 0x22, 0x7D };
        File.WriteAllBytes(store.DataFilePath, broken);
        Assert(store.Load().Items.Single().Text == "可以恢复的备份", "Malformed UTF-8 prevented backup recovery.");
        string preserved = Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "todos.json.corrupt-*").Single();
        Assert(File.ReadAllBytes(preserved).SequenceEqual(broken) && !String.IsNullOrEmpty(store.LoadWarning), "Malformed bytes were not preserved or announced.");
    }

    private static void MissingMainRecovery()
    {
        TodoStore store = Store("missing-main");
        store.Save(State("前一版"));
        store.Save(State("最后一版"));
        File.Delete(store.DataFilePath);
        Assert(store.Load().Items.Single().Text == "前一版", "Missing main did not recover its backup.");
        Assert(File.Exists(store.DataFilePath) && !String.IsNullOrEmpty(store.LoadWarning), "Recovery was not materialized or announced.");
    }

    private static void SettingsCompatibility()
    {
        TodoStore store = Store("settings-compatibility");
        Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
        File.WriteAllText(store.DataFilePath, "{\"Items\":[],\"Settings\":{\"Left\":-400,\"Top\":20}}", Encoding.UTF8);
        TodoState state = store.Load();
        Assert(state.Version == 1 && state.Settings.Width == 430 && state.Settings.Height == 475 && !state.Settings.Collapsed, "Missing optional fields did not use defaults.");
        Assert(state.Settings.Left == -400 && state.Settings.Top == 20 && store.LoadWarning == null, "Existing optional settings were not preserved.");
    }

    private static void InvalidTaskIsNotDropped()
    {
        TodoStore store = Store("invalid-task");
        Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
        string json = "{\"Version\":1,\"Items\":[{\"Id\":\"missing-due\",\"Text\":\"不能静默删除\",\"CreatedAt\":\"2026-10-20T09:00:00.0000000\"}]}";
        File.WriteAllText(store.DataFilePath, json, Encoding.UTF8);
        store.Load();
        Assert(!String.IsNullOrEmpty(store.LoadWarning), "Malformed task was silently discarded.");
        string preserved = Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "todos.json.corrupt-*").Single();
        Assert(File.ReadAllText(preserved, Encoding.UTF8) == json, "Semantically invalid source was not preserved.");
    }

    private static void DeadlineBoundaries()
    {
        TodoItem day = Item("day", new DateTime(2026, 10, 20, 13, 14, 15), false);
        Assert(TodoRules.Deadline(day) == new DateTime(2026, 10, 21), "Date-only deadline is not next midnight.");
        Assert(!TodoRules.IsOverdue(day, new DateTime(2026, 10, 20, 23, 59, 59).AddTicks(9999999)), "Date-only task became overdue before its day ended.");
        Assert(TodoRules.IsOverdue(day, new DateTime(2026, 10, 21)), "Date-only task should be overdue at next midnight.");
        TodoItem timed = Item("time", new DateTime(2026, 10, 20, 15, 30, 0), true);
        Assert(!TodoRules.IsOverdue(timed, timed.DueDate.AddTicks(-1)) && TodoRules.IsOverdue(timed, timed.DueDate), "Exact-time deadline boundary is incorrect.");
        timed.IsCompleted = true;
        Assert(!TodoRules.IsOverdue(timed, timed.DueDate.AddYears(1)), "Completed task should not be overdue.");
    }

    private static void Ordering()
    {
        TodoState state = new TodoState();
        TodoItem tomorrowMorning = Item("tomorrow", new DateTime(2026, 10, 21, 8, 0, 0), true);
        TodoItem allDay = Item("all-day", new DateTime(2026, 10, 20), false);
        TodoItem tonight = Item("tonight", new DateTime(2026, 10, 20, 20, 0, 0), true);
        TodoItem earlierCompletion = Item("done-first", new DateTime(2026, 10, 25), false);
        earlierCompletion.IsCompleted = true;
        earlierCompletion.CompletedAt = new DateTime(2026, 10, 20, 11, 0, 0);
        TodoItem laterCompletion = Item("done-last", new DateTime(2026, 10, 19), false);
        laterCompletion.IsCompleted = true;
        laterCompletion.CompletedAt = new DateTime(2026, 10, 20, 12, 0, 0);
        state.Items.AddRange(new[] { tomorrowMorning, earlierCompletion, allDay, laterCompletion, tonight });
        Assert(String.Join(",", TodoRules.Pending(state).Select(item => item.Id)) == "tonight,all-day,tomorrow", "Pending order ignored effective date-only deadline.");
        Assert(String.Join(",", TodoRules.Completed(state).Select(item => item.Id)) == "done-last,done-first", "Completed order ignored completion timestamp.");
    }

    private static void CompleteAndReopen()
    {
        TodoStore store = Store("state-transitions");
        TodoState state = State("可以完成和恢复");
        state.Items[0].IsCompleted = true;
        state.Items[0].CompletedAt = new DateTime(2026, 10, 20, 10, 30, 0);
        store.Save(state);
        state = store.Load();
        Assert(TodoRules.Completed(state).Count() == 1 && !TodoRules.Pending(state).Any(), "Completed state did not survive restart.");
        state.Items[0].IsCompleted = false;
        state.Items[0].CompletedAt = null;
        store.Save(state);
        state = store.Load();
        Assert(TodoRules.Pending(state).Single().Id == "stable-id" && state.Items[0].CompletedAt == null && state.Items[0].DueDate == new DateTime(2026, 10, 21), "Reopening changed identity, date or completion state.");
    }

    private static void DueLabels()
    {
        DateTime now = new DateTime(2026, 10, 20, 14, 0, 0);
        Assert(TodoRules.FormatDue(Item("today", now.Date, false), now) == "今天", "Date-only label contains unwanted time.");
        Assert(TodoRules.FormatDue(Item("tomorrow", new DateTime(2026, 10, 21, 9, 5, 0), true), now) == "明天 09:05", "Timed label lost the entered time.");
        Assert(TodoRules.FormatDue(Item("next-year", new DateTime(2027, 1, 1), false), now) == "2027年1月1日", "Cross-year label is ambiguous.");
    }

    private static void LongTaskCurve()
    {
        DateTime created = new DateTime(2026, 10, 1, 9, 0, 0);
        TodoItem item = Item("thirty-days", created.AddDays(30), true);
        item.CreatedAt = created;
        AssertNear(TodoRules.Urgency(item, created.AddDays(1)), 0, "A 30-day task enlarged the panel a month early.");
        AssertNear(TodoRules.Urgency(item, item.DueDate.AddHours(-24)), 0, "The 24-hour growth window starts at zero.");
        AssertNear(TodoRules.Urgency(item, item.DueDate.AddHours(-12)), 0.25, "The halfway growth point is incorrect.");
        AssertNear(TodoRules.Urgency(item, item.DueDate.AddHours(-6)), 0.5625, "Quadratic growth should accelerate near the deadline.");
        double previous = 0;
        for (int hour = 24; hour >= 0; hour--)
        {
            double actual = TodoRules.Urgency(item, item.DueDate.AddHours(-hour));
            Assert(actual >= previous && actual >= 0 && actual <= 1, "An unchanged deadline grew backward or outside its bounds.");
            previous = actual;
        }
    }

    private static void ShortTaskCurve()
    {
        DateTime created = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoItem item = Item("five-minutes", created.AddMinutes(5), true);
        item.CreatedAt = created;
        Assert(TodoRules.IsUrgent(item, created), "A newly created five-minute task should be urgent immediately.");
        AssertNear(TodoRules.Urgency(item, created), 0, "A new short task immediately jumped to full expansion.");
        AssertNear(TodoRules.Urgency(item, created.AddMinutes(1)), 0.04, "One minute into a five-minute task has the wrong progress.");
        AssertNear(TodoRules.Urgency(item, created.AddMinutes(2.5)), 0.25, "A short task did not use its fixed original interval.");
        AssertNear(TodoRules.Urgency(item, created.AddMinutes(4)), 0.64, "Short task progress did not accelerate.");
        AssertNear(TodoRules.Urgency(item, item.DueDate), 1, "A deadline reached its cutoff without full attention.");
    }

    private static void AttentionCounts()
    {
        DateTime now = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoItem boundary = Item("six-hours", now.AddHours(6), true);
        TodoItem afterBoundary = Item("too-early", now.AddHours(6).AddTicks(1), true);
        TodoItem imminent = Item("one-tick-left", now.AddTicks(1), true);
        TodoItem cutoff = Item("exact-cutoff", now, true);
        TodoItem old = Item("old", now.AddDays(-2), true);
        TodoItem completed = Item("completed", now.AddMinutes(1), true);
        completed.IsCompleted = true;
        TodoState state = new TodoState { Items = new List<TodoItem> { boundary, afterBoundary, imminent, cutoff, old, completed } };
        Assert(TodoRules.IsUrgent(boundary, now) && !TodoRules.IsUrgent(afterBoundary, now), "The six-hour inclusive boundary is wrong.");
        Assert(TodoRules.OverdueCount(state, now) == 2 && TodoRules.UrgentCount(state, now) == 2, "Urgent and overdue counts are incorrect.");
        Assert(state.Items.All(item => !(TodoRules.IsUrgent(item, now) && TodoRules.IsOverdue(item, now))), "A task was counted in both attention groups.");
    }

    private static void PersistentOverdue()
    {
        DateTime due = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoItem item = Item("persistent", due, true);
        item.DeadlineSetAt = due.AddHours(-1);
        AssertNear(TodoRules.Urgency(item, due.AddMinutes(1)), 1, "Freshly overdue task should demand maximum attention.");
        AssertNear(TodoRules.Urgency(item, due.AddYears(10)), 1, "Old overdue task incorrectly faded away.");
        item.IsCompleted = true;
        AssertNear(TodoRules.Urgency(item, due.AddYears(10)), 0, "Completed task still enlarges the panel.");
        item.IsCompleted = false;
        AssertNear(TodoRules.Urgency(item, due.AddYears(10)), 1, "Reopened overdue task lost its persistent attention.");
        Assert(item.DeadlineSetAt == due.AddHours(-1), "Completion changes reset the scheduling epoch.");
    }

    private static TodoItem ProgressItem(string id, DateTime now, double progress)
    {
        TodoItem item = Item(id, now.AddHours(24 * (1 - progress)), true);
        item.CreatedAt = item.DueDate.AddHours(-24);
        return item;
    }

    private static void ExpansionAggregation()
    {
        DateTime now = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoState state = new TodoState();
        state.Items.AddRange(new[] { ProgressItem("strong", now, 0.8), ProgressItem("second", now, 0.6), ProgressItem("third", now, 0.4) });
        AssertNear(TodoRules.Expansion(state, now), 0.702, "The top-three contributions have incorrect weights.");
        for (int i = 0; i < 100; i++) state.Items.Add(ProgressItem("ordinary-" + i, now, 0.3));
        AssertNear(TodoRules.Expansion(state, now), 0.702, "Adding many lower-priority tasks accumulated unbounded growth.");
        TodoItem done = Item("completed-overdue", now.AddDays(-10), true); done.IsCompleted = true;
        state.Items.Add(done);
        AssertNear(TodoRules.Expansion(state, now), 0.702, "Completed overdue tasks still contribute to expansion.");
        state.Items = new List<TodoItem> { ProgressItem("a", now, 0.95), ProgressItem("b", now, 0.95), ProgressItem("c", now, 0.95) };
        AssertNear(TodoRules.Expansion(state, now), 1, "Combined attention exceeded or did not reach its cap.");
        state.Items = new List<TodoItem> { Item("one-overdue", now.AddTicks(-1), true) };
        AssertNear(TodoRules.Expansion(state, now), 1, "A single overdue task must reach the hard cap.");
        AssertNear(TodoRules.Expansion(new TodoState(), now), 0, "An empty panel should not grow.");
    }

    private static void InvalidSchedulingWindow()
    {
        DateTime due = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoItem item = Item("invalid-interval", due, true);
        item.DeadlineSetAt = due;
        AssertNear(TodoRules.Urgency(item, due.AddMinutes(-1)), 0, "Zero window caused premature growth.");
        item.DeadlineSetAt = due.AddHours(1);
        AssertNear(TodoRules.Urgency(item, due.AddMinutes(-1)), 0, "Negative window caused growth or NaN.");
        AssertNear(TodoRules.Urgency(item, due), 1, "An invalid scheduling interval suppressed a genuinely overdue task.");
        item.DeadlineSetAt = due.AddHours(-1);
        AssertNear(TodoRules.Urgency(item, due.AddHours(-2)), 0, "Clock movement before the scheduling epoch produced negative growth.");
        AssertNear(TodoRules.Urgency(null, due), 0, "Null task produced an invalid contribution.");
    }

    private static void AttentionLabels()
    {
        DateTime now = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoItem item = Item("label", now.AddHours(7), true);
        Assert(TodoRules.FormatAttention(item, now) == TodoRules.FormatDue(item, now), "Nonurgent task has an attention suffix.");
        item.DueDate = now.AddMinutes(90);
        Assert(TodoRules.FormatAttention(item, now) == "今天 15:30 · 剩余1小时30分钟", "Remaining hours and minutes are ambiguous.");
        item.DueDate = now.AddSeconds(20);
        Assert(TodoRules.FormatAttention(item, now).EndsWith(" · 剩余不足1分钟"), "An unfinished task showed zero remaining minutes.");
        item.DueDate = now.AddMinutes(-15);
        Assert(TodoRules.FormatAttention(item, now).EndsWith(" · 已逾期15分钟"), "Overdue minute label is incorrect.");
        item.DueDate = now.AddHours(-2);
        Assert(TodoRules.FormatAttention(item, now).EndsWith(" · 已逾期2小时"), "Overdue hour label is incorrect.");
        item.DueDate = now.AddDays(-2);
        Assert(TodoRules.FormatAttention(item, now).EndsWith(" · 已逾期2天"), "Overdue day label is incorrect.");
        item.IsCompleted = true;
        Assert(TodoRules.FormatAttention(item, now) == TodoRules.FormatDue(item, now), "Completed task retained its overdue suffix.");
    }

    private static void LegacyAttentionCompatibility()
    {
        TodoStore store = Store("legacy-attention");
        Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
        string original = "{\"Version\":1,\"Items\":[{\"Id\":\"old-task\",\"Text\":\"旧版任务\",\"CreatedAt\":\"2026-10-20T12:00:00.0000000\",\"DueDate\":\"2026-10-20T14:00:00.0000000\",\"HasTime\":true}],\"Settings\":{\"Width\":460,\"Height\":530}}";
        File.WriteAllText(store.DataFilePath, original, Encoding.UTF8);
        TodoState state = store.Load();
        DateTime now = new DateTime(2026, 10, 20, 13, 0, 0);
        Assert(store.LoadWarning == null && state.Version == 1 && state.Items.Count == 1, "Original schema-version-one data was mistaken for corruption.");
        Assert(state.Items[0].DeadlineSetAt == null && state.Settings.AutoExpand, "New optional fields did not use compatible defaults.");
        AssertNear(TodoRules.Urgency(state.Items[0], now), 0.25, "Missing scheduling timestamp did not fall back to CreatedAt.");
        store.Save(state);
        state = new TodoStore(Path.GetDirectoryName(store.DataFilePath)).Load();
        Assert(state.Items[0].DeadlineSetAt == null && state.Settings.Width == 460 && state.Settings.Height == 530, "Migrating optional fields changed manual geometry or the legacy epoch.");
        AssertNear(TodoRules.Expansion(state, now), 0.25, "Migration changed existing task attention.");
        Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "*.corrupt-*").Length == 0, "A valid old file was archived as damaged.");
    }

    private static void ScheduleAndStateChanges()
    {
        TodoStore store = Store("schedule-state-changes");
        DateTime scheduled = new DateTime(2026, 10, 20, 14, 0, 0);
        TodoItem item = Item("rescheduled", scheduled.AddHours(1), true);
        item.CreatedAt = scheduled.AddDays(-30);
        Assert(TodoRules.Urgency(item, scheduled) > 0.9, "A long-standing near-due task should already demand attention.");
        item.DeadlineSetAt = scheduled;
        AssertNear(TodoRules.Urgency(item, scheduled), 0, "Actual rescheduling did not begin a new short growth interval.");
        TodoState state = new TodoState { Items = new List<TodoItem> { item } };
        store.Save(state);
        DateTime now = scheduled.AddMinutes(30);
        state = store.Load();
        state.Items[0].Text = "只修改文字，继续原来的时钟";
        store.Save(state);
        state = store.Load();
        AssertNear(TodoRules.Urgency(state.Items[0], now), 0.25, "Text editing restarted attention progress.");
        state.Items[0].IsCompleted = true; state.Items[0].CompletedAt = now;
        store.Save(state);
        state = store.Load();
        AssertNear(TodoRules.Expansion(state, now), 0, "Completion did not release automatic expansion.");
        state.Items[0].IsCompleted = false; state.Items[0].CompletedAt = null;
        store.Save(state);
        state = store.Load();
        Assert(state.Items[0].DeadlineSetAt == scheduled && state.Items[0].CreatedAt == scheduled.AddDays(-30), "Restoring a task rewrote its scheduling timestamps.");
        AssertNear(TodoRules.Urgency(state.Items[0], now), 0.25, "Restoring a pending task restarted or advanced the original interval.");
    }

    private static void DateOnlyAttention()
    {
        DateTime scheduled = new DateTime(2026, 10, 20, 20, 0, 0);
        TodoItem item = Item("date-only-attention", scheduled.Date, false);
        item.DeadlineSetAt = scheduled;
        DateTime midnight = new DateTime(2026, 10, 21);
        AssertNear(TodoRules.Urgency(item, scheduled), 0, "A date-only reschedule started from the stored midnight instead of the following midnight.");
        AssertNear(TodoRules.Urgency(item, scheduled.AddHours(2)), 0.25, "Date-only growth used the wrong duration.");
        Assert(TodoRules.IsUrgent(item, midnight.AddTicks(-1)) && !TodoRules.IsOverdue(item, midnight.AddTicks(-1)), "Date-only task became overdue before its calendar day ended.");
        Assert(!TodoRules.IsUrgent(item, midnight) && TodoRules.IsOverdue(item, midnight), "The midnight boundary was counted twice or missed.");
        AssertNear(TodoRules.Urgency(item, midnight), 1, "Date-only task did not reach maximum at next midnight.");
        item.DueDate = scheduled.Date.AddDays(1);
        item.DeadlineSetAt = midnight;
        Assert(TodoRules.Deadline(item) == midnight.AddDays(1), "Moving a date-only deadline changed its calendar meaning.");
        AssertNear(TodoRules.Urgency(item, midnight), 0, "A next-day reschedule did not reset its scheduling epoch.");
        AssertNear(TodoRules.Urgency(item, midnight.AddHours(12)), 0.25, "Rescheduled date-only growth did not use its new interval.");
    }

    private static void AttentionRestartStability()
    {
        TodoStore store = Store("attention-restart");
        DateTime scheduled = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Local).AddTicks(1234);
        TodoItem item = Item("stable-attention", scheduled.AddHours(1), true);
        item.DeadlineSetAt = scheduled;
        TodoState state = new TodoState { Items = new List<TodoItem> { item } };
        state.Settings.AutoExpand = false;
        state.Settings.Width = 456.5; state.Settings.Height = 610.75;
        state.Settings.Left = -1280.5; state.Settings.Top = 12.25;
        store.Save(state);
        for (int restart = 0; restart < 5; restart++)
        {
            store = new TodoStore(Path.GetDirectoryName(store.DataFilePath));
            state = store.Load();
            Assert(state.Items[0].DeadlineSetAt == scheduled && state.Items[0].DeadlineSetAt.Value.Kind == DateTimeKind.Local, "Restart changed the fixed epoch or its DateTime kind.");
            AssertNear(TodoRules.Expansion(state, scheduled.AddMinutes(30)), 0.25, "Restart or disabling auto expansion changed the task's urgency calculation.");
            TodoRules.Expansion(state, scheduled.AddYears(2));
            Assert(!state.Settings.AutoExpand && state.Settings.Width == 456.5 && state.Settings.Height == 610.75 && state.Settings.Left == -1280.5 && state.Settings.Top == 12.25, "Automatic attention overwrote the manual geometry or preference.");
            store.Save(state);
        }
        string json = File.ReadAllText(store.DataFilePath, Encoding.UTF8);
        Assert(json.Contains("\"DeadlineSetAt\":\"2026-10-20T14:00:00.0001234"), "Nullable scheduling timestamp was not saved in precise ISO format.");
    }

    private static void AppearanceDefaults()
    {
        AppearanceSettings light = new WidgetSettings().Appearance;
        Assert(light != null && light.BackgroundTransparency == 20 && light.BackgroundColor == "#F4F9FB"
            && light.PrimaryTextColor == "#193C50" && light.SecondaryTextColor == "#537486", "New settings are not the readable light preset.");
        TodoStore store = Store("appearance-first-run");
        Assert(store.Load().Settings.Appearance.Matches(light), "First launch did not use the new default appearance.");
        Assert(!File.Exists(store.DataFilePath) && store.LoadWarning == null, "New appearance defaults unexpectedly wrote data or warned.");
        AppearanceSettings dark = AppearanceSettings.CreateDark();
        Assert(dark.BackgroundTransparency == 15 && dark.BackgroundColor == "#182B38"
            && dark.PrimaryTextColor == "#F1F5F8" && dark.SecondaryTextColor == "#BAC8D2", "The dark preset does not match the agreed colors.");
        AppearanceSettings original = AppearanceSettings.CreateOriginal();
        AssertNear(original.BackgroundTransparency, 100.0 - 32.0 / 255.0 * 100.0, "Original background alpha was rounded or inverted.");
        Assert(original.BackgroundColor == "#FFFFFF" && original.PrimaryTextColor == "#193C50"
            && original.SecondaryTextColor == "#537486", "Original preset did not preserve version-one colors.");
        light.BackgroundColor = "#123456";
        Assert(new WidgetSettings().Appearance.BackgroundColor == "#F4F9FB", "New settings share a mutable appearance instance.");
    }

    private static string AppearanceDocument(string settingsJson)
    {
        return "{\"Version\":1,\"Items\":[{\"Id\":\"appearance-stable\",\"Text\":\"颜色不能影响任务 📌\",\"IsCompleted\":true,"
            + "\"CreatedAt\":\"2026-10-20T09:00:00.0001234Z\",\"CompletedAt\":\"2026-10-24T10:00:00.0002468Z\","
            + "\"DueDate\":\"2026-10-25T18:30:00.0005678\",\"HasTime\":true,\"DeadlineSetAt\":\"2026-10-24T08:00:00.0009876Z\"}]"
            + (settingsJson == null ? "" : ",\"Settings\":" + settingsJson) + "}";
    }

    private static void AssertAppearanceTask(TodoState state)
    {
        Assert(state.Version == 1 && state.Items.Count == 1, "Appearance migration changed schema version or task count.");
        TodoItem item = state.Items.Single();
        Assert(item.Id == "appearance-stable" && item.Text == "颜色不能影响任务 📌" && item.IsCompleted && item.HasTime, "Appearance changed task identity, Unicode text or status.");
        Assert(item.CreatedAt == new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc).AddTicks(1234)
            && item.CreatedAt.Kind == DateTimeKind.Utc, "Appearance changed the creation timestamp.");
        Assert(item.CompletedAt == new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc).AddTicks(2468)
            && item.CompletedAt.Value.Kind == DateTimeKind.Utc, "Appearance changed the completion timestamp.");
        Assert(item.DueDate == new DateTime(2026, 10, 25, 18, 30, 0, DateTimeKind.Unspecified).AddTicks(5678)
            && item.DueDate.Kind == DateTimeKind.Unspecified, "Appearance changed the wall-clock deadline.");
        Assert(item.DeadlineSetAt == new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc).AddTicks(9876)
            && item.DeadlineSetAt.Value.Kind == DateTimeKind.Utc, "Appearance restarted the scheduling clock.");
    }

    private static void LegacyAppearanceMigration()
    {
        string[] legacySettings = { null, "null", "{}", "{\"Width\":460,\"Height\":530,\"AutoExpand\":false}" };
        for (int i = 0; i < legacySettings.Length; i++)
        {
            TodoStore store = Store("appearance-migration-" + i);
            Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
            File.WriteAllText(store.DataFilePath, AppearanceDocument(legacySettings[i]), Encoding.UTF8);
            byte[] originalBytes = File.ReadAllBytes(store.DataFilePath);
            TodoState state = store.Load();
            AssertAppearanceTask(state);
            Assert(state.Settings.Appearance.Matches(AppearanceSettings.CreateOriginal()), "An old file silently changed to the new light preset.");
            Assert(store.LoadWarning == null && originalBytes.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "Loading a legacy file warned or rewrote the document.");
            if (i == 3) Assert(state.Settings.Width == 460 && state.Settings.Height == 530 && !state.Settings.AutoExpand, "Appearance migration reset existing preferences.");
            store.Save(state);
            state = new TodoStore(Path.GetDirectoryName(store.DataFilePath)).Load();
            AssertAppearanceTask(state);
            Assert(state.Settings.Appearance.Matches(AppearanceSettings.CreateOriginal()), "The migrated original appearance did not survive restart.");
            Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "*.corrupt-*").Length == 0, "A valid legacy file was treated as damaged.");
        }
    }

    private static void AppearanceRoundTrip()
    {
        TodoStore store = Store("appearance-round-trip");
        Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
        File.WriteAllText(store.DataFilePath, AppearanceDocument("{\"Left\":-400,\"Top\":20,\"Width\":470,\"Height\":510,\"AutoExpand\":false}"), Encoding.UTF8);
        TodoState state = store.Load();
        AppearanceSettings draft = new AppearanceSettings {
            BackgroundTransparency = 12.375, BackgroundColor = "#0a1b2c",
            PrimaryTextColor = "#FeDcBa", SecondaryTextColor = "#987654"
        };
        state.Settings.Appearance = draft;
        store.Save(state);
        Assert(draft.BackgroundColor == "#0a1b2c" && draft.PrimaryTextColor == "#FeDcBa", "Saving unexpectedly mutated the caller's draft.");
        state = store.Load();
        AppearanceSettings appearance = state.Settings.Appearance;
        Assert(appearance.BackgroundTransparency == 12.375 && appearance.BackgroundColor == "#0A1B2C"
            && appearance.PrimaryTextColor == "#FEDCBA" && appearance.SecondaryTextColor == "#987654", "Independent appearance values were lost or not canonicalized.");
        appearance.BackgroundColor = "#112233";
        store.Save(state); state = store.Load();
        Assert(state.Settings.Appearance.BackgroundColor == "#112233" && state.Settings.Appearance.BackgroundTransparency == 12.375
            && state.Settings.Appearance.PrimaryTextColor == "#FEDCBA" && state.Settings.Appearance.SecondaryTextColor == "#987654", "Changing the background color reset another field.");
        state.Settings.Appearance.BackgroundTransparency = 73.25;
        store.Save(state); state = store.Load();
        Assert(state.Settings.Appearance.BackgroundTransparency == 73.25 && state.Settings.Appearance.BackgroundColor == "#112233"
            && state.Settings.Appearance.PrimaryTextColor == "#FEDCBA" && state.Settings.Appearance.SecondaryTextColor == "#987654", "Changing transparency reset a color.");
        state.Settings.Appearance.PrimaryTextColor = "#445566";
        store.Save(state); state = store.Load();
        Assert(state.Settings.Appearance.PrimaryTextColor == "#445566" && state.Settings.Appearance.SecondaryTextColor == "#987654", "Primary and secondary text colors are coupled.");
        state.Settings.Appearance.SecondaryTextColor = "#778899";
        store.Save(state); state = new TodoStore(Path.GetDirectoryName(store.DataFilePath)).Load();
        Assert(state.Settings.Appearance.SecondaryTextColor == "#778899" && state.Settings.Appearance.PrimaryTextColor == "#445566"
            && state.Settings.Appearance.BackgroundColor == "#112233" && state.Settings.Appearance.BackgroundTransparency == 73.25, "Changing secondary text reset another appearance field.");
        Assert(state.Settings.Left == -400 && state.Settings.Top == 20 && state.Settings.Width == 470 && state.Settings.Height == 510
            && !state.Settings.AutoExpand && store.LoadWarning == null, "Appearance changes altered geometry, growth or data validity.");
        AssertAppearanceTask(state);
    }

    private static void InvalidAppearanceRecovery()
    {
        string[] invalidObjects = { "null", "false", "[]", "\"not-an-object\"", "42" };
        for (int i = 0; i < invalidObjects.Length; i++)
        {
            TodoStore store = Store("appearance-invalid-object-" + i);
            Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
            File.WriteAllText(store.DataFilePath, AppearanceDocument("{\"Appearance\":" + invalidObjects[i] + "}"), Encoding.UTF8);
            TodoState state = store.Load();
            AssertAppearanceTask(state);
            Assert(state.Settings.Appearance.Matches(AppearanceSettings.CreateLight()) && store.LoadWarning == null, "Malformed optional appearance invalidated the user's data.");
            Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "*.corrupt-*").Length == 0, "Appearance alone caused a damaged-data archive.");
        }
        string[] invalidNumbers = { "-0.01", "100.01", "null", "\"20\"", "false", "{}", "[]" };
        for (int i = 0; i < invalidNumbers.Length; i++)
        {
            TodoStore store = Store("appearance-invalid-field-" + i);
            Directory.CreateDirectory(Path.GetDirectoryName(store.DataFilePath));
            File.WriteAllText(store.DataFilePath, AppearanceDocument("{\"Appearance\":{\"BackgroundTransparency\":" + invalidNumbers[i]
                + ",\"BackgroundColor\":\"#abcdef\",\"PrimaryTextColor\":\"#12345G\",\"SecondaryTextColor\":\"#112233\"}}"), Encoding.UTF8);
            TodoState state = store.Load();
            AssertAppearanceTask(state);
            Assert(state.Settings.Appearance.BackgroundTransparency == 20 && state.Settings.Appearance.BackgroundColor == "#ABCDEF"
                && state.Settings.Appearance.PrimaryTextColor == "#193C50" && state.Settings.Appearance.SecondaryTextColor == "#112233", "Invalid fields did not fall back independently.");
            Assert(store.LoadWarning == null && Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath), "*.corrupt-*").Length == 0, "Invalid optional fields caused task recovery or data loss.");
            store.Save(state);
            AssertAppearanceTask(store.Load());
        }
    }

    private static void InvalidAppearanceSave()
    {
        TodoStore store = Store("appearance-save-normalization");
        double[] invalidNumbers = { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -1, 101 };
        TodoState state = State("外观异常也不能丢失");
        for (int i = 0; i < invalidNumbers.Length; i++)
        {
            state.Settings.Appearance = new AppearanceSettings {
                BackgroundTransparency = invalidNumbers[i], BackgroundColor = "#123456",
                PrimaryTextColor = "#FF000000", SecondaryTextColor = "#abcdef"
            };
            store.Save(state);
            TodoState loaded = store.Load();
            Assert(loaded.Settings.Appearance.BackgroundTransparency == 20 && loaded.Settings.Appearance.BackgroundColor == "#123456"
                && loaded.Settings.Appearance.PrimaryTextColor == "#193C50" && loaded.Settings.Appearance.SecondaryTextColor == "#ABCDEF", "Invalid appearance made a valid state unsavable or discarded good fields.");
            Assert(loaded.Items.Single().Id == "stable-id" && loaded.Items[0].Text == state.Items[0].Text && store.LoadWarning == null, "Appearance normalization changed tasks or produced damaged data.");
        }
        state.Settings.Appearance = null;
        store.Save(state);
        Assert(store.Load().Settings.Appearance.Matches(AppearanceSettings.CreateLight()), "A missing new in-memory appearance could not be saved safely.");
        string[] invalidColors = { null, "", "red", "#FFF", "#11223344", "112233", "#１２３４５６", "#11223Z" };
        foreach (string color in invalidColors)
        {
            AppearanceSettings normalized = AppearanceSettings.Normalize(new AppearanceSettings {
                BackgroundTransparency = 55, BackgroundColor = color, PrimaryTextColor = "#A1B2C3", SecondaryTextColor = "#D4E5F6"
            });
            Assert(normalized.BackgroundColor == "#F4F9FB" && normalized.BackgroundTransparency == 55
                && normalized.PrimaryTextColor == "#A1B2C3" && normalized.SecondaryTextColor == "#D4E5F6", "Invalid color affected another appearance field.");
        }
    }

    private static void AppearanceTransparencyEndpoints()
    {
        TodoStore store = Store("appearance-endpoints");
        TodoState state = State("背景透明不会改变文字颜色");
        state.Settings.Appearance = AppearanceSettings.CreateDark();
        state.Settings.Appearance.BackgroundTransparency = 100;
        store.Save(state);
        state = new TodoStore(Path.GetDirectoryName(store.DataFilePath)).Load();
        Assert(state.Settings.Appearance.BackgroundTransparency == 100 && state.Settings.Appearance.BackgroundColor == "#182B38"
            && state.Settings.Appearance.PrimaryTextColor == "#F1F5F8" && state.Settings.Appearance.SecondaryTextColor == "#BAC8D2", "Full background transparency erased independent colors.");
        state.Settings.Appearance.BackgroundTransparency = 0;
        store.Save(state); state = store.Load();
        Assert(state.Settings.Appearance.BackgroundTransparency == 0 && state.Settings.Appearance.PrimaryTextColor == "#F1F5F8"
            && state.Settings.Appearance.SecondaryTextColor == "#BAC8D2", "Opaque background was rejected or affected text color.");
        Assert(state.Items.Single().Text == "背景透明不会改变文字颜色" && store.LoadWarning == null, "Transparency endpoints damaged the saved state.");
    }

    private static void AppearanceCloneIsolation()
    {
        AppearanceSettings original = AppearanceSettings.CreateDark();
        AppearanceSettings draft = original.Clone();
        Assert(!Object.ReferenceEquals(original, draft) && draft.Matches(original), "Clone did not create an equal independent value.");
        draft.BackgroundTransparency = 100; draft.BackgroundColor = "#112233";
        draft.PrimaryTextColor = "#445566"; draft.SecondaryTextColor = "#778899";
        Assert(original.Matches(AppearanceSettings.CreateDark()), "Draft editing changed the saved appearance before confirmation.");
        draft.BackgroundColor = "#abcdef";
        AppearanceSettings normalized = AppearanceSettings.Normalize(draft);
        Assert(!Object.ReferenceEquals(normalized, draft) && normalized.BackgroundColor == "#ABCDEF" && draft.BackgroundColor == "#abcdef", "Normalization mutated its caller.");
        normalized.PrimaryTextColor = "#123456";
        Assert(draft.PrimaryTextColor == "#445566", "Normalized preview and draft share a mutable object.");
        AppearanceSettings light = AppearanceSettings.Normalize(null);
        Assert(light.Matches(AppearanceSettings.CreateLight()) && !light.Matches(null), "Null appearance behavior is inconsistent.");
        AppearanceSettings fresh = AppearanceSettings.CreateLight();
        light.BackgroundColor = "#000000";
        Assert(fresh.BackgroundColor == "#F4F9FB", "Factory or normalization returns a shared preset object.");
    }

    private static void AssertNear(double actual, double expected, string message)
    {
        Assert(!Double.IsNaN(actual) && !Double.IsInfinity(actual) && Math.Abs(actual - expected) < 0.000000001, message + " Actual: " + actual + "; expected: " + expected);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
