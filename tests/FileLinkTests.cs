using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using CornerTodo;

internal static class FileLinkTests
{
    private static readonly DateTime Epoch = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Local);
    private static string root;
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        root = Path.Combine(Path.GetTempPath(), "CornerTodo-FileLinkTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            PathContract();
            SessionIsolationAndFailure();
            AddEditAndPersistence();
            RecurrenceInheritance();
            LegacyMigrationAndRecovery();
            OriginalFilesAreUntouched();
            Console.WriteLine("PASS: " + assertions + " original-file association assertions.");
        }
        finally {
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string owned = Path.GetFullPath(root);
            if (owned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(owned).StartsWith("CornerTodo-FileLinkTests-", StringComparison.Ordinal))
                try { Directory.Delete(owned, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void PathContract()
    {
        Assert(new TodoItem().FileLinks.Count == 0 && new TaskDraft().FileLinks.Count == 0 &&
            new RecurringTaskRule().FileLinks.Count == 0, "New task, draft or template does not default to no associations.");
        var input = new List<string> { null, "", " \t", @"C:\Work\报告.docx", @"c:\work\报告.DOCX",
            @"\\server\share\资料.pdf", @"\\SERVER\SHARE\资料.PDF", "/Users/me/Report.pdf", "/Users/me/report.pdf",
            @"D:/Research/原图.png", @"\\?\D:\Long\文件.txt", @"\\?\UNC\server\share\文件.txt" };
        List<string> normalized = FileLinkRules.Normalize(input);
        Assert(normalized.SequenceEqual(new[] { input[3], input[5], input[7], input[8], input[9], input[10], input[11] }),
            "Windows case-insensitive/POSIX case-sensitive deduplication or original spelling was lost.");
        Assert(input.Count == 12 && input[0] == null, "Normalizing changed the caller's input.");
        normalized.Clear();
        Assert(input.Count == 12, "Normalized result shares its list with the caller.");
        Assert(FileLinkRules.Normalize(null).Count == 0 && FileLinkRules.Copy(null).Count == 0, "Null collections did not use independent empty lists.");
        var original = new List<string> { @"C:\missing\one.txt", "/home/me/source " };
        List<string> copy = FileLinkRules.Copy(original); copy.Add(@"C:\other.txt");
        Assert(original.Count == 2 && FileLinkRules.Normalize(original)[1] == original[1], "Copy or normalization changed original path characters.");
        foreach (string invalid in new[] { "relative.txt", @"folder\relative.txt", "C:relative.txt", @"\rooted-only.txt",
            "https://example.com/file", "file:///home/me/file", @"\\server", @"\\server\", "~/file", "/tmp/a\0b".Replace("\\0", "\0") })
            Throws<ArgumentException>(delegate { FileLinkRules.Normalize(new[] { invalid }); });
        var enumerated = FileLinkRules.Normalize(EnumerateOriginal());
        Assert(enumerated.Count == 1 && enumerated[0] == @"Z:\offline\missing.dat", "A deferred association enumerable was not safely materialized.");
    }

    private static IEnumerable<string> EnumerateOriginal() { yield return @"Z:\offline\missing.dat"; }

    private static void SessionIsolationAndFailure()
    {
        var writer = new Writer();
        TodoState supplied = Fixture();
        var session = new TodoSession(writer, supplied, false, delegate { return Epoch.AddHours(1); });
        supplied.Items[0].FileLinks.Clear();
        Assert(Only(session).FileLinks.Count == 1, "Constructor input changed the committed association list.");
        TodoState exposed = session.Snapshot(); exposed.Items[0].FileLinks.Clear();
        Assert(Only(session).FileLinks.Count == 1, "A snapshot shares the committed association list.");
        string unchanged = WithoutLinks(session.Snapshot());
        var links = new List<string> { @"C:\Work\更新稿.docx", @"c:\work\更新稿.DOCX", "/home/test/原文件.pdf" };
        writer.DuringSave = delegate(TodoState state) { state.Items[0].FileLinks.Clear(); state.Items[0].Text = "writer mutation"; };
        session.SetFileLinks("one", links);
        Assert(Only(session).FileLinks.SequenceEqual(new[] { links[0], links[2] }) && WithoutLinks(session.Snapshot()) == unchanged,
            "Updating associations changed a reminder, scheduling origin, content, settings or other task.");
        links.Clear();
        Assert(Only(session).FileLinks.Count == 2, "An association input list escaped into committed state.");
        int saves = writer.Saves;
        session.SetFileLinks("one", Only(session).FileLinks);
        Assert(writer.Saves == saves, "Saving an identical association list wrote a new generation.");
        string before = Fingerprint(session.Snapshot());
        writer.Fail = true;
        Throws<IOException>(delegate { session.SetFileLinks("one", new[] { @"C:\new.txt" }); });
        Assert(Fingerprint(session.Snapshot()) == before, "A failed association save partially committed state.");
        writer.Fail = false;
        session.SetFileLinks("one", new[] { @"C:\new.txt" });
        Assert(Only(session).FileLinks.SequenceEqual(new[] { @"C:\new.txt" }) && WithoutLinks(session.Snapshot()) == unchanged,
            "Retry failed or changed unrelated state.");
        saves = writer.Saves;
        Throws<ArgumentException>(delegate { session.SetFileLinks("one", new[] { "relative.txt" }); });
        Throws<KeyNotFoundException>(delegate { session.SetFileLinks("missing", new[] { @"C:\valid.txt" }); });
        Assert(writer.Saves == saves && Only(session).FileLinks.Count == 1, "Invalid associations or IDs reached the writer.");
        session.SetFileLinks("one", null);
        Assert(Only(session).FileLinks.Count == 0 && WithoutLinks(session.Snapshot()) == unchanged,
            "Removing all associations changed other state.");
        var previewWriter = new Writer();
        var preview = new TodoSession(previewWriter, Fixture(), true, delegate { return Epoch; });
        preview.SetFileLinks("one", new[] { @"C:\preview.txt" });
        Assert(previewWriter.Saves == 0 && Only(preview).FileLinks.Count == 1, "Preview wrote application data.");
    }

    private static void AddEditAndPersistence()
    {
        var store = new TodoStore(Path.Combine(root, "restart"));
        DateTime clock = Epoch;
        var session = new TodoSession(store, new TodoState(), false, delegate { return clock; });
        TaskDraft draft = Draft(); draft.FileLinks = new List<string> { @"C:\未连接的磁盘\报告.docx", "/Users/mac/真实文件.pdf" };
        TodoItem added = session.Add(draft);
        List<string> original = FileLinkRules.Copy(added.FileLinks);
        draft.FileLinks.Clear(); added.FileLinks.Clear();
        Assert(Only(session).FileLinks.SequenceEqual(original), "Add retained caller-owned associations.");
        clock = clock.AddHours(1); TodoItem old = Only(session);
        TaskDraft edit = Draft(); edit.Text = "仅更新事项文字"; edit.FileLinks = FileLinkRules.Copy(original);
        session.Edit(old.Id, edit); edit.FileLinks.Clear();
        TodoItem edited = Only(session);
        Assert(edited.FileLinks.SequenceEqual(original) && edited.DeadlineSetAt == old.DeadlineSetAt &&
            edited.ReminderPlanId == old.ReminderPlanId && edited.ReminderSetAt == old.ReminderSetAt,
            "Editing associations or text reset the scheduling origin or reminder plan.");
        TodoState restarted = new TodoStore(Path.GetDirectoryName(store.DataFilePath)).Load();
        Assert(restarted.Version == 6 && restarted.Items.Single().FileLinks.SequenceEqual(original), "File references did not survive a real save/reopen.");
        var codec = new JavaScriptSerializer();
        var json = (IDictionary<string, object>)codec.DeserializeObject(File.ReadAllText(store.DataFilePath));
        Assert((int)json["Version"] == 6, "Saved references did not upgrade to schema six.");
        session.SetCompleted(old.Id, true); session.SetCompleted(old.Id, false);
        Assert(Only(session).FileLinks.SequenceEqual(original), "Completion or restoration dropped associations.");
        using (var locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            string snapshot = Fingerprint(session.Snapshot());
            Throws<IOException>(delegate { session.SetFileLinks(old.Id, new[] { @"C:\blocked-save.txt" }); });
            Assert(Fingerprint(session.Snapshot()) == snapshot, "A real locked-file save failure committed links.");
        }
        session.SetFileLinks(old.Id, new[] { @"C:\after-retry.txt" });
        Assert(new TodoStore(Path.GetDirectoryName(store.DataFilePath)).Load().Items.Single().FileLinks[0] == @"C:\after-retry.txt",
            "A real save retry did not persist the replacement list.");
    }

    private static void RecurrenceInheritance()
    {
        var writer = new Writer(); DateTime clock = Epoch;
        var session = new TodoSession(writer, new TodoState(), false, delegate { return clock; });
        TaskDraft template = Draft();
        template.Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Daily, StartDate = Epoch.Date };
        template.FileLinks = new List<string> { @"C:\共同文档\每日报告.xlsx", "/Users/shared/report.csv" };
        TodoItem first = session.Add(template);
        string ruleId = session.Snapshot().RecurringRules.Single().Id;
        Assert(first.FileLinks.SequenceEqual(template.FileLinks) && session.GetRecurringRule(ruleId).FileLinks.SequenceEqual(template.FileLinks),
            "A repeating rule or its first occurrence lost original-file references.");
        RecurringTaskRule exposed = session.GetRecurringRule(ruleId); exposed.FileLinks.Clear(); first.FileLinks.Clear();
        Assert(session.GetRecurringRule(ruleId).FileLinks.Count == 2 && session.Snapshot().Items[0].FileLinks.Count == 2,
            "A rule or generated result leaked mutable lists.");
        session.SetFileLinks(first.Id, new[] { @"C:\仅本期.docx" });
        Assert(session.GetRecurringRule(ruleId).FileLinks.SequenceEqual(template.FileLinks), "Editing an occurrence changed its future rule.");
        clock = clock.AddDays(1);
        Assert(session.GenerateCurrentRecurrences() == 1, "The next independent recurrence was not generated.");
        TodoItem second = session.Snapshot().Items.Last();
        Assert(second.FileLinks.SequenceEqual(template.FileLinks) && session.Snapshot().Items[0].FileLinks[0] == @"C:\仅本期.docx",
            "A later period did not inherit its rule or rewrote history.");
        string history = FingerprintItems(session.Snapshot());
        DateTime? reminderOrigin = session.GetRecurringRule(ruleId).ReminderSetAt;
        template.FileLinks = new List<string> { @"D:\后续规则.xlsx" };
        session.EditRecurringRule(ruleId, template);
        Assert(FingerprintItems(session.Snapshot()) == history && session.GetRecurringRule(ruleId).ReminderSetAt == reminderOrigin,
            "Future-only association editing changed history or a rule reminder origin.");
        writer.Fail = true;
        TaskDraft rejected = Draft(); rejected.Recurrence = RecurringTasks.Copy(template.Recurrence);
        rejected.FileLinks.Add(@"D:\失败的规则.xlsx");
        Throws<IOException>(delegate { session.EditRecurringRule(ruleId, rejected); });
        Assert(session.GetRecurringRule(ruleId).FileLinks.SequenceEqual(template.FileLinks) && FingerprintItems(session.Snapshot()) == history,
            "A failed future edit leaked associations into the rule or history.");
        writer.Fail = false; clock = clock.AddDays(1);
        Assert(session.GenerateCurrentRecurrences() == 1 && session.Snapshot().Items.Last().FileLinks.SequenceEqual(template.FileLinks),
            "The next period ignored the updated original-file references.");
        var store = new TodoStore(Path.Combine(root, "recurrence-restart")); store.Save(session.Snapshot());
        TodoState reloaded = store.Load();
        Assert(reloaded.RecurringRules.Single().FileLinks.SequenceEqual(template.FileLinks) &&
            reloaded.Items[0].FileLinks[0] == @"C:\仅本期.docx", "Saving/reopening lost template versus occurrence association history.");
        TodoState copied = TodoStateCopy.Copy(reloaded); copied.RecurringRules[0].FileLinks.Clear(); copied.Items[0].FileLinks.Clear();
        Assert(reloaded.RecurringRules[0].FileLinks.Count == 1 && reloaded.Items[0].FileLinks.Count == 1, "A deep copy retained nested link lists.");
    }

    private static void LegacyMigrationAndRecovery()
    {
        for (int version = 1; version <= 5; version++) {
            string directory = Path.Combine(root, "legacy-" + version); Directory.CreateDirectory(directory);
            var store = new TodoStore(directory);
            string file = store.DataFilePath;
            string legacy = "{\"Version\":" + version + ",\"Items\":[{\"Id\":\"legacy\",\"Text\":\"旧事项\",\"CreatedAt\":\"2026-11-21T10:00:00.0000000\",\"DueDate\":\"2026-11-23T18:00:00.0000000\",\"HasTime\":true}]}";
            File.WriteAllText(file, legacy, new UTF8Encoding(false)); byte[] bytes = File.ReadAllBytes(file);
            TodoState state = store.Load();
            Assert(state.Items.Count == 1, "Legacy schema " + version + " fixture did not load: " + store.LoadWarning);
            Assert(state.Version == 6 && state.Items.Single().FileLinks.Count == 0 && store.LoadWarning == null,
                "An old schema did not migrate to an empty association list.");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(file)) && !File.Exists(file + ".bak"), "Reading an old schema rewrote its original generation.");
            store.Save(state);
            Assert(bytes.SequenceEqual(File.ReadAllBytes(file + ".bak")) && store.Load().Items[0].FileLinks.Count == 0,
                "The first schema-six save did not preserve its legacy backup.");
        }
        var durable = new TodoStore(Path.Combine(root, "invalid-links")); durable.Save(Fixture());
        string valid = File.ReadAllText(durable.DataFilePath);
        var codec = new JavaScriptSerializer();
        foreach (object invalid in new object[] { "not-a-list", new object[] { 17 }, new object[] { "relative.txt" }, new Dictionary<string, object>() }) {
            var parsed = (IDictionary<string, object>)codec.DeserializeObject(valid);
            ((IDictionary<string, object>)((object[])parsed["Items"])[0])["FileLinks"] = invalid;
            string malformed = codec.Serialize(parsed);
            File.WriteAllText(durable.DataFilePath + ".bak", valid, new UTF8Encoding(false));
            File.WriteAllText(durable.DataFilePath, malformed, new UTF8Encoding(false));
            TodoState recovered = durable.Load();
            Assert(durable.LoadWarning != null && recovered.Items[0].FileLinks[0] == @"C:\Original\报告.docx",
                "Malformed associations bypassed recovery or discarded a valid backup.");
            Assert(Directory.GetFiles(Path.GetDirectoryName(durable.DataFilePath), "todos.json.corrupt-*").Any(path => File.ReadAllText(path) == malformed),
                "A malformed original was not preserved byte-for-byte.");
        }
        TodoState invalidMemory = durable.Load(); invalidMemory.Items[0].FileLinks.Add("relative.txt");
        byte[] saved = File.ReadAllBytes(durable.DataFilePath);
        Throws<ArgumentException>(delegate { durable.Save(invalidMemory); });
        Assert(saved.SequenceEqual(File.ReadAllBytes(durable.DataFilePath)), "An invalid in-memory link replaced the saved generation.");
        string future = valid.Replace("\"Version\":6", "\"Version\":7");
        File.WriteAllText(durable.DataFilePath, future, new UTF8Encoding(false));
        Throws<NotSupportedException>(delegate { durable.Load(); });
        Assert(File.ReadAllText(durable.DataFilePath) == future, "An unsupported future schema was replaced from an old backup.");
    }

    private static void OriginalFilesAreUntouched()
    {
        string original = Path.Combine(root, "original-document.txt"), missing = Path.Combine(root, "missing-original.txt");
        byte[] contents = Encoding.UTF8.GetBytes("只保留原件\nThis file must never be copied, moved or deleted.");
        File.WriteAllBytes(original, contents);
        var store = new TodoStore(Path.Combine(root, "no-file-io"));
        var session = new TodoSession(store, new TodoState(), false, delegate { return Epoch; });
        TaskDraft draft = Draft(); draft.FileLinks = new List<string> { original, missing };
        TodoItem item = session.Add(draft);
        Assert(store.Load().Items[0].FileLinks.SequenceEqual(draft.FileLinks) && !File.Exists(missing), "An absent original was rejected or created.");
        session.SetFileLinks(item.Id, null); session.Delete(item.Id);
        Assert(File.ReadAllBytes(original).SequenceEqual(contents) && !File.Exists(missing), "Unlinking or deleting a task changed an original file.");
        Assert(Directory.GetFiles(Path.GetDirectoryName(store.DataFilePath)).All(path => Path.GetFileName(path).StartsWith("todos.json", StringComparison.Ordinal)),
            "An original file was copied into the application's data directory.");
    }

    private static TaskDraft Draft()
    {
        return new TaskDraft { Text = "关联原文件", Notes = "保留文字和设置", IsImportant = true,
            DueDate = Epoch.Date.AddHours(18), HasTime = true, ReminderEnabled = true, ReminderMinutesBefore = 15 };
    }

    private static TodoState Fixture()
    {
        var state = new TodoState();
        state.Items.Add(new TodoItem { Id = "one", Text = "关联原文件", Notes = "原备注", Priority = TaskPriority.Low,
            IsImportant = true, CreatedAt = Epoch.AddDays(-1), DueDate = Epoch.Date.AddHours(18), HasTime = true,
            DeadlineSetAt = Epoch.AddDays(-1), ReminderEnabled = true, ReminderMinutesBefore = 15,
            ReminderPlanId = "keep-this-plan", ReminderSetAt = Epoch.AddDays(-1), FileLinks = new List<string> { @"C:\Original\报告.docx" } });
        return state;
    }

    private static TodoItem Only(TodoSession session) { return session.Snapshot().Items.Single(); }
    private static string Fingerprint(TodoState state) { return new JavaScriptSerializer().Serialize(state); }
    private static string FingerprintItems(TodoState state) { return new JavaScriptSerializer().Serialize(state.Items); }
    private static string WithoutLinks(TodoState state)
    {
        TodoState copy = TodoStateCopy.Copy(state);
        foreach (TodoItem item in copy.Items) item.FileLinks.Clear();
        return Fingerprint(copy);
    }
    private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class Writer : ITodoStore
    {
        internal bool Fail;
        internal int Saves;
        internal Action<TodoState> DuringSave;
        public void Save(TodoState state)
        {
            Saves++; if (DuringSave != null) DuringSave(state);
            if (Fail) throw new IOException("Synthetic association save failure.");
        }
    }
}
