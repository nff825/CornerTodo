using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CornerTodo
{
    internal interface IFileLinkService
    {
        IList<string> PickFiles(Window owner);
        string PickReplacement(Window owner, string currentPath);
        void Open(string path);
        void Locate(string path);
    }

    /// <summary>Chooses or opens original files only after an explicit user action.</summary>
    internal sealed class WindowsFileLinkService : IFileLinkService
    {
        public IList<string> PickFiles(Window owner)
        {
            OpenFileDialog dialog = CreateDialog("关联本地文件", true);
            if (Show(dialog, owner) != true) return new List<string>();
            var selected = new List<string>();
            foreach (string path in dialog.FileNames) selected.Add(ExistingFile(path));
            return selected;
        }

        public string PickReplacement(Window owner, string currentPath)
        {
            OpenFileDialog dialog = CreateDialog("重新关联文件", false);
            if (!String.IsNullOrWhiteSpace(currentPath)) {
                try {
                    dialog.FileName = Path.GetFileName(currentPath.Replace('/', '\\'));
                    string directory = Path.GetDirectoryName(currentPath);
                    if (IsWindowsAbsolutePath(currentPath) && Directory.Exists(directory)) dialog.InitialDirectory = directory;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
            return Show(dialog, owner) == true ? ExistingFile(dialog.FileName) : null;
        }

        public void Open(string path)
        {
            string existing = ExistingFile(path);
            try {
                // ShellExecute opens the document's registered default action.
                // The path is a FileName value, never executable command text.
                using (Process process = Process.Start(new ProcessStartInfo {
                    FileName = existing, UseShellExecute = true, Verb = "open"
                })) { }
            }
            catch (Win32Exception error) {
                throw new InvalidOperationException("无法打开文件。请确认有访问权限，并已设置该文件类型的默认应用。", error);
            }
        }

        public void Locate(string path)
        {
            string existing = ExistingFile(path);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            try {
                using (Process process = Process.Start(new ProcessStartInfo {
                    FileName = Path.Combine(windows, "explorer.exe"), UseShellExecute = false,
                    Arguments = "/select," + LaunchOptions.QuoteArgument(existing), WorkingDirectory = windows
                })) { }
            }
            catch (Win32Exception error) {
                throw new InvalidOperationException("无法打开所在文件夹。请确认资源管理器可用并有访问权限。", error);
            }
        }

        private static OpenFileDialog CreateDialog(string title, bool multiple)
        {
            return new OpenFileDialog {
                Title = title, Filter = "所有文件 (*.*)|*.*", Multiselect = multiple,
                CheckFileExists = true, CheckPathExists = true, ValidateNames = true, AddExtension = false
            };
        }

        private static bool? Show(OpenFileDialog dialog, Window owner)
        {
            return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        }

        private static string ExistingFile(string path)
        {
            if (!IsWindowsAbsolutePath(path))
                throw new InvalidOperationException("此文件位置无法在当前电脑打开，请选择本机文件重新关联。");
            string full;
            try { full = Path.GetFullPath(path); }
            catch (ArgumentException error) { throw InvalidPath(error); }
            catch (NotSupportedException error) { throw InvalidPath(error); }
            catch (PathTooLongException error) { throw InvalidPath(error); }
            if (!File.Exists(full))
                throw new FileNotFoundException("找不到文件，可能已被移动、重命名或暂时无法访问。请重新关联。", full);
            return full;
        }

        private static bool IsWindowsAbsolutePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return true;
            return path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        }

        private static Exception InvalidPath(Exception error)
        {
            return new InvalidOperationException("文件位置无效，请重新关联本机文件。", error);
        }
    }
}
