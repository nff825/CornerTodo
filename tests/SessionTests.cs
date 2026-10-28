using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CornerTodo;

internal static class SessionTests
{
    private static readonly DateTime Epoch = new DateTime(2026, 10, 28, 8, 9, 10, DateTimeKind.Utc).AddTicks(1234);
    private static readonly DateTime Due = Epoch.AddDays(3).AddSeconds(17).AddTicks(4321);
    private static int passed;
    private static string temporaryRoot;

    public static int Main()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "CornerTodo-SessionTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Run("Construction and snapshots isolate all mutable application state", SnapshotIsolation);
            Run("Copy preserves every public field and independently copies the object graph", CompleteCopy);
            Run("Add uses the injected clock and returns a separate item", AddWithClock);
            Run("Text editing preserves identity, UTC precision and scheduling epoch", TextOnlyEdit);
            Run("Effective deadline changes alone reset the scheduling clock", Reschedule);
            Run("Date-only deadlines retain their next-midnight semantics", DateOnlyDeadline);
            Run("Legacy null scheduling epochs survive text and completion changes", LegacyEpoch);
            Run("Complete and reopen preserve dates and are idempotent", Completion);
            Run("Editing a completed item preserves its completion history", CompletedEdit);
            Run("Delete removes only the requested item and unknown IDs never create data", DeleteAndUnknownIds);
            Run("A store cannot corrupt committed state by mutating its save argument", StoreArgumentIsolation);
            Run("Every failed operation preserves state and can be retried", FailedOperations);
            Run("Independent settings changes preserve tasks and the other settings", SettingsIsolation);
            Run("Preview operations change only the isolated session", Preview);
            Run("Real isolated persistence survives restart and a locked-save failure", RealStoreRoundTrip);
            Console.WriteLine("PASS: " + passed + " session behavior tests.");
            Console.WriteLine("No WPF, production Data or Windows startup registration is used.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            string full = Path.GetFullPath(temporaryRoot);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(full).StartsWith("CornerTodo-SessionTests-", StringComparison.Ordinal))
            {
                try { if (Directory.Exists(full)) Directory.Delete(full, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("  OK " + name); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static TaskDraft Draft(string text, DateTime due, bool hasTime)
    { return new TaskDraft { Text = text, DueDate = due, HasTime = hasTime }; }
    private static TaskDraft Draft(string text) { return Draft(text, Due, true); }
    private static TodoState Fixture()
    {
        var state = new TodoState();
        state.Settings.Left = 87.25; state.Settings.Top = 29.75;
        state.Settings.Width = 460; state.Settings.Height = 530;
        state.Settings.Collapsed = false; state.Settings.CompletedCollapsed = true; state.Settings.AutoExpand = true;
        state.Settings.Appearance = AppearanceSettings.CreateLight();
        state.Items.Add(new TodoItem { Id = "fixture-one", Text = "原始事项", CreatedAt = Epoch.AddDays(-5),
            DueDate = Due, HasTime = true, DeadlineSetAt = Epoch.AddDays(-2).AddTicks(17) });
        return state;
    }
    private static TodoItem Only(TodoSession session) { return session.Snapshot().Items.Single(); }

    private static void SnapshotIsolation()
    {
        var writer = new RecordingStore();
        TodoState supplied = Fixture(), expected = Fixture();
        var session = new TodoSession(writer, supplied, false, delegate { return Epoch; });
        supplied.Items[0].Text = "外部修改";
        supplied.Items.Add(new TodoItem());
        supplied.Settings.Appearance.PrimaryTextColor = "#ABCDEF";
        supplied.Settings.Width = 999;
        Equal(expected, session.Snapshot(), "Constructor input escaped into the session");
        TodoState first = session.Snapshot();
        first.Items[0].Id = "changed-id"; first.Items.Clear();
        first.Settings.Appearance.BackgroundTransparency = 100; first.Settings.Collapsed = true;
        Equal(expected, session.Snapshot(), "A returned snapshot changed committed state");
        Assert(writer.Arguments.Count == 0, "Construction or inspection unexpectedly saved data.");
    }

    private static void CompleteCopy()
    {
        TodoState original = Fixture(), expected = Fixture();
        SeedGraph(original, 1); SeedGraph(expected, 1);
        TodoState copy = TodoStateCopy.Copy(original);
        Equal(expected, copy, "Copy dropped a public model/settings field");
        Assert(!Object.ReferenceEquals(original, copy) && !Object.ReferenceEquals(original.Items, copy.Items) &&
            !Object.ReferenceEquals(original.Items[0], copy.Items[0]) && !Object.ReferenceEquals(original.Settings, copy.Settings) &&
            !Object.ReferenceEquals(original.Settings.Appearance, copy.Settings.Appearance), "Copy retained a mutable reference.");
        SeedGraph(original, 2);
        original.Items.Add(new TodoItem { Id = "extra-source-item" });
        Equal(expected, copy, "Mutating the source changed the copy");
        TodoState changedSource = Fixture(); SeedGraph(changedSource, 2);
        changedSource.Items.Add(new TodoItem { Id = "extra-source-item" });
        copy.Items[0].Text = "copy mutation"; copy.Settings.Appearance.BackgroundColor = "#123456";
        Equal(changedSource, original, "Mutating the copy changed the source");
    }

    // Seed every writable scalar property, including fields added in later work.
    // A new non-scalar type asks for an explicit fixture rather than silently
    // testing only its default value and missing a broken copy implementation.
    private static void SeedGraph(TodoState state, int variant)
    {
        SeedScalars(state.Items[0], variant);
        SeedScalars(state.Settings, variant);
        SeedScalars(state.Settings.Appearance, variant);
    }
    private static void SeedScalars(object value, int variant)
    {
        int index = 0;
        foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0 || property.PropertyType == typeof(AppearanceSettings)) continue;
            index++;
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object candidate;
            if (type == typeof(string)) candidate = property.Name.IndexOf("Color", StringComparison.Ordinal) >= 0
                ? (variant == 1 ? "#ABCDEF" : "#123456") : "copy-" + variant + "-" + property.Name;
            else if (type == typeof(bool)) candidate = variant == 1;
            else if (type == typeof(DateTime)) candidate = Epoch.AddDays(variant).AddTicks(index * 31);
            else if (type == typeof(double)) candidate = property.Name == "BackgroundTransparency" ? variant * 17.5 : 400.25 + index + variant;
            else if (type == typeof(int)) candidate = 17 + variant + index;
            else if (type.IsEnum) {
                Array choices = Enum.GetValues(type); candidate = choices.GetValue(choices.Length > 1 ? 1 : 0);
            }
            else throw new InvalidOperationException("Add a meaningful copy fixture for " + property.DeclaringType.Name + "." + property.Name + ".");
            property.SetValue(value, candidate, null);
        }
    }

    private static void AddWithClock()
    {
        var writer = new RecordingStore();
        var session = new TodoSession(writer, new TodoState(), false, delegate { return Epoch; });
        TaskDraft draft = Draft("新建事项");
        TodoItem added = session.Add(draft);
        Assert(!String.IsNullOrWhiteSpace(added.Id) && added.Text == "新建事项", "Add lost the submitted text or stable identity.");
        DateEqual(Epoch, added.CreatedAt, "Creation must use the injected clock");
        DateEqual(Epoch, added.DeadlineSetAt.Value, "Initial scheduling must use the same operation time");
        DateEqual(Due, added.DueDate, "Add lost UTC deadline precision");
        Assert(!added.IsCompleted && added.CompletedAt == null, "A new item was already completed.");
        string identity = added.Id;
        draft.Text = "draft mutation"; draft.DueDate = Due.AddYears(1); draft.HasTime = false;
        added.Id = "result mutation"; added.Text = "result mutation";
        Assert(Only(session).Id == identity && Only(session).Text == "新建事项" && Only(session).HasTime,
            "A caller retained an editable draft/result reference.");
        DateEqual(Due, Only(session).DueDate, "A draft changed the committed deadline");
        TodoItem second = session.Add(Draft("另一个事项"));
        Assert(second.Id != identity && session.Snapshot().Items.Count == 2, "Add reused an existing identity.");
    }

    private static void TextOnlyEdit()
    {
        var writer = new RecordingStore(); TodoState initial = Fixture();
        var session = new TodoSession(writer, initial, false, delegate { return Epoch.AddHours(2); });
        session.Edit("fixture-one", Draft("只改文字"));
        TodoItem edited = Only(session);
        Assert(edited.Text == "只改文字" && edited.Id == initial.Items[0].Id, "Text editing changed identity.");
        DateEqual(initial.Items[0].CreatedAt, edited.CreatedAt, "Text editing changed creation time");
        DateEqual(initial.Items[0].DueDate, edited.DueDate, "Text editing changed UTC ticks/Kind");
        DateEqual(initial.Items[0].DeadlineSetAt.Value, edited.DeadlineSetAt.Value, "Text editing restarted the scheduling epoch");
        Assert(!edited.IsCompleted && edited.CompletedAt == null, "Text editing changed completion state.");
    }

    private static void Reschedule()
    {
        DateTime now = Epoch; var session = new TodoSession(new RecordingStore(), Fixture(), false, delegate { return now; });
        TodoItem original = Only(session);
        DateTime equivalentLocal = original.DueDate.ToLocalTime();
        session.Edit(original.Id, Draft("相同有效截止", equivalentLocal, true));
        DateEqual(original.DeadlineSetAt.Value, Only(session).DeadlineSetAt.Value, "Equivalent UTC/local deadlines restarted scheduling");
        Assert(TodoRules.Deadline(original) == TodoRules.Deadline(Only(session)), "Equivalent local deadline changed the cutoff.");
        now = Epoch.AddHours(3).AddTicks(91);
        DateTime rescheduled = Due.AddDays(2).AddTicks(77);
        session.Edit(original.Id, Draft("重新安排", rescheduled, true));
        DateEqual(rescheduled, Only(session).DueDate, "A genuine deadline edit lost its supplied precision");
        DateEqual(now, Only(session).DeadlineSetAt.Value, "A genuine deadline edit did not start a new epoch");
        DateEqual(original.CreatedAt, Only(session).CreatedAt, "Rescheduling changed creation history");
    }

    private static void DateOnlyDeadline()
    {
        DateTime now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local);
        var session = new TodoSession(new RecordingStore(), new TodoState(), false, delegate { return now; });
        TodoItem item = session.Add(Draft("当天结束前", new DateTime(2026, 10, 28), false));
        DateTime midnight = new DateTime(2026, 10, 29);
        Assert(TodoRules.Deadline(item) == midnight && !TodoRules.IsOverdue(item, midnight.AddTicks(-1)) && TodoRules.IsOverdue(item, midnight),
            "A date-only item no longer expires at next midnight.");
        DateTime epoch = item.DeadlineSetAt.Value;
        now = now.AddHours(1);
        session.Edit(item.Id, Draft("改为同一零点时刻", midnight, true));
        DateEqual(epoch, Only(session).DeadlineSetAt.Value, "Changing representation without changing the effective deadline reset its epoch");
        now = now.AddHours(1);
        session.Edit(item.Id, Draft("改为次日全天", midnight, false));
        Assert(TodoRules.Deadline(Only(session)) == midnight.AddDays(1), "A date-only reschedule lost the calendar-day meaning.");
        DateEqual(now, Only(session).DeadlineSetAt.Value, "Changing the effective date-only deadline did not reset its epoch");
    }

    private static void LegacyEpoch()
    {
        TodoState initial = Fixture(); initial.Items[0].DeadlineSetAt = null;
        var session = new TodoSession(new RecordingStore(), initial, false, delegate { return Epoch; });
        session.Edit("fixture-one", Draft("旧记录文字更新"));
        session.SetCompleted("fixture-one", true); session.SetCompleted("fixture-one", false);
        Assert(Only(session).DeadlineSetAt == null, "A legacy null scheduling epoch was silently replaced.");
        DateEqual(initial.Items[0].CreatedAt, Only(session).CreatedAt, "Legacy editing rewrote the fallback creation epoch");
    }

    private static void Completion()
    {
        var writer = new RecordingStore(); DateTime now = Epoch;
        var session = new TodoSession(writer, Fixture(), false, delegate { return now; });
        TodoItem initial = Only(session);
        session.SetCompleted(initial.Id, true);
        Assert(Only(session).IsCompleted, "Completion did not update the item.");
        DateEqual(now, Only(session).CompletedAt.Value, "Completion ignored the injected clock");
        int writes = writer.Arguments.Count; now = now.AddDays(1);
        session.SetCompleted(initial.Id, true);
        Assert(writer.Arguments.Count == writes, "Setting the same completed state wrote a second generation.");
        DateEqual(Epoch, Only(session).CompletedAt.Value, "Idempotent completion changed its history");
        session.SetCompleted(initial.Id, false);
        Assert(!Only(session).IsCompleted && Only(session).CompletedAt == null, "Reopen did not clear the completion timestamp.");
        writes = writer.Arguments.Count; session.SetCompleted(initial.Id, false);
        Assert(writer.Arguments.Count == writes, "Idempotent reopen wrote another generation.");
        DateEqual(initial.CreatedAt, Only(session).CreatedAt, "Completion/reopen changed creation time");
        DateEqual(initial.DueDate, Only(session).DueDate, "Completion/reopen changed the deadline");
        DateEqual(initial.DeadlineSetAt.Value, Only(session).DeadlineSetAt.Value, "Completion/reopen reset scheduling");
    }

    private static void CompletedEdit()
    {
        TodoState state = Fixture(); state.Items[0].IsCompleted = true; state.Items[0].CompletedAt = Epoch.AddHours(-1);
        var session = new TodoSession(new RecordingStore(), state, false, delegate { return Epoch; });
        session.Edit("fixture-one", Draft("已完成事项改期", Due.AddDays(1), true));
        Assert(Only(session).IsCompleted, "Editing a completed item reopened it.");
        DateEqual(state.Items[0].CompletedAt.Value, Only(session).CompletedAt.Value, "Editing changed completion history");
        DateEqual(state.Items[0].CreatedAt, Only(session).CreatedAt, "Editing changed creation history");
    }

    private static void DeleteAndUnknownIds()
    {
        var writer = new RecordingStore(); var session = new TodoSession(writer, Fixture(), false, delegate { return Epoch; });
        TodoItem added = session.Add(Draft("保留的另一项"));
        session.Delete("fixture-one");
        Assert(Only(session).Id == added.Id, "Delete removed an unrelated item.");
        TodoState before = session.Snapshot(); int writes = writer.Arguments.Count;
        ExpectBusinessFailure(delegate { session.Edit("missing", Draft("不应新增")); });
        ExpectBusinessFailure(delegate { session.SetCompleted("missing", true); });
        ExpectBusinessFailure(delegate { session.Delete("missing"); });
        Equal(before, session.Snapshot(), "An unknown ID changed committed state");
        Assert(writer.Arguments.Count == writes, "An unknown ID attempted a save.");
    }

    private static void StoreArgumentIsolation()
    {
        var writer = new RecordingStore();
        writer.DuringSave = delegate(TodoState received) { received.Items.Clear(); received.Settings.Appearance.BackgroundColor = "#FF0000"; received.Version = 99; };
        var session = new TodoSession(writer, Fixture(), false, delegate { return Epoch; });
        session.Edit("fixture-one", Draft("正常提交"));
        TodoState after = session.Snapshot();
        Assert(after.Version == 1 && after.Items.Single().Text == "正常提交" && after.Settings.Appearance.BackgroundColor == AppearanceSettings.CreateLight().BackgroundColor,
            "A store mutated the session while saving its argument.");
        writer.DuringSave = null;
        session.SetCompleted("fixture-one", true);
        TodoState committed = session.Snapshot();
        writer.Arguments.Last().Items[0].Text = "later store mutation";
        writer.Arguments.Last().Settings.Appearance.PrimaryTextColor = "#000000";
        Equal(committed, session.Snapshot(), "The store retained an editable reference after Save returned");
    }

    private static void FailedOperations()
    {
        Action<TodoSession>[] operations = {
            delegate(TodoSession s) { s.Add(Draft("新增重试")); },
            delegate(TodoSession s) { s.Edit("fixture-one", Draft("编辑重试")); },
            delegate(TodoSession s) { s.SetCompleted("fixture-one", true); },
            delegate(TodoSession s) { s.Delete("fixture-one"); },
            delegate(TodoSession s) { s.SetAppearance(AppearanceSettings.CreateDark()); },
            delegate(TodoSession s) { s.SetCompletedCollapsed(false); },
            delegate(TodoSession s) { s.SetAutoExpand(false); },
            delegate(TodoSession s) { s.SetCollapsed(true); },
            delegate(TodoSession s) { s.SaveGeometry(100, 50, 500, 600); }
        };
        foreach (Action<TodoSession> operation in operations)
        {
            var writer = new RecordingStore();
            writer.DuringSave = delegate(TodoState received) {
                received.Items.Clear(); received.Settings.Appearance.BackgroundTransparency = 100; received.Settings.Width = 9999;
                throw new IOException("fixture save failure after argument mutation");
            };
            var session = new TodoSession(writer, Fixture(), false, delegate { return Epoch; });
            TodoState before = session.Snapshot();
            ExpectSaveFailure(delegate { operation(session); });
            Equal(before, session.Snapshot(), "A failed operation partially committed or leaked the store argument");
            Assert(writer.Arguments.Count == 1, "The operation did not make exactly one attempted commit.");
            writer.DuringSave = null;
            operation(session);
            Assert(writer.Arguments.Count == 2, "A failed operation could not be retried.");
            Assert(!StatesEqual(before, session.Snapshot()), "A successful retry did not commit the requested change.");
        }
    }

    private static void SettingsIsolation()
    {
        var writer = new RecordingStore(); var session = new TodoSession(writer, Fixture(), false, delegate { return Epoch; });
        TodoState expected = Fixture();
        AppearanceSettings submitted = AppearanceSettings.CreateDark();
        session.SetAppearance(submitted); expected.Settings.Appearance = AppearanceSettings.CreateDark();
        submitted.BackgroundColor = "#FF0000";
        Equal(expected, session.Snapshot(), "Appearance leaked its draft or changed other settings/tasks");
        session.SetCompletedCollapsed(false); expected.Settings.CompletedCollapsed = false;
        Equal(expected, session.Snapshot(), "Completed-region setting changed unrelated data");
        session.SetAutoExpand(false); expected.Settings.AutoExpand = false;
        Equal(expected, session.Snapshot(), "Auto-expand setting changed unrelated data");
        session.SetCollapsed(true); expected.Settings.Collapsed = true;
        Equal(expected, session.Snapshot(), "Panel collapse changed unrelated data");
        session.SaveGeometry(111.25, 55.75, 490, 610);
        expected.Settings.Left = 111.25; expected.Settings.Top = 55.75; expected.Settings.Width = 490; expected.Settings.Height = 610;
        Equal(expected, session.Snapshot(), "Geometry changed tasks, timing or unrelated settings");
    }

    private static void Preview()
    {
        var writer = new RecordingStore(); writer.DuringSave = delegate { throw new InvalidOperationException("Preview must not call its writer."); };
        TodoState original = Fixture();
        var session = new TodoSession(writer, original, true, delegate { return Epoch; });
        TodoItem added = session.Add(Draft("示例新增"));
        session.Edit(added.Id, Draft("示例编辑")); session.SetCompleted(added.Id, true); session.SetCompleted(added.Id, false);
        session.SetAppearance(AppearanceSettings.CreateDark()); session.SetCompletedCollapsed(false);
        session.SetAutoExpand(false); session.SetCollapsed(true); session.SaveGeometry(100, 50, 500, 600); session.Delete(added.Id);
        Assert(writer.Arguments.Count == 0, "Preview wrote to persistent storage.");
        Assert(session.Snapshot().Settings.Collapsed && !session.Snapshot().Settings.AutoExpand && session.Snapshot().Settings.Appearance.Matches(AppearanceSettings.CreateDark()),
            "Preview failed to update its own isolated state.");
        Equal(Fixture(), original, "Preview changed its supplied original state");
    }

    private static void RealStoreRoundTrip()
    {
        var store = new TodoStore(Path.Combine(temporaryRoot, "round-trip"));
        store.Save(Fixture());
        var session = new TodoSession(store, store.Load(), false, delegate { return Epoch; });
        session.SetAppearance(AppearanceSettings.CreateDark()); session.SetAutoExpand(false);
        TodoState expected = session.Snapshot();
        var restarted = new TodoSession(store, store.Load(), false, delegate { return Epoch; });
        Equal(expected, restarted.Snapshot(), "A restart lost committed task or appearance state");
        byte[] saved = File.ReadAllBytes(store.DataFilePath);
        using (var locked = new FileStream(store.DataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectSaveFailure(delegate { restarted.Edit("fixture-one", Draft("解锁后重试")); });
        Equal(expected, restarted.Snapshot(), "A locked real destination changed committed memory");
        Assert(saved.SequenceEqual(File.ReadAllBytes(store.DataFilePath)), "A locked real destination lost its last saved bytes.");
        restarted.Edit("fixture-one", Draft("解锁后重试"));
        Assert(store.Load().Items.Single().Text == "解锁后重试", "Retry after unlocking did not persist.");
        DateEqual(Due, store.Load().Items.Single().DueDate, "A real store retry changed UTC deadline precision");
    }

    private static void ExpectBusinessFailure(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (KeyNotFoundException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An unknown identity was silently accepted.");
    }
    private static void ExpectSaveFailure(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("The save unexpectedly succeeded.");
    }
    private static void DateEqual(DateTime expected, DateTime actual, string message)
    { Assert(expected.Ticks == actual.Ticks && expected.Kind == actual.Kind, message + "."); }
    private static bool StatesEqual(TodoState expected, TodoState actual)
    {
        try { Equal(expected, actual, "state"); return true; }
        catch (InvalidOperationException) { return false; }
    }
    private static void Equal(object expected, object actual, string path)
    {
        if (expected == null || actual == null) { Assert(expected == null && actual == null, path + ": nullable value changed."); return; }
        Type type = expected.GetType();
        Assert(actual.GetType() == type, path + ": value type changed.");
        if (type == typeof(DateTime)) { DateEqual((DateTime)expected, (DateTime)actual, path); return; }
        if (type.IsValueType || type == typeof(string)) { Assert(Object.Equals(expected, actual), path + ": scalar value changed."); return; }
        IEnumerable sequence = expected as IEnumerable;
        if (sequence != null) {
            object[] left = sequence.Cast<object>().ToArray(), right = ((IEnumerable)actual).Cast<object>().ToArray();
            Assert(left.Length == right.Length, path + ": collection length changed.");
            for (int i = 0; i < left.Length; i++) Equal(left[i], right[i], path + "[" + i + "]");
            return;
        }
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                Equal(property.GetValue(expected, null), property.GetValue(actual, null), path + "." + property.Name);
    }
    private sealed class RecordingStore : ITodoStore
    {
        public readonly List<TodoState> Arguments = new List<TodoState>();
        public Action<TodoState> DuringSave;
        public void Save(TodoState state) { Arguments.Add(state); if (DuringSave != null) DuringSave(state); }
    }
}
