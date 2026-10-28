using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace CornerTodo
{
    internal interface IStartupManager
    {
        StartupRegistration Inspect();
        void Enable();
        void Disable();
    }

    internal sealed class StartupRegistration
    {
        public StartupRegistration(bool exists, bool matchesCurrent, string targetPath, string arguments, bool isManaged)
        {
            Exists = exists; MatchesCurrent = matchesCurrent; TargetPath = targetPath ?? "";
            Arguments = arguments ?? ""; IsManaged = isManaged;
        }
        public bool Exists { get; private set; }
        public bool MatchesCurrent { get; private set; }
        public string TargetPath { get; private set; }
        public string Arguments { get; private set; }
        public bool IsManaged { get; private set; }
    }

    /// <summary>
    /// Owns one marked shortcut in the current user's Startup folder. Merely
    /// constructing or inspecting this service never creates/repairs a shortcut.
    /// StartupApproved and registry settings are deliberately outside its scope.
    /// </summary>
    internal sealed class StartupManager : IStartupManager
    {
        internal const string ShortcutName = "CornerTodo-LoginStartup.lnk";
        internal const string ProductMarker = "CornerTodo.LoginStartup.ManagedShortcut.v1";
        private readonly string executablePath;
        private readonly string dataDirectory;
        private readonly string startupDirectory;
        private readonly string workingDirectory;
        private readonly string arguments;
        private readonly string shortcutPath;

        public StartupManager(string executablePath, string dataDirectory)
            : this(executablePath, dataDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Startup,
                Environment.SpecialFolderOption.DoNotVerify)) { }

        // Tests must pass an isolated directory here; production uses the default.
        public StartupManager(string executablePath, string dataDirectory, string startupDirectory)
        {
            if (String.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("Executable path is required.", "executablePath");
            this.executablePath = Path.GetFullPath(executablePath);
            this.dataDirectory = LaunchOptions.NormalizeDirectory(dataDirectory);
            this.startupDirectory = LaunchOptions.NormalizeDirectory(startupDirectory);
            workingDirectory = LaunchOptions.NormalizeDirectory(Path.GetDirectoryName(this.executablePath));
            arguments = "--startup --data-dir " + LaunchOptions.QuoteArgument(this.dataDirectory);
            shortcutPath = Path.Combine(this.startupDirectory, ShortcutName);
        }

        public StartupRegistration Inspect()
        {
            LinkEntry entry = ReadEntry(shortcutPath);
            return new StartupRegistration(entry.Exists, entry.Managed && Matches(entry), entry.Target,
                entry.Arguments, entry.Managed);
        }

        public void Enable()
        {
            if (!File.Exists(executablePath)) throw new FileNotFoundException("当前程序文件不存在，无法登记登录自启。", executablePath);
            LinkEntry previous = ReadEntry(shortcutPath);
            RequireManagedOrMissing(previous);
            if (previous.Managed && Matches(previous)) return;
            // No directory or file creation occurs until this explicit operation.
            Directory.CreateDirectory(startupDirectory);
            string token = Guid.NewGuid().ToString("N");
            // These temporary/backup extensions are not launchable .lnk entries.
            string temporary = Path.Combine(startupDirectory, "CornerTodo-LoginStartup-" + token + ".tmp");
            string backup = Path.Combine(startupDirectory, "CornerTodo-LoginStartup-" + token + ".bak");
            bool committed = false;
            bool keepBackup = false;
            try
            {
                WriteLink(temporary);
                using (FileStream stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                    stream.Flush(true);
                LinkEntry staged = ReadEntry(temporary);
                if (!staged.Managed || !Matches(staged)) throw new IOException("登录自启快捷方式读回校验失败。");
                // Check again after staging; a same-name foreign entry must not be
                // overwritten merely because it appeared during link creation.
                LinkEntry beforeCommit = ReadEntry(shortcutPath);
                RequireManagedOrMissing(beforeCommit);
                if (beforeCommit.Exists != previous.Exists ||
                    (previous.Exists && !SameLink(previous, beforeCommit)))
                    throw new IOException("自启快捷方式在操作期间发生变化，请重试。");
                if (previous.Exists) File.Replace(temporary, shortcutPath, backup, true);
                else File.Move(temporary, shortcutPath);
                committed = true;
                LinkEntry installed = ReadEntry(shortcutPath);
                if (!installed.Managed || !Matches(installed)) throw new IOException("登录自启快捷方式提交后校验失败。");
            }
            catch (Exception failure)
            {
                if (committed || File.Exists(backup))
                {
                    try
                    {
                        LinkEntry installed = ReadEntry(shortcutPath);
                        RequireManagedOrMissing(installed);
                        if (previous.Exists && File.Exists(backup))
                        {
                            if (installed.Exists) File.Replace(backup, shortcutPath, null, true);
                            else File.Move(backup, shortcutPath);
                        }
                        else if (!previous.Exists && installed.Managed && Matches(installed)) File.Delete(shortcutPath);
                    }
                    catch (Exception rollback)
                    {
                        keepBackup = true;
                        throw new IOException("自启登记失败，恢复也未完成。原快捷方式备份保留在：" + backup,
                            new AggregateException(failure, rollback));
                    }
                }
                throw;
            }
            finally
            {
                DeleteTemporary(temporary);
                if (!keepBackup) DeleteTemporary(backup);
            }
        }

        public void Disable()
        {
            LinkEntry entry = ReadEntry(shortcutPath);
            RequireManagedOrMissing(entry);
            if (!entry.Exists) return;
            // Never enumerate or delete other startup items, including our temp files.
            File.Delete(shortcutPath);
            if (ReadEntry(shortcutPath).Exists) throw new IOException("自启快捷方式未能移除。");
        }

        private static void RequireManagedOrMissing(LinkEntry entry)
        {
            if (entry.Exists && !entry.Managed)
                throw new IOException("同名自启项不是本程序登记的快捷方式，已保留原文件，请先处理此名称冲突。");
        }

        private bool Matches(LinkEntry entry)
        {
            if (!File.Exists(executablePath)) return false;
            try
            {
                return String.Equals(Path.GetFullPath(entry.Target), executablePath, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(LaunchOptions.NormalizeDirectory(entry.WorkingDirectory), workingDirectory, StringComparison.OrdinalIgnoreCase) &&
                    ArgumentsMatch(entry.Arguments);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        private bool ArgumentsMatch(string value)
        {
            int count;
            IntPtr parsed = CommandLineToArgvW("CornerTodo " + (value ?? ""), out count);
            if (parsed == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                bool startup = false, data = false;
                for (int i = 1; i < count; i++)
                {
                    string argument = Marshal.PtrToStringUni(Marshal.ReadIntPtr(parsed, i * IntPtr.Size));
                    if (argument == "--startup" && !startup) startup = true;
                    else if (argument == "--data-dir" && !data && i + 1 < count)
                    {
                        string path = Marshal.PtrToStringUni(Marshal.ReadIntPtr(parsed, (++i) * IntPtr.Size));
                        if (!Path.IsPathRooted(path) || !String.Equals(LaunchOptions.NormalizeDirectory(path), dataDirectory,
                            StringComparison.OrdinalIgnoreCase)) return false;
                        data = true;
                    }
                    else return false;
                }
                return startup && data;
            }
            finally { LocalFree(parsed); }
        }

        private void WriteLink(string file)
        {
            object instance = new ShellLink();
            try
            {
                IShellLinkW link = (IShellLinkW)instance;
                link.SetPath(executablePath);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(ProductMarker);
                link.SetShowCmd(1); // normal window; no minimizing or forced activation
                link.SetIconLocation(executablePath, 0);
                ((IPersistFile)instance).Save(file, true);
            }
            finally { Marshal.FinalReleaseComObject(instance); }
        }

        private static LinkEntry ReadEntry(string file)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(file); }
            catch (FileNotFoundException) { return new LinkEntry(); }
            catch (DirectoryNotFoundException) { return new LinkEntry(); }
            LinkEntry entry = new LinkEntry { Exists = true };
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return entry;
            // Verify read access independently, so access/share failures are not
            // mistaken for an ordinary malformed shortcut and silently ignored.
            using (FileStream readable = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete)) { }
            object instance = new ShellLink();
            try
            {
                ((IPersistFile)instance).Load(file, 0); // STGM_READ; never Resolve or Save
                IShellLinkW link = (IShellLinkW)instance;
                StringBuilder target = new StringBuilder(32768), args = new StringBuilder(32768);
                StringBuilder description = new StringBuilder(1024), working = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH
                link.GetArguments(args, args.Capacity);
                link.GetDescription(description, description.Capacity);
                link.GetWorkingDirectory(working, working.Capacity);
                entry.Target = target.ToString(); entry.Arguments = args.ToString();
                entry.WorkingDirectory = working.ToString();
                entry.Managed = String.Equals(description.ToString(), ProductMarker, StringComparison.Ordinal);
                return entry;
            }
            catch (COMException error)
            {
                if (error.ErrorCode == unchecked((int)0x80070005) || error.ErrorCode == unchecked((int)0x80030005) ||
                    error.ErrorCode == unchecked((int)0x80070020) || error.ErrorCode == unchecked((int)0x80030020)) throw;
                return entry; // malformed/non-link data is an occupied, foreign entry
            }
            finally { Marshal.FinalReleaseComObject(instance); }
        }

        private static bool SameLink(LinkEntry left, LinkEntry right)
        {
            return left.Managed == right.Managed && left.Target == right.Target &&
                left.Arguments == right.Arguments && left.WorkingDirectory == right.WorkingDirectory;
        }
        private static void DeleteTemporary(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private sealed class LinkEntry
        {
            public bool Exists, Managed;
            public string Target = "", Arguments = "", WorkingDirectory = "";
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private sealed class ShellLink { }
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int command);
            void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr owner, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
