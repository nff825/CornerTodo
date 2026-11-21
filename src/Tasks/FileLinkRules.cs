using System;
using System.Collections.Generic;

namespace CornerTodo
{
    /// <summary>Stores references to original files without accessing the file system.</summary>
    public static class FileLinkRules
    {
        public static List<string> Copy(IEnumerable<string> paths)
        {
            return paths == null ? new List<string>() : new List<string>(paths);
        }

        public static List<string> Normalize(IEnumerable<string> paths)
        {
            var result = new List<string>();
            if (paths == null) return result;
            var windows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var posix = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                bool isWindows = WindowsAbsolute(path);
                if (path.IndexOf('\0') >= 0 || (!isWindows && !path.StartsWith("/", StringComparison.Ordinal)))
                    throw new ArgumentException("关联文件须使用原文件的绝对路径。", "paths");
                // Keep the first original spelling. Never expand, resolve, inspect,
                // copy or remove a path, including paths from a different platform.
                if ((isWindows ? windows : posix).Add(path)) result.Add(path);
            }
            return result;
        }

        private static bool WindowsAbsolute(string path)
        {
            if (path.Length >= 3 && IsDriveLetter(path[0]) && path[1] == ':' && IsSeparator(path[2])) return true;
            if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            // Extended drive paths remain Windows references on non-Windows hosts.
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 7 &&
                IsDriveLetter(path[4]) && path[5] == ':' && IsSeparator(path[6])) return true;
            string unc = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? path.Substring(8) : path.Substring(2);
            int separator = unc.IndexOf('\\');
            if (separator <= 0 || separator == unc.Length - 1) return false;
            string server = unc.Substring(0, separator);
            string share = unc.Substring(separator + 1).Split('\\')[0];
            return server != "." && server != "?" && share.Length > 0 && share != "." && share != "..";
        }

        private static bool IsDriveLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsSeparator(char value) { return value == '\\' || value == '/'; }
    }
}
