using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CornerTodo;

internal static class LaunchOptionsTests
{
    private static int assertions;
    public static void Run()
    {
        string app = Path.Combine(Path.GetTempPath(), "CornerTodo 选项测试", "程序目录");
        LaunchOptions normal = LaunchOptions.Parse(new string[0], app);
        Check(!normal.Startup && !normal.Preview && !normal.Windowed, "Ordinary launch changed default flags.");
        Check(normal.DataDirectory == Path.Combine(app, "Data"), "Default data must be beside the executable, not the logon working directory.");
        LaunchOptions automatic = LaunchOptions.Parse(new[] { "--startup" }, app);
        Check(automatic.Startup && !automatic.Windowed && !automatic.Preview, "Logon launch must retain desktop mode.");
        string data = Path.Combine(app, "独立数据 空格");
        LaunchOptions custom = LaunchOptions.Parse(new[] { "--startup", "--data-dir", data + "\\", "--windowed" }, app);
        Check(custom.Startup && custom.Windowed && custom.DataDirectory == data, "Absolute data normalization or startup flags failed.");
        Check(LaunchOptions.Parse(new[] { "--data-dir", "." }, app).DataDirectory == LaunchOptions.NormalizeDirectory(Environment.CurrentDirectory), "Explicit relative data path changed its command-line meaning.");
        foreach (string[] args in new[] { new[] { "--startup", "--preview" }, new[] { "--preview", "--startup" } }) {
            LaunchOptions preview = LaunchOptions.Parse(args, app);
            Check(preview.Preview && preview.Windowed && !preview.Startup, "Preview must remain separate from real logon startup.");
        }
        string root = Path.GetPathRoot(app);
        Check(LaunchOptions.NormalizeDirectory(root) == root, "A drive root lost its separator.");
        Check(LaunchOptions.GetInstanceKey(data, false) == LaunchOptions.GetInstanceKey(data.ToLowerInvariant() + "\\", false), "Equivalent data paths must share one running instance.");
        Check(LaunchOptions.GetInstanceKey(data, false) != LaunchOptions.GetInstanceKey(data, true), "Preview identity overlaps real data.");
        Check(LaunchOptions.GetInstanceKey(data, false) != LaunchOptions.GetInstanceKey(data + "-other", false), "Independent data directories no longer support independent instances.");
        using (SHA256 hash = SHA256.Create()) {
            string legacy = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(data.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
            Check(LaunchOptions.GetInstanceKey(data, false) == legacy, "The normal v1.1 instance key changed unexpectedly.");
        }
        foreach (string value in new[] { "", "plain", "中文与 spaces", root, data + "\\", "embedded\"quote", "two\\\\slashes\"quoted\\" })
            Check(RoundTrip(value) == value, "Windows argument round-trip failed for " + value);
        Throws(delegate { LaunchOptions.Parse(new[] { "--data-dir" }, app); });
        Throws(delegate { LaunchOptions.Parse(new[] { "--data-dir", "--startup" }, app); });
        Throws(delegate { LaunchOptions.Parse(new[] { "--data-dir", " " }, app); });
        Throws(delegate { LaunchOptions.Parse(new[] { "--unknown" }, app); });
        Throws(delegate { LaunchOptions.NormalizeDirectory(null); });
        Console.WriteLine("PASS: " + assertions + " launch-option assertions (no startup registration or data writes).");
    }

    private static string RoundTrip(string value)
    {
        int count;
        IntPtr arguments = CommandLineToArgvW("fixture.exe " + LaunchOptions.QuoteArgument(value), out count);
        if (arguments == IntPtr.Zero) throw new InvalidOperationException("Windows could not parse the test command line.");
        try { Check(count == 2, "Quoted input created extra process arguments."); return Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, IntPtr.Size)); }
        finally { LocalFree(arguments); }
    }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { assertions++; return; }
        throw new InvalidOperationException("An invalid launch option was accepted.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); assertions++; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
