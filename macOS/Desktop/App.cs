using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CornerTodo.MacCore;

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
    private string? startupBackup;
    private string compatibilityNotice = "";
    private TodoSession? activeSession;

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
            TodoState state;
            try {
                if (!Program.Options.Preview) startupBackup = MacUpgradeBackup.CreateBeforeLoad(Program.Options.DataDirectory);
                state = Program.Options.Preview ? Program.PreviewState() : store.Load();
                compatibilityNotice = MacCompatibilityNotice.ForState(state);
            }
            catch (Exception error) {
                // The pre-load guard has not authorized any mutation of this
                // profile yet, including appending an existing log file.
                ShowStartupFailure(desktop, error);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var session = new TodoSession(store, state, Program.Options.Preview);
            activeSession = session;
            window = new TodoWindow(session, Program.Options.DataDirectory, () => RequestExit());
            window.PreviewMode = Program.Options.Preview;
            native = new MacWindowIntegration(window, message => window.ShowStatus(message));
            desktop.MainWindow = window;
            window.Closing += (_, args) => { if (!exiting) { args.Cancel = true; RequestExit(); } };
            window.Show();
            var notices = new List<string>();
            if (!string.IsNullOrEmpty(store.LoadWarning)) notices.Add(store.LoadWarning);
            if (!string.IsNullOrEmpty(native.LastError)) notices.Add("实验性桌面层级未启用，可从菜单栏找回面板。");
            if (startupBackup != null) notices.Add("升级前备份已保存，可在“关于 CornerTodo”查看位置。");
            if (compatibilityNotice.Length > 0) notices.Add(compatibilityNotice);
            if (notices.Count > 0) window.ShowStatus(string.Join("\n", notices));
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
        menu.Items.Add(new NativeMenuItem("关于 CornerTodo…") { Command = new DelegateCommand(ShowAbout) });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("显示待办") { Command = new DelegateCommand(Restore) });
        menu.Items.Add(new NativeMenuItem("恢复右上角位置") { Command = new DelegateCommand(Restore) });
        menu.Items.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem("退出 CornerTodo") { Command = new DelegateCommand(() => RequestExit()) };
        if (OperatingSystem.IsMacOS()) quit.Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta);
        menu.Items.Add(quit);
        return menu;
    }

    private async void ShowAbout()
    {
        if (exiting || window == null) return;
        if (window.IsBusy) { window.ShowStatus("请先完成或取消当前编辑，再查看版本信息。"); return; }
        var dialog = new Window { Title = "关于 CornerTodo", Width = 560, Height = 540,
            MinWidth = 300, MinHeight = 280, ShowInTaskbar = false, CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Style(dialog);
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        root.Children.Add(Ui.Text("CornerTodo · Mac 内测版", 23, true));
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(0, 15, 0, 10) };
        string feedback = Program.FeedbackInformation(Program.Options.DataDirectory);
        var info = Ui.Input("可复制的版本与设备信息", feedback); info.IsReadOnly = true; info.AcceptsReturn = true;
        info.TextWrapping = Avalonia.Media.TextWrapping.Wrap; info.MinHeight = 210; fields.Children.Add(info);
        fields.Children.Add(Ui.Text("本轮支持基础待办、无截止日期和外观改进；重复生成、到期提醒、悬浮显示及登录自启尚未接入。", 12, brush: Ui.Muted));
        string currentNotice = activeSession == null ? compatibilityNotice : MacCompatibilityNotice.ForState(activeSession.Snapshot());
        if (currentNotice.Length > 0) fields.Children.Add(Ui.Text(currentNotice, 12, brush: Ui.Warning));
        fields.Children.Add(Ui.Text(startupBackup == null ? "升级备份目录：\n" + Path.Combine(Program.Options.DataDirectory, MacUpgradeBackup.DirectoryName)
            : "本次升级前备份：\n" + startupBackup, 12, brush: Ui.Muted));
        var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var status = Ui.Text("复制内容包含本机数据目录，可检查后再分享。", 12, brush: Ui.Muted);
        Grid.SetRow(status, 2); root.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Ui.Button("复制反馈信息", async () => {
            try {
                var clipboard = TopLevel.GetTopLevel(dialog)?.Clipboard;
                if (clipboard == null) { status.Text = "剪贴板暂不可用，可选中文字手动复制。"; return; }
                await clipboard.SetTextAsync(feedback); status.Text = "版本与设备信息已复制，未包含待办内容。";
            }
            catch (Exception error) { status.Text = "复制失败，可选中文字手动复制。"; Program.Log(error); }
        }));
        buttons.Children.Add(Ui.Button("关闭", () => dialog.Close(), true));
        Grid.SetRow(buttons, 3); root.Children.Add(buttons); dialog.Content = root;
        try { await window.ShowAuxiliaryDialog(dialog); }
        catch (Exception error) { window.ShowStatus("版本信息窗口未能打开，可重试。" ); Program.Log(error); }
    }

    private void ShowStartupFailure(IClassicDesktopStyleApplicationLifetime desktop, Exception error)
    {
        var failure = new Window { Title = "CornerTodo 启动未完成", Width = 540, Height = 390,
            MinWidth = 300, MinHeight = 240, ShowInTaskbar = true, CanResize = true };
        Ui.Style(failure);
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(Ui.Text("待办尚未打开", 23, true));
        var message = Ui.Text("读取前检查或升级备份未能完成。程序已停止打开待办，请保留原数据目录和已生成的升级备份。\n\n"
            + error.Message + "\n\n数据目录：\n" + Program.Options.DataDirectory + "\n\n请保留原目录，处理提示的问题后重新打开。", 14);
        var scroll = new ScrollViewer { Content = message, Margin = new Thickness(0, 15, 0, 15),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var close = Ui.Button("退出", () => { exiting = true; Cleanup(); desktop.Shutdown(1); }, true);
        close.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetRow(close, 2); root.Children.Add(close);
        failure.Content = root; desktop.MainWindow = failure;
        failure.Closed += (_, _) => { if (!exiting) { exiting = true; Cleanup(); desktop.Shutdown(1); } };
        failure.Show();
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
