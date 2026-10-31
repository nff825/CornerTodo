using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CornerTodo.MacDesktop;

namespace CornerTodo.MacTests
{
    internal static class SingleInstanceTests
    {
        private static int assertions;

        internal static int Run(string qaRoot)
        {
            Preview(Path.Combine(qaRoot, "single-preview"));
            BusyAndRepeatedRequests(Directory(qaRoot, "single-requests"));
            Tokens(Directory(qaRoot, "single-tokens"));
            Restart(Directory(qaRoot, "single-restart"));
            Failures(Directory(qaRoot, "single-failures"));
            CrossProcess(Directory(qaRoot, "single-child"));
            ErrorClassification();
            Console.WriteLine("PASS: " + assertions + " single-instance/file-request assertions, including isolated child processes.");
            return assertions;
        }

        private static string Directory(string root, string name)
        {
            string directory = Path.Combine(root, name);
            System.IO.Directory.CreateDirectory(directory);
            return directory;
        }

        private static void Preview(string directory)
        {
            int calls = 0;
            using (var preview = new SingleInstance(directory, true,
                delegate(string path) { calls++; throw new InvalidOperationException("Preview opened a lock."); },
                delegate(string path) { calls++; throw new InvalidOperationException("Preview read a request."); }))
            {
                Assert(preview.IsPrimary && !preview.TryTakeRestoreRequest(false), "Preview did not remain a write-free standalone instance.");
                Assert(!System.IO.Directory.Exists(directory) && calls == 0, "Preview created a directory or used a file boundary.");
            }
            Assert(!System.IO.Directory.Exists(directory), "Preview disposal created a directory.");
        }

        private static void BusyAndRepeatedRequests(string directory)
        {
            string data = Path.Combine(directory, "todos.json"); File.WriteAllText(data, "synthetic task data", new UTF8Encoding(false));
            byte[] original = File.ReadAllBytes(data);
            using (var primary = new SingleInstance(directory, false))
            {
                Assert(primary.IsPrimary && !primary.TryTakeRestoreRequest(false), "First instance did not acquire the store lock cleanly.");
                string first;
                using (var second = new SingleInstance(directory, false))
                {
                    Assert(!second.IsPrimary && !second.TryTakeRestoreRequest(false), "A duplicate became a task writer or consumed requests.");
                    first = File.ReadAllText(Path.Combine(directory, "restore.request"));
                    Assert(first.Length == 32 && Guid.TryParseExact(first, "N", out _), "A duplicate did not create a data-only random token.");
                }
                Assert(!primary.TryTakeRestoreRequest(true), "Busy editor consumed a restore token.");
                Assert(primary.TryTakeRestoreRequest(false) && !primary.TryTakeRestoreRequest(false), "Restore was lost during editing or consumed more than once.");
                using (var third = new SingleInstance(directory, false)) Assert(!third.IsPrimary, "Another duplicate acquired the data lock.");
                string next = File.ReadAllText(Path.Combine(directory, "restore.request"));
                Assert(next != first && primary.TryTakeRestoreRequest(false), "Repeated manual launches did not request distinct restores.");
                Assert(Convert.ToHexString(original) == Convert.ToHexString(File.ReadAllBytes(data)), "Instance coordination changed task data.");
            }
        }

        private static void Tokens(string directory)
        {
            string request = Path.Combine(directory, "restore.request");
            using var primary = new SingleInstance(directory, false);
            string[] invalid = { "", "not-a-guid", new string('f', 31), new string('f', 33), new string('f', 129),
                Guid.NewGuid().ToString("D"), "{\"command\":\"open something\"}", Guid.NewGuid().ToString("N") + "\n" };
            foreach (string token in invalid)
            {
                File.WriteAllText(request, token, new UTF8Encoding(false));
                Assert(!primary.TryTakeRestoreRequest(false), "Invalid or oversized token was executed as a restore request.");
            }
            File.WriteAllBytes(request, new byte[32]);
            Assert(!primary.TryTakeRestoreRequest(false), "Binary NUL bytes were accepted as a token.");
            File.WriteAllBytes(request, System.Linq.Enumerable.Repeat((byte)255, 32).ToArray());
            Assert(!primary.TryTakeRestoreRequest(false), "Non-ASCII data was accepted as a token.");
            string valid = Guid.NewGuid().ToString("N"); File.WriteAllText(request, valid);
            Assert(primary.TryTakeRestoreRequest(false), "Invalid input permanently blocked a later valid token.");
            File.WriteAllText(request, valid.ToUpperInvariant());
            Assert(!primary.TryTakeRestoreRequest(false), "The same GUID in another case was treated as a new command.");
        }

        private static void Restart(string directory)
        {
            string request = Path.Combine(directory, "restore.request");
            File.WriteAllText(request, Guid.NewGuid().ToString("N"));
            var old = new SingleInstance(directory, false);
            Assert(old.IsPrimary && !old.TryTakeRestoreRequest(false), "A previous run's leftover token triggered a startup restore.");
            using (var second = new SingleInstance(directory, false)) Assert(!second.IsPrimary, "The first generation lost its data lock.");
            old.Dispose(); old.Dispose();
            Assert(!old.TryTakeRestoreRequest(false), "A disposed generation continued responding to restore requests.");
            using var replacement = new SingleInstance(directory, false);
            Assert(replacement.IsPrimary && !replacement.TryTakeRestoreRequest(false), "Restart failed to reacquire the store or consumed a stale request.");
            using (var next = new SingleInstance(directory, false)) Assert(!next.IsPrimary, "The replacement did not hold the data lock.");
            Assert(replacement.TryTakeRestoreRequest(false) && !old.TryTakeRestoreRequest(false), "A new request reached the wrong application generation.");
        }

        private static void Failures(string directory)
        {
            string request = Path.Combine(directory, "restore.request");
            var diskError = new IOException("Synthetic disk I/O error", 5);
            try
            {
                using var ignored = new SingleInstance(directory, false,
                    delegate(string path) { throw diskError; }, delegate(string path) { return null; });
                throw new InvalidOperationException("A non-lock I/O error was mistaken for a duplicate instance.");
            }
            catch (IOException error) { Assert(Object.ReferenceEquals(error, diskError), "The lock-open failure was not propagated intact."); }
            Assert(!File.Exists(request), "A non-lock I/O error sent a misleading restore request.");
            FileStream captured = null;
            var readError = new IOException("Synthetic request read error", 32);
            try
            {
                using var ignored = new SingleInstance(directory, false,
                    delegate(string path) { captured = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return captured; },
                    delegate(string path) { throw readError; });
                throw new InvalidOperationException("A request-read error was mistaken for a duplicate instance.");
            }
            catch (IOException error) { Assert(Object.ReferenceEquals(error, readError), "The post-acquisition read error was not propagated intact."); }
            Assert(captured != null && !captured.CanRead && !File.Exists(request), "A failed request read leaked the lock or sent a new request.");
            using (var recovered = new SingleInstance(directory, false)) Assert(recovered.IsPrimary, "Startup failure left the data directory locked.");

            bool failRead = false; int reads = 0;
            var transient = new SingleInstance(directory, false,
                delegate(string path) { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); },
                delegate(string path) { reads++; if (failRead) throw new IOException("Temporary request read failure"); return File.Exists(path) ? File.ReadAllText(path) : null; });
            failRead = true;
            Assert(!transient.TryTakeRestoreRequest(false), "A temporary request-read error escaped the polling boundary.");
            string token = Guid.NewGuid().ToString("N"); File.WriteAllText(request, token); failRead = false;
            Assert(transient.TryTakeRestoreRequest(false), "A transient read failure consumed the pending restore token.");
            transient.Dispose(); int beforeLate = reads;
            Assert(!transient.TryTakeRestoreRequest(false) && reads == beforeLate, "Disposal did not stop request file reads.");
        }

        private static void CrossProcess(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "instance-tests.marker"), "synthetic QA only");
            using var primary = new SingleInstance(directory, false);
            string first = Child(directory, "one");
            Assert(primary.TryTakeRestoreRequest(false), "A child-process duplicate did not wake the primary.");
            string second = Child(directory, "two");
            Assert(first != second && primary.TryTakeRestoreRequest(false), "A second process did not create a distinct request.");
            Assert(!File.Exists(Path.Combine(directory, "writer.probe")), "A duplicate child was allowed to write as a primary.");
        }

        private static string Child(string directory, string suffix)
        {
            string output = Path.Combine(directory, "child-" + suffix + ".json");
            string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("No current process path.");
            var start = new ProcessStartInfo(processPath) { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            if (String.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(directory); start.ArgumentList.Add(output);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the isolated instance probe.");
            if (!child.WaitForExit(15000))
            { child.Kill(); throw new InvalidOperationException("Isolated instance probe timed out."); }
            Assert(child.ExitCode == 0, "Child probe failed: " + child.StandardError.ReadToEnd());
            using var result = JsonDocument.Parse(File.ReadAllText(output));
            Assert(!result.RootElement.GetProperty("isPrimary").GetBoolean(), "A second process opened the same data as a primary.");
            return result.RootElement.GetProperty("token").GetString() ?? "";
        }

        internal static int Probe(string directory, string output)
        {
            try
            {
                if (!File.Exists(Path.Combine(directory, "instance-tests.marker"))) throw new IOException("The instance probe requires a test-owned directory.");
                using var instance = new SingleInstance(directory, false);
                if (instance.IsPrimary) File.WriteAllText(Path.Combine(directory, "writer.probe"), "primary");
                string request = Path.Combine(directory, "restore.request");
                File.WriteAllText(output, JsonSerializer.Serialize(new { isPrimary = instance.IsPrimary, token = File.Exists(request) ? File.ReadAllText(request) : null }));
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void ErrorClassification()
        {
            Assert(SingleInstance.IsLockContention(new IOException("share", unchecked((int)0x80070020)), true, false), "Windows sharing violation was not recognized.");
            Assert(SingleInstance.IsLockContention(new IOException("lock", unchecked((int)0x80070021)), true, false), "Windows lock violation was not recognized.");
            Assert(SingleInstance.IsLockContention(new IOException("Darwin EWOULDBLOCK", 35), false, true), "Darwin raw lock errno was not recognized.");
            Assert(SingleInstance.IsLockContention(new IOException("Linux EAGAIN", 11), false, false), "Linux raw lock errno was not recognized.");
            Assert(!SingleInstance.IsLockContention(new IOException("disk", 5), true, false), "Generic Windows I/O failure was mistaken for contention.");
            Assert(!SingleInstance.IsLockContention(new IOException("other errno", 32), false, true), "A non-contention Darwin errno was accepted.");
            Assert(!SingleInstance.IsLockContention(new IOException("other errno", 35), true, false), "Darwin errno was accepted as a Windows lock conflict.");
        }

        private static void Assert(bool value, string message)
        { assertions++; if (!value) throw new InvalidOperationException(message); }
    }
}
