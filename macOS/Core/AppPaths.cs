using System;
using System.IO;

namespace CornerTodo.MacCore
{
    public static class AppPaths
    {
        // Explicit test/preview directories are never mixed with the default store.
        public static string GetDataDirectory(string overrideDirectory = null)
        {
            if (overrideDirectory != null)
            {
                if (String.IsNullOrWhiteSpace(overrideDirectory)) throw new ArgumentException("Data directory cannot be empty.", "overrideDirectory");
                return Path.GetFullPath(overrideDirectory);
            }

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS() && String.IsNullOrWhiteSpace(profile)) profile = Environment.GetEnvironmentVariable("HOME");
            return DefaultDirectory(OperatingSystem.IsMacOS(), profile,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        public static string GetDataFilePath(string overrideDirectory = null)
        { return Path.Combine(GetDataDirectory(overrideDirectory), "todos.json"); }

        public static string GetLogPath(string overrideDirectory = null)
        { return Path.Combine(GetDataDirectory(overrideDirectory), "CornerTodo.log"); }

        internal static string DefaultDirectory(bool macOS, string profile, string localData)
        {
            if (macOS)
            {
                if (String.IsNullOrWhiteSpace(profile)) throw new IOException("Cannot determine the current user's home directory.");
                return Path.Combine(profile, "Library", "Application Support", "CornerTodo");
            }
            if (String.IsNullOrWhiteSpace(localData)) throw new IOException("Cannot determine local application data directory.");
            // This Avalonia frontend can be smoke-tested on Windows without opening
            // either the stable portable app's Data or CornerBreak's settings.
            return Path.Combine(localData, "CornerTodo.MacPreview");
        }
    }
}
