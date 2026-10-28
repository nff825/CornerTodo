using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CornerTodo
{
    internal sealed class LaunchOptions
    {
        public string DataDirectory { get; private set; }
        public bool Startup { get; private set; }
        public bool Windowed { get; private set; }
        public bool Preview { get; private set; }

        public static LaunchOptions Parse(string[] args, string applicationDirectory)
        {
            if (args == null) throw new ArgumentNullException("args");
            var options = new LaunchOptions {
                DataDirectory = NormalizeDirectory(Path.Combine(NormalizeDirectory(applicationDirectory), "Data"))
            };
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data-dir")
                {
                    if (i + 1 >= args.Length || String.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("--data-dir 后需要指定有效的数据目录。");
                    options.DataDirectory = NormalizeDirectory(args[++i]);
                }
                else if (args[i] == "--startup") options.Startup = true;
                else if (args[i] == "--windowed") options.Windowed = true;
                else if (args[i] == "--preview") options.Preview = true;
                else throw new ArgumentException("无法识别启动参数：" + args[i]);
            }
            if (options.Preview) { options.Windowed = true; options.Startup = false; }
            return options;
        }

        public static string NormalizeDirectory(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("必须指定有效的目录。", "path");
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            while (full.Length > root.Length && (full[full.Length - 1] == Path.DirectorySeparatorChar || full[full.Length - 1] == Path.AltDirectorySeparatorChar))
                full = full.Substring(0, full.Length - 1);
            return full;
        }

        public static string GetInstanceKey(string dataDirectory, bool preview)
        {
            string identity = NormalizeDirectory(dataDirectory).ToUpperInvariant() + (preview ? "-preview" : "");
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 24);
        }

        // Windows process arguments double backslashes before a closing quote
        // and before literal quotes, including for a drive root such as D:\.
        public static string QuoteArgument(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            var result = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { backslashes++; continue; }
                if (character == '"') result.Append('\\', backslashes * 2 + 1).Append(character);
                else result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
            return result.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
