using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CornerTodo;
using CornerTodo.MacCore;
using System.Web.Script.Serialization;

namespace CornerTodo.MacTests
{
    internal static class Program
    {
        private static int assertions;

        public static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--instance-probe")
                return SingleInstanceTests.Probe(args[1], args[2]);
            string oldTemp = Environment.GetEnvironmentVariable("TEMP");
            string oldTmp = Environment.GetEnvironmentVariable("TMP");
            string oldTmpDir = Environment.GetEnvironmentVariable("TMPDIR");
            string owned = null;
            try
            {
                string output = args.Length == 2 && args[0] == "--output" ? Path.GetFullPath(args[1]) :
                    Path.Combine(SourceRoot(), "output", "mac-development-20261030", "core-tests");
                if (args.Length != 0 && !(args.Length == 2 && args[0] == "--output"))
                    throw new ArgumentException("Usage: CornerTodo.MacTests [--output <test-output-directory>]");
                owned = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(owned);
                Environment.SetEnvironmentVariable("TEMP", owned);
                Environment.SetEnvironmentVariable("TMP", owned);
                Environment.SetEnvironmentVariable("TMPDIR", owned);
                string expectedTemp = Path.GetFullPath(owned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string actualTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Assert(String.Equals(expectedTemp, actualTemp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                    "The shared tests would use a temporary directory outside their assigned QA root.");

                Check(StoreTests.Main(), "shared storage");
                Check(SessionTests.Main(), "shared task session");
                Check(RecurrenceTests.Main(), "shared recurring tasks");
                Check(ReminderTests.Main(), "shared reminders");
                Check(GrowthLayoutTests.Main(), "shared growth layout");
                SerializerContract();
                Paths(owned);
                WindowsGoldenFixture(owned);
                WindowsNoDeadlineFixture(owned);
                WindowsRecurringFixture(owned);
                WindowsDisplayPreferences(owned);
                WindowsReminderPreferences(owned);
                LegacyDatesAndLiteralNotes(owned);
                int singleInstanceAssertions = SingleInstanceTests.Run(owned);
                File.WriteAllText(Path.Combine(owned, "verification.json"), JsonSerializer.Serialize(new {
                    sharedSuites = new[] { "Store", "Session", "Recurrence", "Reminder", "Growth" }, sharedSuitesPassed = true,
                    adapterAndCompatibilityAssertions = assertions, testDataRoot = owned,
                    singleInstanceAssertions = singleInstanceAssertions,
                    productionDataRead = false, nativeMacExecutionVerified = false,
                    scope = "Shared C# rules and real atomic storage on the current test host; synthetic fixtures only."
                }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                Console.WriteLine("PASS: " + assertions + " Mac serializer/path/Windows-format compatibility assertions.");
                Console.WriteLine("QA directory: " + owned);
                Console.WriteLine("No WPF, production Data, startup registration or native macOS window behavior is tested here.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error);
                if (owned != null) Console.Error.WriteLine("QA directory: " + owned);
                return 1;
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEMP", oldTemp);
                Environment.SetEnvironmentVariable("TMP", oldTmp);
                Environment.SetEnvironmentVariable("TMPDIR", oldTmpDir);
            }
        }

        private static void Check(int code, string name)
        { if (code != 0) throw new InvalidOperationException("Failed " + name + " regression suite."); }

        private static string SourceRoot()
        {
            DirectoryInfo cursor = new DirectoryInfo(AppContext.BaseDirectory);
            while (cursor != null)
            {
                if (Directory.Exists(Path.Combine(cursor.FullName, "src", "Tasks")) && Directory.Exists(Path.Combine(cursor.FullName, "macOS")))
                    return cursor.FullName;
                cursor = cursor.Parent;
            }
            throw new InvalidOperationException("Run these source tests from the project tree, or pass --output explicitly.");
        }

        private static void SerializerContract()
        {
            var codec = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 8 };
            var fields = (IDictionary<string, object>)codec.DeserializeObject("{\"integer\":1,\"wide\":2147483648,\"fraction\":1.0,\"exponent\":1e0,\"yes\":true,\"no\":false,\"nothing\":null,\"array\":[1,\"内容\"]}");
            Assert(fields["integer"] is int && (int)fields["integer"] == 1, "Integer policy differs from Framework serializer.");
            Assert(fields["wide"] is long && (long)fields["wide"] == 2147483648, "Wide integer was rounded or changed type.");
            Assert(fields["fraction"] is decimal && (decimal)fields["fraction"] == 1m, "Decimal token was coerced into an enum-compatible integer.");
            Assert(fields["exponent"] is double && (double)fields["exponent"] == 1d, "Exponent token did not retain its floating numeric type.");
            Assert(fields["yes"] is bool && (bool)fields["yes"] && fields["no"] is bool && !(bool)fields["no"], "Boolean types were coerced.");
            Assert(fields["nothing"] == null, "Null did not survive parsing.");
            Assert(fields["array"] is object[] && ((object[])fields["array"])[1].Equals("内容"), "Array or Unicode conversion failed.");
            Assert(((IDictionary<string, object>)codec.DeserializeObject("{\"a\":1,\"a\":2}"))["a"].Equals(2), "Duplicate-key policy differs from the old dictionary parser.");
            object reconstructed = codec.DeserializeObject(codec.Serialize(fields));
            Assert(reconstructed is IDictionary<string, object> && ((IDictionary<string, object>)reconstructed)["wide"].Equals(2147483648L), "Object graph serialization lost exact values.");
            Throws<ArgumentException>(delegate { codec.DeserializeObject("{ invalid"); }, "Invalid JSON bypasses the shared store's corruption boundary.");
            Throws<ArgumentException>(delegate { codec.DeserializeObject("{\"a\":1,}"); }, "Trailing commas were silently accepted.");
            Throws<ArgumentException>(delegate { codec.DeserializeObject("{/*comment*/\"a\":1}"); }, "Comments were silently accepted.");
            Throws<ArgumentException>(delegate { codec.DeserializeObject(new string(' ', 4097)); }, "Read size limit was not enforced.");
            Throws<ArgumentException>(delegate { codec.Serialize(new string('x', 4096)); }, "Write size limit was not enforced.");
            Throws<ArgumentException>(delegate { codec.DeserializeObject("[[[[[[[[[0]]]]]]]]]"); }, "Read depth limit was not enforced.");
            Throws<OverflowException>(delegate { codec.DeserializeObject("1e999"); }, "Non-finite JSON values were accepted.");
            Throws<ArgumentOutOfRangeException>(delegate { codec.RecursionLimit = 0; }, "Invalid depth limit was accepted.");
            Throws<ArgumentOutOfRangeException>(delegate { codec.MaxJsonLength = 0; }, "Invalid length limit was accepted.");
        }

        private static void Paths(string output)
        {
            string home = Path.Combine(output, "synthetic-user");
            string local = Path.Combine(output, "synthetic-local-data");
            string mac = AppPaths.DefaultDirectory(true, home, local);
            Assert(mac == Path.Combine(home, "Library", "Application Support", "CornerTodo"), "Mac default data path is not in Application Support/CornerTodo.");
            string windows = AppPaths.DefaultDirectory(false, home, local);
            Assert(windows == Path.Combine(local, "CornerTodo.MacPreview"), "Windows UI helper data is not independent from the stable app.");
            string explicitPath = Path.Combine(output, "explicit");
            Assert(AppPaths.GetDataDirectory(explicitPath) == Path.GetFullPath(explicitPath), "Explicit data path was not honored.");
            Assert(AppPaths.GetDataFilePath(explicitPath) == Path.Combine(Path.GetFullPath(explicitPath), "todos.json"), "Explicit data file path differs from TodoStore.");
            Assert(AppPaths.GetLogPath(explicitPath) == Path.Combine(Path.GetFullPath(explicitPath), "CornerTodo.log"), "Logs do not stay in the selected data directory.");
            Assert(!Directory.Exists(home) && !Directory.Exists(local) && !Directory.Exists(explicitPath), "Path calculation wrote application data.");
            Throws<ArgumentException>(delegate { AppPaths.GetDataDirectory(" "); }, "Empty override path was accepted.");
            Throws<IOException>(delegate { AppPaths.DefaultDirectory(true, "", local); }, "Missing Mac home silently fell back to an app-bundle directory.");
        }

        private static void WindowsGoldenFixture(string output)
        {
            string directory = Path.Combine(output, "windows-golden"); Directory.CreateDirectory(directory);
            string fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "windows-v1.4.0.json"), Encoding.UTF8);
            string file = Path.Combine(directory, "todos.json"); File.WriteAllText(file, fixture, new UTF8Encoding(false));
            var store = new TodoStore(directory);
            TodoState state = store.Load();
            Assert(store.LoadWarning == null && state.Items.Count == 2 && state.Version == TodoState.CurrentVersion,
                "The Windows version-one fixture did not migrate in memory without losing its tasks.");
            Assert(File.ReadAllText(file, Encoding.UTF8) == fixture, "Reading a valid Windows file rewrote its original bytes.");
            TodoItem pending = state.Items.Single(item => item.Id == "synthetic-windows-task");
            Assert(pending.HasDeadline, "An older task without HasDeadline lost its original deadline.");
            Assert(pending.Text == "向李老师汇报研究进展 📌" && pending.Notes == "准备实验数据\n检查截止时刻" && pending.Priority == TaskPriority.High,
                "Windows task fields were lost or escaped incorrectly.");
            Assert(pending.CreatedAt.Kind == DateTimeKind.Utc && pending.CreatedAt.Ticks % 10000 == 1234 &&
                pending.DueDate.Kind == DateTimeKind.Unspecified && pending.DueDate.Ticks % 10000 == 4321 && pending.DueDate.Second == 45,
                "Cross-platform read lost date kind or fractional precision.");
            Assert(pending.DeadlineSetAt.Value.Ticks % 10000 == 5678 && pending.DeadlineSetAt.Value.Kind == DateTimeKind.Utc,
                "Reading the Windows fixture changed its scheduling origin.");
            Assert(state.Settings.EdgeAutoHide && state.Settings.AutoExpand && state.Settings.SortMode == TaskSortMode.Priority && state.Settings.CompletedCollapsed,
                "Windows display preferences did not survive parsing.");
            Assert(!state.Settings.AlwaysOnTop && !state.Settings.FloatingAutoExpand && state.Settings.EffectiveAutoExpand,
                "An older Windows fixture enabled floating mode or changed the desktop expansion default.");
            Assert(state.Settings.Width == 430 && Math.Abs(state.Settings.Height - 475.33333333333326) < 0.0000001,
                "Window geometry was rounded while loading.");
            var session = new TodoSession(store, state, false);
            session.SetCompleted(pending.Id, true);
            TodoState restarted = new TodoStore(directory).Load();
            Assert(restarted.Items.Count == 2 && restarted.Items.All(item => item.IsCompleted), "Completion did not persist across a Mac-Core restart.");
            Assert(restarted.Items[0].CreatedAt == pending.CreatedAt && restarted.Items[0].DueDate == pending.DueDate &&
                restarted.Items[0].DeadlineSetAt == pending.DeadlineSetAt, "A display/state change reset the deadline interval.");
            Assert(File.Exists(file + ".bak") && File.ReadAllText(file + ".bak", Encoding.UTF8) == fixture,
                "A Mac save did not preserve the original Windows generation as a backup.");
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8)))
            {
                JsonElement item = json.RootElement.GetProperty("Items")[0];
                Assert(json.RootElement.GetProperty("Version").GetInt32() == TodoState.CurrentVersion && item.GetProperty("HasDeadline").GetBoolean() &&
                    item.GetProperty("DueDate").GetString() == "2026-10-31T17:23:45.0004321" && item.GetProperty("Priority").GetInt32() == 1,
                    "Saving an older Windows fixture lost its date precision or current-schema deadline flag.");
            }
        }

        private static void WindowsNoDeadlineFixture(string output)
        {
            string directory = Path.Combine(output, "windows-no-deadline"); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "todos.json");
            const string fixture = "{\"Version\":2,\"Items\":[{\"Id\":\"synthetic-no-deadline\",\"Text\":\"随手记下的事项\",\"Notes\":\"稍后整理\",\"Priority\":1,\"CreatedAt\":\"2026-10-01T08:00:00.0000000\",\"HasDeadline\":false,\"DueDate\":null,\"HasTime\":false,\"DeadlineSetAt\":null}]}";
            File.WriteAllText(file, fixture, new UTF8Encoding(false));
            var store = new TodoStore(directory); TodoState state = store.Load();
            Assert(store.LoadWarning == null && state.Version == TodoState.CurrentVersion && state.Items.Count == 1,
                "The version-two Windows fixture was treated as corrupt: " + store.LoadWarning);
            Assert(File.ReadAllText(file, Encoding.UTF8) == fixture, "Reading a no-deadline fixture rewrote its original bytes.");
            TodoItem item = state.Items.Single();
            Assert(!item.HasDeadline && item.DueDate == DateTime.MinValue && !item.HasTime && item.DeadlineSetAt == null,
                "A no-deadline Windows task acquired a fabricated date or scheduling interval.");
            DateTime now = new DateTime(2026, 10, 31, 12, 0, 0);
            Assert(TodoRules.Pending(state).Count() == 1 && TodoRules.TimeProgress(item, now) == 0 &&
                TodoRules.UrgentCount(state, now) == 0 && TodoRules.OverdueCount(state, now) == 0 && TodoRules.Expansion(state, now) == 0,
                "A no-deadline task was omitted from pending tasks or contributed to time-based attention.");
            Assert(TodoRules.FormatDue(item, now) == "未设截止" && TodoRules.FormatAttention(item, now) == "未设截止",
                "A no-deadline task displayed a fake date or overdue information.");
            var session = new TodoSession(store, state, false, () => now);
            session.Edit(item.Id, new TaskDraft { Text = "已修改的随手记", Notes = item.Notes, Priority = item.Priority,
                HasDeadline = false, DueDate = DateTime.MinValue });
            session.SetCompleted(item.Id, true);
            var reloaded = new TodoStore(directory).Load();
            Assert(reloaded.Items.Single().IsCompleted && !reloaded.Items.Single().HasDeadline,
                "Completing and restarting changed the no-deadline state.");
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8)))
                Assert(json.RootElement.GetProperty("Version").GetInt32() == TodoState.CurrentVersion &&
                    json.RootElement.GetProperty("Items")[0].GetProperty("DueDate").ValueKind == JsonValueKind.Null &&
                    !json.RootElement.GetProperty("Items")[0].GetProperty("HasDeadline").GetBoolean(),
                    "Mac storage did not preserve the shared nullable-date wire format.");
            session.SetCompleted(item.Id, false);
            session.Edit(item.Id, new TaskDraft { Text = "已修改的随手记", Notes = item.Notes, Priority = item.Priority,
                HasDeadline = true, DueDate = now.AddDays(2), HasTime = true });
            var scheduled = new TodoStore(directory).Load().Items.Single();
            Assert(scheduled.HasDeadline && scheduled.DueDate == now.AddDays(2) && scheduled.DeadlineSetAt == now,
                "Adding a date reused the original creation time as its progress origin.");
        }

        private static void WindowsRecurringFixture(string output)
        {
            string directory = Path.Combine(output, "windows-recurring"); Directory.CreateDirectory(directory);
            DateTime start = new DateTime(2026, 11, 2);
            long timeTicks = new TimeSpan(17, 23, 45).Ticks + 1234;
            var rule = new RecurringTaskRule {
                Id = "synthetic-recurring-rule", Text = "整理研究记录", Notes = "仅本次修改不应改变模板\n/Date(0)/",
                Priority = TaskPriority.High, HasDeadline = true, HasTime = true,
                DeadlineOffsetDays = 2, DeadlineTimeTicks = timeTicks,
                Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Weekly,
                    WeekDays = 2 | 32, DayOfMonth = 1, UseLastDay = false, StartDate = start },
                IsPaused = true, IsStopped = false, ProcessedThrough = start
            };
            var item = new TodoItem {
                Id = "synthetic-recurring-occurrence", Text = rule.Text, Notes = rule.Notes, Priority = rule.Priority,
                CreatedAt = start.AddHours(8), HasDeadline = true, HasTime = true,
                DueDate = start.AddDays(2).AddTicks(timeTicks), DeadlineSetAt = start.AddHours(8),
                RecurringRuleId = rule.Id, OccurrenceDate = start, RepeatLabel = "每周一、周五"
            };
            var state = new TodoState(); state.RecurringRules.Add(rule); state.Items.Add(item);
            var store = new TodoStore(directory); store.Save(state);
            string file = store.DataFilePath;
            string original = File.ReadAllText(file, Encoding.UTF8);
            var reloaded = new TodoStore(directory).Load();
            Assert(File.ReadAllText(file, Encoding.UTF8) == original && reloaded.Version == TodoState.CurrentVersion,
                "Reading current-schema recurrence data rewrote the file or changed its version.");
            Assert(reloaded.RecurringRules.Count == 1 && reloaded.Items.Count == 1,
                "Mac storage lost a recurring rule or one of its occurrence records.");
            RecurringTaskRule loadedRule = reloaded.RecurringRules.Single();
            TodoItem loadedItem = reloaded.Items.Single();
            Assert(loadedRule.Id == rule.Id && loadedRule.Text == rule.Text && loadedRule.Notes == rule.Notes &&
                loadedRule.Priority == rule.Priority && loadedRule.IsPaused && !loadedRule.IsStopped,
                "Rule text, Unicode notes, priority or paused state did not survive the Mac serializer.");
            Assert(loadedRule.Recurrence.Frequency == RecurrenceFrequency.Weekly && loadedRule.Recurrence.WeekDays == (2 | 32) &&
                loadedRule.Recurrence.StartDate == start && loadedRule.ProcessedThrough == start &&
                loadedRule.DeadlineOffsetDays == 2 && loadedRule.DeadlineTimeTicks == timeTicks,
                "Recurrence calendar fields, processing cursor or wide time ticks were rounded or dropped.");
            Assert(loadedItem.RecurringRuleId == rule.Id && loadedItem.OccurrenceDate == start &&
                loadedItem.RepeatLabel == "每周一、周五" && loadedItem.DueDate.Ticks == item.DueDate.Ticks,
                "Occurrence identity, frozen label or deadline precision did not survive round-trip.");
            using (JsonDocument json = JsonDocument.Parse(original)) {
                JsonElement savedRule = json.RootElement.GetProperty("RecurringRules")[0];
                Assert(json.RootElement.GetProperty("Version").GetInt32() == TodoState.CurrentVersion &&
                    savedRule.GetProperty("DeadlineTimeTicks").GetInt64() == timeTicks &&
                    json.RootElement.GetProperty("Items")[0].GetProperty("RecurringRuleId").GetString() == rule.Id,
                    "The shared recurrence wire format omitted rule identity or changed 64-bit scheduling fields.");
            }

            string frozenRule = JsonSerializer.Serialize(loadedRule);
            var session = new TodoSession(store, reloaded, false, () => start.AddHours(12));
            session.Edit(loadedItem.Id, new TaskDraft { Text = "仅修改这一次的记录", Notes = "本次备注",
                Priority = TaskPriority.Low, HasDeadline = false });
            session.SetCompleted(loadedItem.Id, true);
            session.SaveGeometry(12, 24, 430, 475);
            var edited = new TodoStore(directory).Load();
            Assert(JsonSerializer.Serialize(edited.RecurringRules.Single()) == frozenRule,
                "Ordinary Mac task edits, completion or geometry save changed the future recurrence rule.");
            TodoItem editedItem = edited.Items.Single();
            Assert(editedItem.Text == "仅修改这一次的记录" && editedItem.IsCompleted && !editedItem.HasDeadline &&
                editedItem.RecurringRuleId == rule.Id && editedItem.OccurrenceDate == start && editedItem.RepeatLabel == item.RepeatLabel,
                "Editing only an occurrence detached it, erased its history identity or changed its frozen repeat label.");
            TodoState detached = session.Snapshot();
            detached.RecurringRules[0].Text = "不可泄漏的副本修改";
            detached.RecurringRules[0].Recurrence.WeekDays = 4;
            Assert(session.Snapshot().RecurringRules.Single().Text == rule.Text &&
                session.Snapshot().RecurringRules.Single().Recurrence.WeekDays == (2 | 32),
                "A Mac UI snapshot could mutate its session-owned recurrence template.");

            string supported = File.ReadAllText(file, Encoding.UTF8);
            string future = supported.Replace("\"Version\":" + TodoState.CurrentVersion, "\"Version\":999");
            Assert(future != supported, "Future-version fixture did not update the root schema marker.");
            string backup = File.ReadAllText(file + ".bak", Encoding.UTF8);
            File.WriteAllText(file, future, new UTF8Encoding(false));
            Throws<NotSupportedException>(() => new TodoStore(directory).Load(),
                "An unknown recurrence schema was treated as damaged data or replaced with an older backup.");
            Assert(File.ReadAllText(file, Encoding.UTF8) == future && File.ReadAllText(file + ".bak", Encoding.UTF8) == backup &&
                !Directory.GetFiles(directory, "*.corrupt-*").Any(),
                "Rejecting a future schema changed the original file, its backup or its classification.");
        }

        private static void WindowsReminderPreferences(string output)
        {
            string directory = Path.Combine(output, "windows-reminder-preferences");
            DateTime now = new DateTime(2026, 11, 4, 12, 0, 0);
            var store = new TodoStore(directory);
            var session = new TodoSession(store, new TodoState(), false, () => now);
            TodoItem item = session.Add(new TaskDraft { Text = "保留Windows提醒配置", Notes = "仅验证共享数据", HasDeadline = true,
                HasTime = true, DueDate = new DateTime(2026, 11, 20, 17, 0, 0), ReminderEnabled = true,
                ReminderMinutesBefore = 1440 + 120 + 30 });
            TodoState loaded = store.Load();
            TodoItem saved = loaded.Items.Single();
            Assert(loaded.Version == TodoState.CurrentVersion && saved.ReminderEnabled && saved.ReminderMinutesBefore == 1590 &&
                saved.ReminderPlanId == item.ReminderPlanId && saved.ReminderSetAt == item.ReminderSetAt && saved.ReminderHandledAt == null,
                "Mac serialization lost Windows reminder configuration or its pending plan identity.");
            Assert(ReminderRules.GetReminderTime(saved) == new DateTime(2026, 11, 19, 14, 30, 0),
                "The shared reminder duration changed its intended local reminder time.");
            session.SaveGeometry(12, 24, 430, 475);
            Assert(store.Load().Items.Single().ReminderPlanId == item.ReminderPlanId,
                "A Mac geometry write reset a Windows reminder plan.");
            now = new DateTime(2026, 11, 19, 14, 30, 0);
            Assert(session.ClaimDueReminders().Count == 1, "The shared synthetic reminder did not become claimable.");
            TodoItem handled = store.Load().Items.Single();
            Assert(handled.ReminderHandledAt.HasValue && handled.ReminderPlanId == item.ReminderPlanId,
                "Mac serialization dropped a reminder receipt.");
            var restarted = new TodoSession(store, store.Load(), false, () => now.AddMinutes(1));
            Assert(restarted.ClaimDueReminders().Count == 0,
                "A shared reminder receipt did not prevent restart duplication.");
        }

        private static void WindowsDisplayPreferences(string output)
        {
            string directory = Path.Combine(output, "windows-display-preferences");
            DateTime now = new DateTime(2026, 11, 4, 14, 0, 0);
            var state = new TodoState();
            state.Settings.AlwaysOnTop = true;
            state.Settings.AutoExpand = true;
            state.Settings.FloatingAutoExpand = false;
            state.Items.Add(new TodoItem { Id = "synthetic-display-item", Text = "跨平台显示设置兼容",
                CreatedAt = now.AddDays(-2), DueDate = now.AddDays(-1), HasTime = true });
            var store = new TodoStore(directory); store.Save(state);
            var loaded = store.Load();
            Assert(loaded.Version == TodoState.CurrentVersion && loaded.Settings.AlwaysOnTop && loaded.Settings.AutoExpand &&
                !loaded.Settings.FloatingAutoExpand && !loaded.Settings.EffectiveAutoExpand,
                "The Mac store did not preserve independent Windows display preferences in schema three.");
            Assert(TodoRules.Expansion(loaded, now) == 1,
                "The shared urgency calculation was incorrectly gated by the Windows floating preference.");
            var session = new TodoSession(store, loaded, false, () => now);
            session.SaveGeometry(12, 24, 430, 475);
            var geometry = store.Load();
            Assert(geometry.Settings.AlwaysOnTop && geometry.Settings.AutoExpand && !geometry.Settings.FloatingAutoExpand,
                "A Mac geometry save changed the preserved Windows display preferences.");
            session.SetDesktopAutoExpand(false);
            var off = store.Load();
            Assert(off.Settings.AlwaysOnTop && !off.Settings.AutoExpand && !off.Settings.FloatingAutoExpand,
                "The Mac desktop expansion command changed a Windows floating preference.");
            session.SetDesktopAutoExpand(true);
            var on = store.Load();
            Assert(on.Settings.AlwaysOnTop && on.Settings.AutoExpand && !on.Settings.FloatingAutoExpand &&
                !on.Settings.EffectiveAutoExpand && on.Items.Single().Text == state.Items.Single().Text,
                "The Mac desktop expansion toggle did not persist independently or modified a task.");
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(store.DataFilePath, Encoding.UTF8))) {
                JsonElement settings = json.RootElement.GetProperty("Settings");
                Assert(json.RootElement.GetProperty("Version").GetInt32() == TodoState.CurrentVersion &&
                    settings.GetProperty("AlwaysOnTop").GetBoolean() &&
                    !settings.GetProperty("FloatingAutoExpand").GetBoolean() &&
                    settings.GetProperty("AutoExpand").GetBoolean() &&
                    !settings.TryGetProperty("EffectiveAutoExpand", out _),
                    "The Mac serializer omitted display settings or persisted a derived effective setting.");
            }
        }

        private static void LegacyDatesAndLiteralNotes(string output)
        {
            var codec = new JavaScriptSerializer();
            string[] legacy = { "\"\\/Date(0)\\/\"", "\"\\/Date(-1)\\/\"", "\"\\/Date(0+0800)\\/\"", "\"\\/Date(0-0400)\\/\"", "\"\\/Date(1x)\\/\"" };
            long[] milliseconds = { 0, -1, 0, 0, 1 };
            for (int index = 0; index < legacy.Length; index++)
            {
                object read = codec.DeserializeObject(legacy[index]);
                Assert(read is DateTime && ((DateTime)read).Kind == DateTimeKind.Utc &&
                    ((DateTime)read).Ticks == new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks + milliseconds[index] * TimeSpan.TicksPerMillisecond,
                    "Old Framework date literal differs from the verified serializer behavior.");
            }
            Assert(codec.DeserializeObject("\"/Date(0)/\"").Equals("/Date(0)/"), "Ordinary task text was coerced into a DateTime.");
            string directory = Path.Combine(output, "legacy-dates"); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "todos.json");
            File.WriteAllText(file, "{\"Version\":1,\"Items\":[{\"Id\":\"legacy\",\"Text\":\"旧日期格式\",\"Notes\":\"/Date(0)/\",\"CreatedAt\":\"\\/Date(0)\\/\",\"DueDate\":\"\\/Date(86400000+0800)\\/\",\"HasTime\":true}]}", new UTF8Encoding(false));
            var store = new TodoStore(directory); TodoState readState = store.Load();
            Assert(store.LoadWarning == null && readState.Items.Count == 1 && readState.Items[0].Notes == "/Date(0)/", "Valid legacy data was rejected or literal notes were lost.");
            Assert(readState.Items[0].DueDate == new DateTime(1970, 1, 2, 0, 0, 0, DateTimeKind.Utc), "Legacy milliseconds or ignored-offset interpretation changed.");
            Assert(!readState.Settings.EdgeAutoHide && readState.Items[0].Priority == TaskPriority.Normal, "Legacy optional fields did not use the same defaults.");
            store.Save(readState); Assert(new TodoStore(directory).Load().Items[0].Notes == "/Date(0)/", "Literal date-shaped notes did not survive a full save and reload.");
        }

        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { assertions++; return; }
            throw new InvalidOperationException(message);
        }
        private static void Assert(bool condition, string message)
        { assertions++; if (!condition) throw new InvalidOperationException(message); }
    }
}

