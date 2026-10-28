using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CornerTodo
{
    /// <summary>Coordinates views, task commands and the Windows application integration.</summary>
    internal sealed class MainWindow : Window, IApplicationWindow
    {
        private readonly TodoStore store;
        private readonly TodoSession session;
        private TodoState state;
        private readonly bool windowed;
        private readonly bool preview;
        private readonly IStartupManager startupManager;
        private readonly TodoPanelView panel;
        private readonly PanelController interaction;
        private readonly ApplicationInteraction applicationInteraction;
        private readonly DesktopAttachmentController desktopAttachment;
        private readonly DispatcherTimer saveTimer;
        private readonly DispatcherTimer refreshTimer;
        private bool menuOpen;
        private bool dialogOpen;
        private bool deferredRender;
        private bool resumeQueued;
        private AppearanceSettings appearancePreview;
        private string dateStamp;
        private bool disposed;
        private DispatcherOperation resumeOperation;
        private const string DesktopPendingMessage = "暂时无法连接桌面层。当前以普通窗口显示。";
        public bool HostLost { get; set; }
        public event EventHandler HandleLost;
        internal TodoPanelView Panel { get { return panel; } }
        internal PanelController Interaction { get { return interaction; } }

        public MainWindow(TodoStore store, TodoState state, bool windowed, bool preview)
            : this(store, state, windowed, preview, null, null) { }

        internal MainWindow(TodoStore store, TodoState state, bool windowed, bool preview, IStartupManager startupManager)
            : this(store, state, windowed, preview, startupManager, null) { }

        internal MainWindow(TodoStore store, TodoState state, bool windowed, bool preview,
            IStartupManager startupManager, IPanelHost panelHost)
            : this(store, new TodoSession(store, state, preview), windowed, preview,
                startupManager ?? new UnavailableStartupManager(null), panelHost, new ApplicationInteraction()) { }

        internal MainWindow(TodoStore store, TodoSession session, bool windowed, bool preview,
            IStartupManager startupManager, IPanelHost panelHost, ApplicationInteraction applicationInteraction)
        {
            this.store = store; this.windowed = windowed; this.preview = preview;
            if (session == null) throw new ArgumentNullException("session");
            if (startupManager == null) throw new ArgumentNullException("startupManager");
            if (applicationInteraction == null) throw new ArgumentNullException("applicationInteraction");
            this.session = session; this.startupManager = startupManager;
            this.applicationInteraction = applicationInteraction;
            this.state = session.Snapshot();
            Title = "桌面待办"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = windowed;
            ShowActivated = windowed; Topmost = false;
            Width = this.state.Settings.Width; Height = this.state.Settings.Height;
            FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = Theme.Ink;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            panel = new TodoPanelView(new TodoViewActions {
                Add = delegate { Edit(null); }, ToggleCollapsed = TogglePanel, ShowMenu = ShowMenu,
                ToggleCompletedSection = delegate {
                    Execute(delegate { session.SetCompletedCollapsed(!this.state.Settings.CompletedCollapsed); }, "保存失败，修改尚未保存。", true);
                },
                SetCompleted = delegate(TodoItem item, bool completed) {
                    return Execute(delegate { session.SetCompleted(item.Id, completed); }, "保存失败，修改尚未保存。", true);
                },
                Edit = Edit, EditNotes = delegate(TodoItem item) { EditTask(item, true); },
                SetSortMode = delegate(TaskSortMode mode) {
                    return Execute(delegate { session.SetSortMode(mode); }, "排序方式未保存。", true);
                },
                Delete = Delete, TrackMenu = TrackMenu
            });
            Content = panel.Surface;
            interaction = new PanelController(this, panel.Header, panel.Grip, panelHost ?? new DesktopPanelHost(this),
                delegate { return menuOpen || dialogOpen || applicationInteraction.IsBusy; },
                delegate { return HostLost; }, ScheduleSave, ResumeAttention);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveGeometry(); };
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            refreshTimer.Tick += delegate {
                if (dateStamp != DateTime.Now.ToString("yyyyMMddHHmmss")) {
                    if (InteractionBlocked) {
                        deferredRender = true;
                        panel.UpdateHeader(this.state, DateTime.Now, preview);
                        panel.UpdateTimes(DateTime.Now);
                    }
                    else Render();
                }
                else UpdateExpansion(false, false);
            };
            desktopAttachment = new DesktopAttachmentController(this, windowed, DesktopPendingChanged, delegate { return HostLost; });
            desktopAttachment.HandleLost += DesktopHandleLost;
            Loaded += LoadedWindow;
            Closed += ClosedWindow;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Edit(null); e.Handled = true; }
            };
            panel.Surface.ContextMenu = BuildMenu();
            Render();
        }

        private void LoadedWindow(object sender, RoutedEventArgs e)
        {
            Diagnostics.Log("Loaded main window");
            interaction.Initialize(state.Settings);
            panel.UpdateCollapsed(state, DateTime.Now);
            UpdateExpansion(true, true); refreshTimer.Start(); desktopAttachment.Start();
            if (!String.IsNullOrEmpty(store.LoadWarning)) {
                ShowStatus("已恢复数据，请查看提示。");
                ShowModal(store.LoadWarning, "待办数据恢复", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
            }
        }

        private void DesktopPendingChanged(bool pending)
        {
            if (disposed) return;
            if (pending) ShowStatus(DesktopPendingMessage);
            else if (panel.StatusText.Text == DesktopPendingMessage) panel.StatusText.Visibility = Visibility.Collapsed;
        }

        private void DesktopHandleLost(object sender, EventArgs e)
        {
            EventHandler handler = HandleLost;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void ClosedWindow(object sender, EventArgs e) { Dispose(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Loaded -= LoadedWindow; Closed -= ClosedWindow;
            desktopAttachment.HandleLost -= DesktopHandleLost;
            desktopAttachment.Dispose(); interaction.Dispose();
            saveTimer.Stop(); refreshTimer.Stop();
            if (resumeOperation != null) { resumeOperation.Abort(); resumeOperation = null; }
        }

        public void RestoreToDesktop()
        {
            if (disposed || HostLost) return;
            Diagnostics.Log("Existing instance restore requested");
            if (state.Settings.Collapsed) TogglePanel();
            Show(); WindowState = WindowState.Normal; ResetPosition();
            if (!windowed) desktopAttachment.AttachNow();
            InvalidateVisual(); UpdateLayout();
        }

        private void Render()
        {
            DateTime now = DateTime.Now;
            dateStamp = now.ToString("yyyyMMddHHmmss"); deferredRender = false;
            panel.Render(state, now, preview, appearancePreview);
            Foreground = panel.Palette.Ink;
            UpdateExpansion(false, false);
        }

        private void ShowMenu()
        {
            ContextMenu menu = BuildMenu();
            menu.PlacementTarget = panel.MoreButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var loginStartup = new MenuItem { Header = "开机自启（登录后）", IsCheckable = true, Tag = "login-startup" };
            var repairStartup = new MenuItem { Header = "更新自启位置为当前程序", Tag = "repair-startup", Visibility = Visibility.Collapsed };
            loginStartup.Click += delegate {
                bool requested = loginStartup.IsChecked;
                if (!TrySetStartup(requested)) loginStartup.IsChecked = !requested;
                RefreshStartupMenu(loginStartup, repairStartup);
            };
            repairStartup.Click += delegate { TrySetStartup(true); RefreshStartupMenu(loginStartup, repairStartup); };
            menu.Opened += delegate {
                foreach (var menuItem in menu.Items.OfType<MenuItem>()) {
                    if (Object.Equals(menuItem.Tag, "auto-expand")) menuItem.IsChecked = state.Settings.AutoExpand;
                    if (Object.Equals(menuItem.Tag, "completed-toggle")) menuItem.Header = state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成";
                }
                RefreshStartupMenu(loginStartup, repairStartup);
            };
            var create = new MenuItem { Header = "添加待办" }; create.Click += delegate { Edit(null); }; menu.Items.Add(create);
            var showCompleted = new MenuItem { Header = state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成", Tag = "completed-toggle" };
            showCompleted.Click += delegate {
                Execute(delegate { session.SetCompletedCollapsed(!state.Settings.CompletedCollapsed); }, "保存失败，修改尚未保存。", true);
            }; menu.Items.Add(showCompleted);
            menu.Items.Add(new Separator());
            var appearance = new MenuItem { Header = "外观设置…" }; appearance.Click += delegate { EditAppearance(); }; menu.Items.Add(appearance);
            var readable = new MenuItem { Header = "恢复清晰浅色" }; readable.Click += delegate { TrySaveAppearance(AppearanceSettings.CreateLight()); }; menu.Items.Add(readable);
            menu.Items.Add(new Separator());
            var automatic = new MenuItem { Header = "随截止时间扩大", IsCheckable = true, IsChecked = state.Settings.AutoExpand, Tag = "auto-expand" };
            automatic.Click += delegate {
                if (Execute(delegate { session.SetAutoExpand(automatic.IsChecked); }, "保存失败，修改尚未保存。", true))
                    UpdateExpansion(true, true);
            };
            menu.Items.Add(automatic);
            menu.Items.Add(loginStartup); menu.Items.Add(repairStartup);
            var reset = new MenuItem { Header = "恢复右上角位置" }; reset.Click += delegate { ResetPosition(); }; menu.Items.Add(reset);
            var help = new MenuItem { Header = "使用说明" }; help.Click += delegate {
                ShowModal("点击圆圈切换完成状态；双击事项或右键编辑。\n有备注的事项显示备注图标，点击即可查看或修改。\n细线表示已用时间，鼠标悬停可看百分比；满格表示到期，不代表完成。\n优先级可选高、中、低；列表顶部可切换截止时间优先或优先级优先。\n拖动标题调整位置；关闭菜单中的‘随截止时间扩大’后，可调整常态大小。\n紧急事项：距截止不超过6小时；逾期事项持续保持最大面板。\n自动扩展以右上角为锚点，最大占桌面宽度的一半、高度约72%。\n仅日期的事项到当天结束后才逾期。\n开机自启默认关闭，可在菜单自行开启，仅当前账号登录后启动。\n退出仅关闭本次运行；移动程序后请更新自启位置。\n\n数据自动保存在：\n" + store.DataFilePath, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
            }; menu.Items.Add(help);
            menu.Items.Add(new Separator()); var quit = new MenuItem { Header = "退出待办" }; quit.Click += delegate { applicationInteraction.RequestExit(); }; menu.Items.Add(quit);
            TrackMenu(menu); return menu;
        }

        private void RefreshStartupMenu(MenuItem enabled, MenuItem repair)
        {
            repair.Visibility = Visibility.Collapsed;
            if (preview) {
                enabled.IsChecked = false; enabled.IsEnabled = false;
                enabled.ToolTip = "示例模式不会更改 Windows 自启动设置。";
                return;
            }
            try {
                StartupRegistration registration = startupManager.Inspect();
                enabled.IsChecked = registration.Exists && registration.IsManaged;
                enabled.IsEnabled = !registration.Exists || registration.IsManaged;
                if (registration.Exists && !registration.IsManaged)
                    enabled.ToolTip = "自启动文件被其他内容占用，未作修改。";
                else if (registration.Exists && !registration.MatchesCurrent) {
                    enabled.ToolTip = "已登记另一位置。可关闭此项，或更新为当前程序；不会自动迁移。";
                    repair.Visibility = Visibility.Visible;
                }
                else enabled.ToolTip = "仅当前账号登录后启动；勾选表示快捷方式已登记。Windows 中禁用后需自行重新启用。";
            }
            catch (Exception ex) {
                enabled.IsEnabled = false;
                enabled.ToolTip = "无法检查开机自启：" + ex.Message;
            }
        }

        internal bool TrySetStartup(bool enabled)
        {
            if (preview) return false;
            try {
                if (enabled) startupManager.Enable(); else startupManager.Disable();
                ShowStatus(enabled ? "已登记当前位置，下次登录后由 Windows 启动。" : "开机自启已关闭，本次运行不受影响。");
                return true;
            }
            catch (Exception ex) {
                ShowStatus("开机自启设置未更改：" + ex.Message);
                return false;
            }
        }

        private void Edit(TodoItem item) { EditTask(item, false); }

        private void EditTask(TodoItem item, bool focusNotes)
        {
            if (disposed || applicationInteraction.IsBusy) return;
            applicationInteraction.EditorOpen = true; interaction.StopAnimation();
            try
            {
                var editor = new TaskEditor(item, focusNotes, delegate(TaskDraft draft) {
                    return Execute(delegate {
                        if (item == null) session.Add(draft);
                        else session.Edit(item.Id, draft);
                    }, "保存失败，输入仍保留在编辑窗口。", false);
                });
                Rect area = DesktopHost.WorkArea(this);
                editor.MinWidth = Math.Min(editor.MinWidth, Math.Max(1, area.Width - 24));
                editor.MinHeight = Math.Min(editor.MinHeight, Math.Max(1, area.Height - 24));
                editor.MaxWidth = Math.Max(editor.MinWidth, area.Width - 24);
                editor.MaxHeight = Math.Max(editor.MinHeight, area.Height - 24);
                editor.Width = Math.Min(editor.Width, editor.MaxWidth);
                editor.Height = Math.Min(editor.Height, editor.MaxHeight);
                editor.WindowStartupLocation = WindowStartupLocation.Manual;
                editor.Left = Math.Max(area.Left + 12, area.Right - editor.Width - 26);
                editor.Top = Math.Max(area.Top + 20, Math.Min(DesktopHost.GetBounds(this).Top + 55, area.Bottom - editor.Height - 20));
                if (editor.ShowDialog() == true)
                {
                    panel.StatusText.Visibility = Visibility.Collapsed;
                    Render();
                }
            }
            finally { applicationInteraction.EditorOpen = false; ResumeAttention(); }
        }

        private void EditAppearance()
        {
            if (disposed || applicationInteraction.IsBusy) return;
            applicationInteraction.EditorOpen = true; interaction.StopAnimation();
            try {
                var editor = new AppearanceEditor(state.Settings.Appearance, PreviewAppearance, TrySaveAppearance);
                Rect area = DesktopHost.WorkArea(this);
                editor.WindowStartupLocation = WindowStartupLocation.Manual;
                editor.MinWidth = Math.Min(editor.MinWidth, Math.Max(1, area.Width - 24));
                editor.MinHeight = Math.Min(editor.MinHeight, Math.Max(1, area.Height - 24));
                editor.MaxWidth = Math.Max(editor.MinWidth, area.Width - 24);
                editor.MaxHeight = Math.Max(editor.MinHeight, area.Height - 24);
                editor.Width = Math.Min(editor.Width, editor.MaxWidth);
                editor.Height = Math.Min(editor.Height, editor.MaxHeight);
                editor.Left = Math.Max(area.Left + 12, area.Right - editor.Width - 26);
                editor.Top = Math.Max(area.Top + 12, Math.Min(DesktopHost.GetBounds(this).Top + 35, area.Bottom - editor.Height - 12));
                editor.ShowDialog();
            }
            finally {
                try { CancelAppearancePreview(); }
                finally { applicationInteraction.EditorOpen = false; ResumeAttention(); }
            }
        }

        internal void PreviewAppearance(AppearanceSettings appearance)
        {
            appearancePreview = AppearanceSettings.Normalize(appearance);
            Render();
        }

        internal void CancelAppearancePreview()
        {
            appearancePreview = null;
            Render();
        }

        internal bool TrySaveAppearance(AppearanceSettings appearance)
        {
            bool saved = Execute(delegate { session.SetAppearance(appearance); }, "外观未保存，已恢复原来的外观。", false);
            appearancePreview = null;
            if (saved) panel.StatusText.Visibility = Visibility.Collapsed;
            Render();
            return saved;
        }

        private void Delete(TodoItem item)
        {
            string description = item.Text.Length > 100 ? item.Text.Substring(0, 100) + "…" : item.Text;
            if (ShowModal("删除这条待办？\n\n" + description, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Execute(delegate { session.Delete(item.Id); }, "保存失败，修改尚未保存。", true);
        }

        private bool Execute(Action operation, string failureMessage, bool render)
        {
            // Catch only the service operation. A rendering failure must not be
            // reported as a failed save after the service has already committed.
            try { operation(); }
            catch (Exception ex) { ShowStatus(failureMessage + ex.Message); return false; }
            state = session.Snapshot();
            if (render) { panel.StatusText.Visibility = Visibility.Collapsed; Render(); }
            return true;
        }
        private void ShowStatus(string text) { panel.StatusText.Text = text; panel.StatusText.Visibility = Visibility.Visible; panel.StatusText.ToolTip = text; }
        private void ScheduleSave() { if (disposed || !interaction.IsReady) return; saveTimer.Stop(); saveTimer.Start(); }
        public void SaveGeometry()
        {
            if (disposed || !interaction.IsReady || HostLost) return;
            Rect bounds = interaction.BaselineBounds;
            Execute(delegate { session.SaveGeometry(bounds.Left, bounds.Top, bounds.Width, bounds.Height); }, "位置尚未保存。", false);
        }
        public TodoState CurrentState { get { return session.Snapshot(); } }
        private void TogglePanel()
        {
            interaction.StopAnimation();
            if (!Execute(delegate { session.SetCollapsed(!state.Settings.Collapsed); }, "保存失败，修改尚未保存。", true)) return;
            UpdateExpansion(true, true); ScheduleSave();
        }
        private void ResetPosition()
        {
            interaction.ResetPosition(GrowthProgress(), state.Settings.Collapsed);
        }

        private bool InteractionBlocked { get { return interaction.IsInteractionBlocked; } }

        private MessageBoxResult ShowModal(string text, string title, MessageBoxButton buttons,
            MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            dialogOpen = true; interaction.StopAnimation();
            try {
                using (applicationInteraction.EnterModal()) return MessageBox.Show(text, title, buttons, icon, defaultResult);
            }
            finally { dialogOpen = false; ResumeAttention(); }
        }

        private void TrackMenu(ContextMenu menu)
        {
            menu.Opened += delegate { menuOpen = true; interaction.StopAnimation(); };
            menu.Closed += delegate { menuOpen = false; ResumeAttention(); };
        }

        private double GrowthProgress()
        {
            return state.Settings.AutoExpand ? TodoRules.Expansion(state, DateTime.Now) : 0;
        }

        private void UpdateExpansion(bool immediate, bool allowDuringInteraction)
        {
            interaction.UpdateExpansion(GrowthProgress(), state.Settings.Collapsed, immediate, allowDuringInteraction);
        }

        private void ResumeAttention()
        {
            if (disposed || !interaction.IsReady || HostLost || resumeQueued) return;
            resumeQueued = true;
            resumeOperation = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate {
                resumeOperation = null;
                resumeQueued = false;
                if (disposed || !interaction.IsReady || HostLost || InteractionBlocked) return;
                if (deferredRender || dateStamp != DateTime.Now.ToString("yyyyMMddHHmmss")) Render();
                else UpdateExpansion(false, false);
            }));
        }

    }
}
