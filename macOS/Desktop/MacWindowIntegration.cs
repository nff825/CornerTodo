using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace CornerTodo.MacDesktop;

/// <summary>Experimental desktop-band policy. Only applies to the main panel, never editors.</summary>
internal sealed class MacWindowIntegration : IDisposable
{
    private readonly Window window;
    private readonly Action<string> warning;
    private bool disposed;
    internal bool Applied { get; private set; }
    internal string? LastError { get; private set; }

    internal MacWindowIntegration(Window window, Action<string> warning)
    {
        this.window = window; this.warning = warning;
        window.Topmost = false;
        window.Opened += OnOpened; window.Activated += OnActivated;
    }

    private void OnOpened(object? sender, EventArgs args) => Apply();
    private void OnActivated(object? sender, EventArgs args) => Apply();

    internal void Apply()
    {
        if (disposed || !OperatingSystem.IsMacOS()) return;
        try
        {
            var handle = window.TryGetPlatformHandle();
            IntPtr native = handle is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow :
                handle?.HandleDescriptor == "NSWindow" ? handle.Handle : IntPtr.Zero;
            if (native == IntPtr.Zero) throw new InvalidOperationException("未取得主面板的 NSWindow。");
            // kCGDesktopIconWindowLevelKey. Query the OS rather than hard-code its negative level.
            int desktopIcons = CGWindowLevelForKey(18);
            if (desktopIcons >= -100) throw new InvalidOperationException("系统返回了异常的桌面窗口层级。");
            SendLong(native, Selector("setLevel:"), (long)desktopIcons + 1);
            // canJoinAllSpaces | stationary | ignoresCycle | fullScreenNone.
            // No fullScreenAuxiliary/canJoinAllApplications overlay policy is requested.
            SendUnsigned(native, Selector("setCollectionBehavior:"), 1UL | 16UL | 64UL | 512UL);
            SendByte(native, Selector("setHidesOnDeactivate:"), 0);
            Applied = true; LastError = null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
            InvalidOperationException or PlatformNotSupportedException)
        {
            Applied = false; LastError = error.GetType().Name;
            warning("实验性桌面层级未能启用，当前按普通非置顶窗口显示；可从菜单栏找回。");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; window.Opened -= OnOpened; window.Activated -= OnActivated;
    }

    private static IntPtr Selector(string value) => sel_registerName(value);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGWindowLevelForKey(int key);
    [DllImport("/usr/lib/libobjc.A.dylib", CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendLong(IntPtr receiver, IntPtr selector, long value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendUnsigned(IntPtr receiver, IntPtr selector, ulong value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendByte(IntPtr receiver, IntPtr selector, byte value);
}
