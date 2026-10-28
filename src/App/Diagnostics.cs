using System;
using System.IO;
using System.Text;

namespace CornerTodo
{
    internal static class Diagnostics
    {
        private static string file;
        public static void Initialize(string directory)
        {
            try { Directory.CreateDirectory(directory); file = Path.Combine(directory, "CornerTodo.log");
                if (File.Exists(file) && new FileInfo(file).Length > 512000) File.Copy(file, file + ".previous", true);
                if (File.Exists(file) && new FileInfo(file).Length > 512000) File.WriteAllText(file, "");
            } catch { file = null; }
        }
        public static void Log(string message)
        {
            if (file == null) return;
            try { File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
    }
}
