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
                Check(GrowthLayoutTests.Main(), "shared growth layout");
                SerializerContract();
                Paths(owned);
                WindowsGoldenFixture(owned);
                LegacyDatesAndLiteralNotes(owned);
                int singleInstanceAssertions = SingleInstanceTests.Run(owned);
                File.WriteAllText(Path.Combine(owned, "verification.json"), JsonSerializer.Serialize(new {
                    sharedSuites = new[] { "Store", "Session", "Growth" }, sharedSuitesPassed = true,
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
            Assert(store.LoadWarning == null && state.Items.Count == 2, "The Windows-shaped fixture was treated as corrupt.");
            Assert(File.ReadAllText(file, Encoding.UTF8) == fixture, "Reading a valid Windows file rewrote its original bytes.");
            TodoItem pending = state.Items.Single(item => item.Id == "synthetic-windows-task");
            Assert(pending.Text == "向李老师汇报研究进展 📌" && pending.Notes == "准备实验数据\n检查截止时刻" && pending.Priority == TaskPriority.High,
                "Windows task fields were lost or escaped incorrectly.");
            Assert(pending.CreatedAt.Kind == DateTimeKind.Utc && pending.CreatedAt.Ticks % 10000 == 1234 &&
                pending.DueDate.Kind == DateTimeKind.Unspecified && pending.DueDate.Ticks % 10000 == 4321 && pending.DueDate.Second == 45,
                "Cross-platform read lost date kind or fractional precision.");
            Assert(pending.DeadlineSetAt.Value.Ticks % 10000 == 5678 && pending.DeadlineSetAt.Value.Kind == DateTimeKind.Utc,
                "Reading the Windows fixture changed its scheduling origin.");
            Assert(state.Settings.EdgeAutoHide && state.Settings.AutoExpand && state.Settings.SortMode == TaskSortMode.Priority && state.Settings.CompletedCollapsed,
                "Windows display preferences did not survive parsing.");
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
                Assert(item.GetProperty("DueDate").GetString() == "2026-10-31T17:23:45.0004321" && item.GetProperty("Priority").GetInt32() == 1,
                    "Saved output is no longer in the Windows version-one schema.");
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
