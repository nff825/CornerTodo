using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace CornerTodo
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            try {
                ApplicationComposition.Run(LaunchOptions.Parse(args, AppDomain.CurrentDomain.BaseDirectory));
            }
            catch (Exception ex) {
                Diagnostics.Log("Startup failed: " + ex);
                MessageBox.Show("无法启动桌面待办：\n" + ex.Message, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
