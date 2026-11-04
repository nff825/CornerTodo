using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CornerTodo.MacCore
{
    /// <summary>Preserves the original directory before a legacy load can migrate or recover it.</summary>
    internal static class MacUpgradeBackup
    {
        internal const string DirectoryName = "UpgradeBackups";

        internal static string CreateBeforeLoad(string dataDirectory)
        {
            string root = Path.GetFullPath(dataDirectory);
            try { RejectLink(root); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            if (!Directory.Exists(root)) throw new IOException("数据目录路径被文件占用，请保留该文件并检查：" + root);
            string primary = Path.Combine(root, "todos.json");
            string previous = primary + ".bak";
            string candidate = File.Exists(primary) ? primary : File.Exists(previous) ? previous : null;
            if (candidate == null) return null;
            RejectLink(candidate);
            int sourceVersion;
            bool needsBackup = NeedsBackup(candidate, out sourceVersion);
            // A semantically damaged current primary can cause TodoStore to
            // recover an older .bak and write the migrated schema immediately.
            // Preserve both generations before that recovery path is allowed.
            if (!needsBackup && sourceVersion == TodoState.CurrentVersion && File.Exists(primary) && File.Exists(previous))
            {
                RejectLink(previous);
                int previousVersion;
                if (NeedsBackup(previous, out previousVersion)) { needsBackup = true; sourceVersion = previousVersion; }
            }
            if (!needsBackup) return null;

            // Inventory first: links and inaccessible subdirectories fail before
            // any source file can be read by the mutating recovery/load path.
            var relativeFiles = new List<string>();
            var relativeDirectories = new List<string>();
            Inventory(root, root, relativeFiles, relativeDirectories);
            string parent = Path.Combine(root, DirectoryName);
            if (File.Exists(parent)) throw new IOException("升级备份路径被文件占用，请保留原数据并处理该路径：" + parent);
            if (Directory.Exists(parent)) RejectLink(parent);
            Directory.CreateDirectory(parent);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string final = Path.Combine(parent, stamp + "-schema" + sourceVersion + "-" + suffix);
            string stage = Path.Combine(parent, ".partial-" + suffix);
            Directory.CreateDirectory(stage);
            var manifest = new List<object>();
            try
            {
                foreach (string relative in relativeDirectories) Directory.CreateDirectory(Child(stage, relative));
                foreach (string relative in relativeFiles)
                {
                    string source = Child(root, relative), target = Child(stage, relative);
                    RejectLink(source);
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output); output.Flush(true);
                    }
                    string sourceHash = Hash(source), targetHash = Hash(target);
                    if (sourceHash != targetHash) throw new IOException("备份校验失败，原数据尚未升级。");
                    manifest.Add(new { Path = relative, SHA256 = targetHash, Length = new FileInfo(target).Length });
                }
                string contents = JsonSerializer.Serialize(new {
                    Purpose = "CornerTodo pre-upgrade backup", SourceVersion = sourceVersion,
                    TargetVersion = TodoState.CurrentVersion, CreatedAtUtc = DateTime.UtcNow, Files = manifest
                }, new JsonSerializerOptions { WriteIndented = true });
                string manifestPath = Path.Combine(stage, "BACKUP-MANIFEST.json");
                if (File.Exists(manifestPath) || Directory.Exists(manifestPath))
                    manifestPath = Path.Combine(stage, "BACKUP-MANIFEST-" + Guid.NewGuid().ToString("N") + ".json");
                using (var output = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
                    output.Write(bytes, 0, bytes.Length); output.Flush(true);
                }
                Directory.Move(stage, final);
                return final;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // Keep partial copies for diagnosis; never delete or rewrite originals.
                throw new IOException("升级前备份未完成，已停止读取和升级。原数据保持不变。备份位置：" + stage, error);
            }
        }

        private static bool NeedsBackup(string file, out int version)
        {
            version = 1;
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(file, new UTF8Encoding(false, true)), new JsonDocumentOptions { MaxDepth = 64 }))
                {
                    JsonElement root = document.RootElement, saved;
                    if (root.ValueKind != JsonValueKind.Object) return true;
                    if (root.TryGetProperty("Version", out saved))
                    {
                        if (saved.ValueKind != JsonValueKind.Number || !saved.TryGetInt32(out version)) { version = 0; return true; }
                    }
                }
                // Unknown future versions are left untouched for TodoStore's
                // explicit unsupported-version error, never restored from a backup.
                return version < TodoState.CurrentVersion;
            }
            catch (JsonException) { version = 0; return true; }
            catch (DecoderFallbackException) { version = 0; return true; }
        }

        private static void Inventory(string root, string directory, List<string> files, List<string> directories)
        {
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                if (directory == root && string.Equals(Path.GetFileName(entry), DirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
                RejectLink(entry);
                string relative = Path.GetRelativePath(root, entry);
                if (Directory.Exists(entry)) { directories.Add(relative); Inventory(root, entry, files, directories); }
                else files.Add(relative);
            }
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("数据目录包含符号链接，无法安全自动备份。请先单独备份并检查：" + path);
        }

        private static string Child(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(root, relative));
            if (!result.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("备份路径超出数据目录。");
            return result;
        }

        private static string Hash(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create()) return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
