using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace CornerTodo.MacDesktop;

public sealed class App : Application
{
    private TodoWindow? window;
    private MacWindowIntegration? native;
    private TrayIcon? tray;
    private DispatcherTimer? restoreTimer;
    private IClassicDesktopStyleApplicationLifetime? lifetime;
    private bool exiting;
    private bool cleaned;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light; Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            lifetime = desktop; desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new TodoStore(Program.Options.DataDirectory);
            var state = Program.Options.Preview ? Program.PreviewState() : store.Load();
            var session = new TodoSession(store, state, Program.Options.Preview);
            window = new TodoWindow(session, Program.Options.DataDirectory, () => RequestExit());
            window.PreviewMode = Program.Options.Preview;
            native = new MacWindowIntegration(window, message => window.ShowStatus(message));
            desktop.MainWindow = window;
            window.Closing += (_, args) => { if (!exiting) { args.Cancel = true; RequestExit(); } };
            window.Show();
            if (!string.IsNullOrEmpty(store.LoadWarning)) window.ShowStatus(store.LoadWarning);
            SetupMenus();
            restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            restoreTimer.Tick += (_, _) => {
                if (!exiting && Program.Instance?.TryTakeRestoreRequest(window.IsBusy) == true) Restore();
            };
            restoreTimer.Start();
            Dispatcher.UIThread.UnhandledException += OnUnhandled;
            desktop.Exit += (_, _) => Cleanup();
            if (Program.Options.SmokeTest || Program.Options.RenderDirectory != null)
                DispatcherTimer.RunOnce(async () => {
                    try
                    {
                        await PreviewChecks.RunAsync(window, session, Program.Options.RenderDirectory);
                        Console.WriteLine("PASS: CornerTodo Avalonia auxiliary UI smoke checks; native macOS behavior not verified on Windows.");
                        // Exercise Close -> Closing -> guarded save -> Shutdown,
                        // rather than bypass the same route used by the user.
                        window.Close();
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); exiting = true; Cleanup(); desktop.Shutdown(1); }
                }, TimeSpan.FromMilliseconds(600));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void SetupMenus()
    {
        using var stream = AssetLoader.Open(new Uri("avares://CornerTodo.MacDesktop/Assets/CornerTodo.png"));
        tray = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "CornerTodo · 桌面待办", Menu = CreateMenu(), IsVisible = true };
        tray.Clicked += (_, _) => Restore();
        var appMenu = new NativeMenu(); appMenu.Items.Add(new NativeMenuItem("CornerTodo") { Menu = CreateMenu() });
        NativeMenu.SetMenu(this, appMenu);
    }

    private NativeMenu CreateMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("显示待办") { Command = new DelegateCommand(Restore) });
        menu.Items.Add(new NativeMenuItem("恢复右上角位置") { Command = new DelegateCommand(Restore) });
        menu.Items.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem("退出 CornerTodo") { Command = new DelegateCommand(() => RequestExit()) };
        if (OperatingSystem.IsMacOS()) quit.Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta);
        menu.Items.Add(quit);
        return menu;
    }

    private void Restore()
    {
        if (exiting || window == null) return;
        if (window.IsBusy) { window.ShowStatus("请先完成或取消当前编辑，再恢复面板位置。"); return; }
        window.RestorePanel(); native?.Apply();
    }

    private void RequestExit()
    {
        if (exiting || lifetime == null || window == null) return;
        if (!window.SaveBeforeExit()) return;
        exiting = true; Cleanup(); lifetime.Shutdown(0);
    }

    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true; Program.Log(args.Exception);
        window?.ShowStatus("操作未能完成，请重试；原有任务不会因为这次操作而清空。");
    }

    private void Cleanup()
    {
        if (cleaned) return;
        cleaned = true;
        restoreTimer?.Stop(); window?.StopTimers(); native?.Dispose(); tray?.Dispose();
        Dispatcher.UIThread.UnhandledException -= OnUnhandled;
    }

    private sealed class DelegateCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
