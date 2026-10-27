using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using CornerTodo;

internal static class StartupTests
{
    private static string root;
    private static string executable;
    private static string data;
    private static int passed;

    [STAThread]
    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "CornerTodo-StartupTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            executable = FakeExecutable("中文 程序", "桌面待办.exe");
            data = Path.Combine(root, "独立 数据", "事项");
            Run("Constructors and missing-entry inspection are read-only", ReadOnlyConstruction);
            Run("Native shortcut round-trip preserves Unicode paths and exact launch metadata", RoundTrip);
            Run("Repeated enable and disable leave exactly one owned entry", RepeatedOperations);
            Run("Trailing directory separators and root-directory quoting round-trip", NormalizedPaths);
            Run("An old executable location is detected and explicitly updated", MovedProgram);
            Run("Independent data directories are recognized and explicitly updated", IndependentData);
            Run("A missing executable cannot be enabled or shown as current", MissingExecutable);
            Run("An occupied destination preserves the previous registration during failed replacement", LockedReplacement);
            Run("An occupied destination cannot be silently disabled", LockedDisable);
            Run("A non-directory startup location fails without damaging it", InvalidStartupDirectory);
            Run("Malformed same-name data is never overwritten or removed", MalformedCollision);
            Run("A valid foreign same-name shortcut is never overwritten or removed", ForeignCollision);
            Run("A same-name directory is never replaced or recursively removed", DirectoryCollision);
#if !STARTUP_NATIVE_ONLY
            LaunchOptionsTests.Run();
            StartupUiTests.Run();
#endif
#if STARTUP_NATIVE_ONLY
            Console.WriteLine("PASS: " + passed + " native startup-registration tests (service-only run).");
#else
            Console.WriteLine("PASS: " + passed + " native startup-registration tests, plus launch/UI regressions.");
#endif
            Console.WriteLine("All Enable/Disable calls used isolated temporary startup directories; real Startup and registry were not modified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            string full = Path.GetFullPath(root);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(full).StartsWith("CornerTodo-StartupTests-", StringComparison.Ordinal))
            {
                try { Directory.Delete(full, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void Run(string name, Action test)
    { test(); passed++; Console.WriteLine("  OK " + name); }
    private static string DirectoryFor(string name) { return Path.Combine(root, name); }
    private static string LinkFor(string folder) { return Path.Combine(folder, StartupManager.ShortcutName); }
    private static StartupManager Manager(string name) { return new StartupManager(executable, data, DirectoryFor(name)); }
    private static string FakeExecutable(string folder, string file)
    {
        string directory = Path.Combine(root, folder); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, file); File.WriteAllText(path, "Never executed; shortcut integration test fixture."); return path;
    }

    private static void ReadOnlyConstruction()
    {
        string missing = DirectoryFor("constructor-must-not-create-this");
        StartupManager manager = new StartupManager(executable, data, missing);
        StartupRegistration state = manager.Inspect();
        Assert(!state.Exists && !state.IsManaged && !state.MatchesCurrent, "Missing startup entry was enabled.");
        Assert(!Directory.Exists(missing), "Construction/inspection created a startup directory.");
        Assert(!Directory.Exists(data), "Construction/inspection created the data directory.");
        // Do not Inspect or mutate the real Startup directory. The default
        // constructor is also deliberately free from file/directory writes.
        StartupManager defaultManager = new StartupManager(executable, data);
        Assert(defaultManager != null && !Directory.Exists(data), "Default construction wrote data.");
    }

    private static void RoundTrip()
    {
        string folder = DirectoryFor("round-trip");
        StartupManager manager = new StartupManager(executable, data, folder);
        manager.Enable();
        StartupRegistration state = manager.Inspect();
        Assert(state.Exists && state.IsManaged && state.MatchesCurrent, "Enabled shortcut was not recognized.");
        Snapshot actual = ReadLink(LinkFor(folder));
        Assert(String.Equals(actual.Target, executable, StringComparison.OrdinalIgnoreCase), "Shortcut target changed.");
        Assert(actual.Arguments == "--startup --data-dir " + LaunchOptions.QuoteArgument(data), "Shortcut arguments changed.");
        Assert(String.Equals(actual.WorkingDirectory, Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase), "Shortcut working directory changed.");
        Assert(actual.Description == StartupManager.ProductMarker, "Product ownership marker was not written.");
        Assert(actual.ShowCommand == 1, "Shortcut should request a normal window.");
        Assert(!Directory.Exists(data), "Enabling startup created or read application data.");
        Assert(Directory.GetFiles(folder).Length == 1, "Temporary or backup file was left in Startup.");
    }

    private static void RepeatedOperations()
    {
        string folder = DirectoryFor("repeat"); StartupManager manager = Manager("repeat");
        for (int i = 0; i < 3; i++)
        {
            manager.Enable(); manager.Enable();
            Assert(manager.Inspect().MatchesCurrent && Directory.GetFiles(folder).Length == 1, "Repeated enable created duplicate entries.");
            string unrelated = Path.Combine(folder, "unrelated.txt"); File.WriteAllText(unrelated, "keep");
            manager.Disable(); manager.Disable();
            Assert(!manager.Inspect().Exists && File.ReadAllText(unrelated) == "keep", "Disable removed another startup item.");
            File.Delete(unrelated);
        }
    }

    private static void NormalizedPaths()
    {
        string folder = DirectoryFor("normalized");
        new StartupManager(executable, data + "\\\\", folder + "\\").Enable();
        Assert(new StartupManager(executable, data, folder).Inspect().MatchesCurrent, "Trailing separators changed registration identity.");
        string driveRoot = Path.GetPathRoot(root);
        StartupManager rootData = new StartupManager(executable, driveRoot, DirectoryFor("quoted-root"));
        rootData.Enable();
        Assert(rootData.Inspect().MatchesCurrent, "A quoted trailing root backslash did not round-trip.");
    }

    private static void MovedProgram()
    {
        string folder = DirectoryFor("moved"); StartupManager old = Manager("moved"); old.Enable();
        string moved = FakeExecutable("新位置 程序", "CornerTodo.exe");
        StartupManager updated = new StartupManager(moved, data, folder);
        StartupRegistration stale = updated.Inspect();
        Assert(stale.Exists && stale.IsManaged && !stale.MatchesCurrent, "Old location was incorrectly shown as current.");
        updated.Enable();
        Assert(updated.Inspect().MatchesCurrent && !old.Inspect().MatchesCurrent, "Explicit location update failed.");
        Assert(Directory.GetFiles(folder).Length == 1, "Replacement left launchable duplicates or backups.");
        old.Disable();
        Assert(!updated.Inspect().Exists, "A managed old-location registration could not be disabled.");
    }

    private static void IndependentData()
    {
        string folder = DirectoryFor("independent"); StartupManager first = Manager("independent"); first.Enable();
        StartupManager other = new StartupManager(executable, Path.Combine(root, "another data"), folder);
        Assert(other.Inspect().IsManaged && !other.Inspect().MatchesCurrent, "Independent data directories were conflated.");
        other.Enable();
        Assert(other.Inspect().MatchesCurrent && !first.Inspect().MatchesCurrent, "Explicit data-directory registration update failed.");
    }

    private static void MissingExecutable()
    {
        string path = FakeExecutable("missing-exe", "CornerTodo.exe");
        string folder = DirectoryFor("missing-target"); StartupManager manager = new StartupManager(path, data, folder);
        manager.Enable(); File.Delete(path);
        StartupRegistration stale = manager.Inspect();
        Assert(stale.Exists && stale.IsManaged && !stale.MatchesCurrent, "A missing target was shown as current.");
        ExpectFailure(delegate { manager.Enable(); });
        Assert(stale.TargetPath == manager.Inspect().TargetPath, "Failed enable changed the old registration.");
        manager.Disable(); Assert(!manager.Inspect().Exists, "A stale owned entry could not be removed.");
    }

    private static void LockedReplacement()
    {
        string folder = DirectoryFor("locked-update"); StartupManager old = Manager("locked-update"); old.Enable();
        string file = LinkFor(folder); byte[] before = File.ReadAllBytes(file);
        string replacement = FakeExecutable("replacement", "CornerTodo.exe");
        StartupManager update = new StartupManager(replacement, data, folder);
        using (FileStream locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectFailure(delegate { update.Enable(); });
        Assert(before.SequenceEqual(File.ReadAllBytes(file)), "Failed atomic replacement damaged the original shortcut.");
        Assert(old.Inspect().MatchesCurrent && !update.Inspect().MatchesCurrent, "Failed replacement changed active registration.");
        Assert(Directory.GetFiles(folder).Length == 1, "Failed replacement leaked temporary files.");
    }

    private static void LockedDisable()
    {
        string folder = DirectoryFor("locked-disable"); StartupManager manager = Manager("locked-disable"); manager.Enable();
        byte[] before = File.ReadAllBytes(LinkFor(folder));
        using (FileStream locked = new FileStream(LinkFor(folder), FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectFailure(delegate { manager.Disable(); });
        Assert(before.SequenceEqual(File.ReadAllBytes(LinkFor(folder))) && manager.Inspect().MatchesCurrent,
            "Failed disable damaged the registration.");
    }

    private static void InvalidStartupDirectory()
    {
        string file = DirectoryFor("a-file-not-startup-directory"); File.WriteAllText(file, "keep original");
        StartupManager manager = new StartupManager(executable, data, file);
        ExpectFailure(delegate { manager.Enable(); });
        Assert(File.ReadAllText(file) == "keep original", "Failed creation damaged the occupied directory path.");
    }

    private static void MalformedCollision()
    {
        string folder = DirectoryFor("malformed"); Directory.CreateDirectory(folder);
        string file = LinkFor(folder); File.WriteAllText(file, "not a product shortcut"); byte[] before = File.ReadAllBytes(file);
        StartupManager manager = Manager("malformed");
        StartupRegistration state = manager.Inspect();
        Assert(state.Exists && !state.IsManaged && !state.MatchesCurrent, "Malformed same-name file was adopted.");
        ExpectFailure(delegate { manager.Enable(); }); ExpectFailure(delegate { manager.Disable(); });
        Assert(before.SequenceEqual(File.ReadAllBytes(file)), "Foreign malformed data was changed.");
    }

    private static void ForeignCollision()
    {
        string folder = DirectoryFor("foreign"); Directory.CreateDirectory(folder);
        string file = LinkFor(folder); WriteForeignLink(file); byte[] before = File.ReadAllBytes(file);
        StartupManager manager = Manager("foreign");
        Assert(manager.Inspect().Exists && !manager.Inspect().IsManaged && !manager.Inspect().MatchesCurrent,
            "A foreign same-name shortcut was adopted.");
        ExpectFailure(delegate { manager.Enable(); }); ExpectFailure(delegate { manager.Disable(); });
        Assert(before.SequenceEqual(File.ReadAllBytes(file)), "A foreign same-name shortcut was overwritten or removed.");
    }

    private static void DirectoryCollision()
    {
        string folder = DirectoryFor("directory-collision"); Directory.CreateDirectory(LinkFor(folder));
        string child = Path.Combine(LinkFor(folder), "keep.txt"); File.WriteAllText(child, "keep");
        StartupManager manager = Manager("directory-collision");
        Assert(manager.Inspect().Exists && !manager.Inspect().IsManaged, "A same-name directory was adopted.");
        ExpectFailure(delegate { manager.Enable(); }); ExpectFailure(delegate { manager.Disable(); });
        Assert(File.ReadAllText(child) == "keep", "A collision directory was damaged.");
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        catch (COMException) { return; }
        throw new InvalidOperationException("The operation unexpectedly succeeded.");
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Snapshot { public string Target, Arguments, WorkingDirectory, Description; public int ShowCommand; }
    private static Snapshot ReadLink(string path)
    {
        object instance = new TestShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0); TestLink link = (TestLink)instance;
            StringBuilder target = new StringBuilder(32768), args = new StringBuilder(32768), working = new StringBuilder(32768), description = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetArguments(args, args.Capacity);
            link.GetWorkingDirectory(working, working.Capacity); link.GetDescription(description, description.Capacity);
            int show; link.GetShowCmd(out show);
            return new Snapshot { Target = target.ToString(), Arguments = args.ToString(), WorkingDirectory = working.ToString(), Description = description.ToString(), ShowCommand = show };
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    private static void WriteForeignLink(string path)
    {
        object instance = new TestShellLink();
        try
        {
            TestLink link = (TestLink)instance;
            link.SetPath(executable); link.SetArguments("--startup"); link.SetDescription("Another product - do not remove");
            ((IPersistFile)instance).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private sealed class TestShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface TestLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList); void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey);
        void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr owner, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
