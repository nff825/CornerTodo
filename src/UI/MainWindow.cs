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
        private readonly ReminderController reminders;
        private bool menuOpen;
        private bool dialogOpen;
        private bool deferredRender;
        private bool resumeQueued;
        private AppearanceSettings appearancePreview;
        private string dateStamp;
        private bool disposed;
        private DispatcherOperation resumeOperation;
        private DispatcherOperation recurrenceOperation;
        private bool missedRecurrencesOffered;
        private Window activeDialog;
        private const string DesktopPendingMessage = "暂时无法连接桌面层。当前以普通窗口显示。";
        public bool HostLost { get; set; }
        public event EventHandler HandleLost;
        internal TodoPanelView Panel { get { return panel; } }
        internal PanelController Interaction { get { return interaction; } }
        internal ReminderController Reminders { get { return reminders; } }

        public MainWindow(TodoStore store, TodoState state, bool windowed, bool preview)
            : this(store, state, windowed, preview, null, null) { }

        internal MainWindow(TodoStore store, TodoState state, bool windowed, bool preview, IStartupManager startupManager)
            : this(store, state, windowed, preview, startupManager, null) { }

        internal MainWindow(TodoStore store, TodoState state, bool windowed, bool preview,
            IStartupManager startupManager, IPanelHost panelHost)
            : this(store, new TodoSession(store, state, preview), windowed, preview,
                startupManager ?? new UnavailableStartupManager(null), panelHost, new ApplicationInteraction()) { }

        internal MainWindow(TodoStore store, TodoSession session, bool windowed, bool preview,
            IStartupManager startupManager, IPanelHost panelHost, ApplicationInteraction applicationInteraction,
            Func<DateTime> panelClock = null, IReminderPresenter reminderPresenter = null, IReminderTimer reminderTimer = null)
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
                Add = delegate { Edit(null); }, ToggleCollapsed = TogglePanel, ShowMenu = ShowMenu, RevealFromEdge = RevealFromEdge,
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
                Delete = Delete, TrackMenu = TrackMenu,
                EditRecurringRule = delegate(TodoItem item) { EditRecurringRule(item.RecurringRuleId, false); },
                SetRecurringPaused = delegate(TodoItem item, bool paused) { SetRecurringPaused(item.RecurringRuleId, paused); },
                StopRecurringRule = delegate(TodoItem item) { StopRecurringRule(item.RecurringRuleId); }
            });
            Content = panel.Surface;
            interaction = new PanelController(this, panel.Header, panel.Grip, panelHost ?? new DesktopPanelHost(this),
                delegate { return menuOpen || dialogOpen || applicationInteraction.IsBusy; },
                delegate { return HostLost; }, ScheduleSave, ResumeAttention, panelClock ?? delegate { return DateTime.UtcNow; });
            interaction.EdgeHiddenChanged += EdgeHiddenChanged;
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
                    else { ProcessRecurrences(false); Render(); }
                }
                else UpdateExpansion(false, false);
            };
            desktopAttachment = new DesktopAttachmentController(this, windowed, DesktopPendingChanged, delegate { return HostLost; });
            if (this.state.Settings.AlwaysOnTop) desktopAttachment.SetFloating(true);
            desktopAttachment.HandleLost += DesktopHandleLost;
            reminders = new ReminderController(delegate { return session.ClaimDueReminders(); }, session.Snapshot,
                delegate { return disposed || HostLost || menuOpen || dialogOpen || applicationInteraction.IsBusy; },
                ReminderStateChanged, delegate(TodoItem item) { Edit(item); },
                delegate(TodoItem item) { return Execute(delegate { session.SetCompleted(item.Id, true); }, "完成状态未保存。", true); },
                delegate(Exception error) { Diagnostics.Log("Reminder delivery failed: " + error); ShowStatus("提醒暂未显示，可稍后重试。" + error.Message); },
                reminderPresenter ?? new ReminderWindowPresenter(delegate { return DesktopHost.WorkArea(this); }, delegate { return DateTime.Now; }),
                reminderTimer ?? new DispatcherReminderTimer(Dispatcher));
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
            QueueRecurrenceCheck();
            if (!preview) reminders.Start();
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
            if (reminders != null) reminders.Refresh();
        }

        private void ClosedWindow(object sender, EventArgs e) { Dispose(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            reminders.Dispose();
            Loaded -= LoadedWindow; Closed -= ClosedWindow;
            desktopAttachment.HandleLost -= DesktopHandleLost;
            desktopAttachment.Dispose();
            interaction.EdgeHiddenChanged -= EdgeHiddenChanged;
            interaction.Dispose();
            saveTimer.Stop(); refreshTimer.Stop();
            if (resumeOperation != null) { resumeOperation.Abort(); resumeOperation = null; }
            if (recurrenceOperation != null) { recurrenceOperation.Abort(); recurrenceOperation = null; }
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
            if (reminders != null) reminders.Refresh();
        }

        private void ReminderStateChanged()
        {
            if (disposed || HostLost) return;
            state = session.Snapshot(); deferredRender = true;
            if (!InteractionBlocked) Render();
            else { panel.UpdateHeader(state, DateTime.Now, preview); panel.UpdateTimes(DateTime.Now); }
        }

        private void ShowMenu()
        {
            ContextMenu menu = BuildMenu();
            menu.PlacementTarget = panel.MoreButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
        }

        private void EdgeHiddenChanged(bool hidden) { if (!disposed) panel.SetEdgeHidden(hidden); }

        private void RevealFromEdge()
        {
            if (disposed || HostLost || !interaction.IsEdgeHidden) return;
            interaction.RevealFromEdge(GrowthProgress(), state.Settings.Collapsed);
        }

        internal bool TrySetEdgeAutoHide(bool enabled)
        {
            if (disposed || HostLost) return false;
            if (!Execute(delegate { session.SetEdgeAutoHide(enabled); }, "贴边收起设置未保存。", false)) return false;
            interaction.SetEdgeAutoHide(enabled);
            Render();
            return true;
        }

        internal bool TrySetAlwaysOnTop(bool enabled)
        {
            if (disposed || HostLost || applicationInteraction.IsBusy || dialogOpen) return false;
            if (state.Settings.AlwaysOnTop == enabled) return true;
            interaction.StopAnimation();
            bool previous = state.Settings.AlwaysOnTop;
            try { desktopAttachment.SetFloating(enabled); }
            catch (Exception error) { ShowStatus("显示方式未更改：" + error.Message); return false; }
            if (!Execute(delegate { session.SetAlwaysOnTop(enabled); }, "显示方式未保存，已恢复原设置。", false)) {
                try { desktopAttachment.SetFloating(previous); }
                catch (Exception error) { ShowStatus("显示方式未保存，恢复窗口显示时发生错误：" + error.Message); }
                return false;
            }
            panel.StatusText.Visibility = Visibility.Collapsed;
            Render();
            // Use the saved ordinary-size baseline, never the currently enlarged
            // rectangle or edge capsule, when changing the display layer.
            UpdateExpansion(true, true);
            return true;
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
                    if (Object.Equals(menuItem.Tag, "auto-expand")) menuItem.IsChecked = state.Settings.EffectiveAutoExpand;
                    if (Object.Equals(menuItem.Tag, "edge-auto-hide")) menuItem.IsChecked = state.Settings.EdgeAutoHide;
                    if (Object.Equals(menuItem.Tag, "completed-toggle")) menuItem.Header = state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成";
                }
                RefreshStartupMenu(loginStartup, repairStartup);
            };
            var create = new MenuItem { Header = "添加待办" }; create.Click += delegate { Edit(null); }; menu.Items.Add(create);
            var recurring = new MenuItem { Header = "管理重复任务…", Tag = "recurring-management" };
            recurring.Click += delegate { ManageRecurringTasks(); }; menu.Items.Add(recurring);
            var catchUp = new MenuItem { Header = "处理遗漏周期…", Tag = "recurring-catch-up" };
            catchUp.Click += delegate { ResolveMissedRecurrences(false); }; menu.Items.Add(catchUp);
            var showCompleted = new MenuItem { Header = state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成", Tag = "completed-toggle" };
            showCompleted.Click += delegate {
                Execute(delegate { session.SetCompletedCollapsed(!state.Settings.CompletedCollapsed); }, "保存失败，修改尚未保存。", true);
            }; menu.Items.Add(showCompleted);
            menu.Items.Add(new Separator());
            var appearance = new MenuItem { Header = "外观设置…" }; appearance.Click += delegate { EditAppearance(); }; menu.Items.Add(appearance);
            var readable = new MenuItem { Header = "恢复清晰浅色" }; readable.Click += delegate { TrySaveAppearance(AppearanceSettings.CreateLight()); }; menu.Items.Add(readable);
            menu.Items.Add(new Separator());
            var display = new MenuItem { Header = "显示方式", Tag = "display-mode" };
            var desktopMode = new MenuItem { Header = "仅在桌面显示（默认）", IsCheckable = true,
                IsChecked = !state.Settings.AlwaysOnTop, Tag = "display-desktop" };
            var floatingMode = new MenuItem { Header = "悬浮在其他窗口上方", IsCheckable = true,
                IsChecked = state.Settings.AlwaysOnTop, Tag = "display-floating" };
            Action refreshDisplay = delegate {
                desktopMode.IsChecked = !state.Settings.AlwaysOnTop;
                floatingMode.IsChecked = state.Settings.AlwaysOnTop;
            };
            desktopMode.Click += delegate { TrySetAlwaysOnTop(false); refreshDisplay(); };
            floatingMode.Click += delegate { TrySetAlwaysOnTop(true); refreshDisplay(); };
            display.SubmenuOpened += delegate { refreshDisplay(); };
            menu.Opened += delegate { refreshDisplay(); };
            display.Items.Add(desktopMode); display.Items.Add(floatingMode); menu.Items.Add(display);
            var automatic = new MenuItem { Header = "随截止时间扩大", IsCheckable = true, IsChecked = state.Settings.EffectiveAutoExpand, Tag = "auto-expand",
                ToolTip = "桌面与悬浮模式分别保存。悬浮模式默认关闭，使用常态尺寸。" };
            automatic.Click += delegate {
                if (Execute(delegate { session.SetAutoExpand(automatic.IsChecked); }, "保存失败，修改尚未保存。", true))
                    UpdateExpansion(true, true);
                automatic.IsChecked = state.Settings.EffectiveAutoExpand;
            };
            menu.Items.Add(automatic);
            var edgeAutoHide = new MenuItem { Header = "贴边自动收起", IsCheckable = true,
                IsChecked = state.Settings.EdgeAutoHide, Tag = "edge-auto-hide",
                ToolTip = "默认关闭。开启后拖至主屏右侧贴边；鼠标离开后收起，点击胶囊展开。" };
            edgeAutoHide.Click += delegate {
                if (!TrySetEdgeAutoHide(edgeAutoHide.IsChecked)) edgeAutoHide.IsChecked = state.Settings.EdgeAutoHide;
            };
            menu.Items.Add(edgeAutoHide);
            menu.Items.Add(loginStartup); menu.Items.Add(repairStartup);
            var reset = new MenuItem { Header = "恢复右上角位置" }; reset.Click += delegate { ResetPosition(); }; menu.Items.Add(reset);
            var help = new MenuItem { Header = "使用说明" }; help.Click += delegate {
                ShowModal("点击圆圈切换完成状态；双击事项或右键编辑。\n有备注的事项显示备注图标，点击即可查看或修改。\n具体时刻可分别点选小时和分钟，也支持键盘输入，使用24小时制。\n细线表示已用时间，鼠标悬停可看百分比；满格表示到期，不代表完成。\n优先级可选高、中、低；列表顶部可切换截止时间优先或优先级优先。\n显示方式默认仅在桌面，可从菜单自行开启悬浮在其他窗口上方。\n桌面与悬浮分别保存‘随截止时间扩大’设置，悬浮默认以常态尺寸显示。\n拖动标题调整位置；关闭当前模式的自动扩大后，可调整常态大小。\n贴边自动收起默认关闭：开启后拖至主屏右侧，鼠标离开后收起。\n点击胶囊展开；编辑或菜单打开时不收起；拖离边缘可取消贴边。\n胶囊沿用当前显示方式；收起期间到期不会自动弹出。\n可通过‘恢复右上角位置’或再次双击程序找回，保持已选显示方式。\n紧急事项：距截止不超过6小时；开启自动扩大时，逾期事项展开保持最大面板。\n自动扩展以右上角为锚点，最大占桌面宽度的一半、高度约72%。\n仅日期的事项到当天结束后才逾期。\n开机自启默认关闭，可在菜单自行开启，仅当前账号登录后启动。\n退出仅关闭本次运行；移动程序后请更新自启位置。\n\n数据自动保存在：\n" + store.DataFilePath, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
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
            RevealFromEdge();
            try
            {
                bool savedRecurrence = false;
                bool futureOnly = false;
                var editor = new TaskEditor(item, focusNotes, delegate(TaskDraft draft) {
                    bool saved = Execute(delegate {
                        if (item == null) futureOnly = session.Add(draft) == null;
                        else session.Edit(item.Id, draft);
                    }, "保存失败，输入仍保留在编辑窗口。", false);
                    if (saved) savedRecurrence = draft.Recurrence != null;
                    return saved;
                });
                editor.SaveErrorProvider = delegate { return panel.StatusText.Text; };
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
                LimitChildHeightAtPosition(editor, area);
                if (ShowChildDialog(editor) == true)
                {
                    if (savedRecurrence) ShowStatus(futureOnly ? "重复规则已保存，将按开始日期生成；可在“管理重复任务”查看。" : "重复规则已保存，已有事项与完成历史保留。");
                    else panel.StatusText.Visibility = Visibility.Collapsed;
                    Render();
                }
            }
            finally { applicationInteraction.EditorOpen = false; ResumeAttention(); }
        }

        private void FitRecurringWindow(Window child)
        {
            Rect area = DesktopHost.WorkArea(this);
            child.MinWidth = Math.Min(child.MinWidth, Math.Max(1, area.Width - 24));
            child.MinHeight = Math.Min(child.MinHeight, Math.Max(1, area.Height - 24));
            child.MaxWidth = Math.Max(child.MinWidth, area.Width - 24); child.MaxHeight = Math.Max(child.MinHeight, area.Height - 24);
            child.Width = Math.Min(child.Width, child.MaxWidth); child.Height = Math.Min(child.Height, child.MaxHeight);
            child.WindowStartupLocation = WindowStartupLocation.Manual;
            child.Left = Math.Max(area.Left + 12, area.Right - child.Width - 26);
            child.Top = Math.Max(area.Top + 12, Math.Min(DesktopHost.GetBounds(this).Top + 35, area.Bottom - child.Height - 12));
            LimitChildHeightAtPosition(child, area);
        }

        internal static void LimitChildHeightAtPosition(Window child, Rect workArea)
        {
            double available = Math.Max(1, workArea.Bottom - child.Top - 12);
            child.MinHeight = Math.Min(child.MinHeight, available);
            child.MaxHeight = Math.Min(child.MaxHeight, available);
            child.Height = Math.Min(child.Height, child.MaxHeight);
        }

        private void ManageRecurringTasks()
        {
            if (disposed || applicationInteraction.IsBusy) return;
            applicationInteraction.EditorOpen = true; interaction.StopAnimation(); RevealFromEdge();
            try {
                var manager = new RecurringTasksWindow(session.Snapshot,
                    delegate(string id) { EditRecurringRule(id, true); }, SetRecurringPaused, StopRecurringRule,
                    delegate { ResolveMissedRecurrences(true); }, null, session.GetRecurrenceSummary);
                FitRecurringWindow(manager); ShowChildDialog(manager);
                state = session.Snapshot(); Render();
            }
            finally { applicationInteraction.EditorOpen = false; ResumeAttention(); }
        }

        private void EditRecurringRule(string id, bool nested)
        {
            if (disposed || (!nested && applicationInteraction.IsBusy)) return;
            RecurringTaskRule rule = session.GetRecurringRule(id);
            if (rule.IsStopped) { ShowStatus("此规则已经停止，已有事项仍可单独编辑。"); return; }
            if (session.GetRecurrenceSummary(id).MissedCount > 0) {
                if (ShowModal("这条规则还有未处理的遗漏周期。请先处理遗漏，再修改后续规则，避免长时间编辑后才被要求返回。\n\n现在打开遗漏周期处理？", "先处理此规则的遗漏周期", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes) == MessageBoxResult.Yes)
                    ResolveMissedRecurrences(nested);
                if (session.GetRecurrenceSummary(id).MissedCount > 0) return;
                rule = session.GetRecurringRule(id);
            }
            bool previousEditor = applicationInteraction.EditorOpen;
            applicationInteraction.EditorOpen = true; interaction.StopAnimation(); RevealFromEdge();
            try {
                var template = new TodoItem { Id = rule.Id, Text = rule.Text, Notes = rule.Notes, Priority = rule.Priority,
                    HasDeadline = rule.HasDeadline, HasTime = rule.HasTime,
                    ReminderEnabled = rule.ReminderEnabled, ReminderMinutesBefore = rule.ReminderMinutesBefore,
                    DueDate = rule.HasDeadline ? rule.Recurrence.StartDate.AddDays(rule.DeadlineOffsetDays).AddTicks(rule.DeadlineTimeTicks) : DateTime.MinValue };
                TaskEditor editor = null; string saveError = null;
                editor = new TaskEditor(template, false, delegate(TaskDraft draft) {
                    editor.RecoverRecurrenceHistory = null;
                    if (draft.Recurrence == null) {
                        bool stopped = StopRecurringRule(id);
                        saveError = stopped ? null : panel.StatusText.Text;
                        return stopped;
                    }
                    bool saved = TrySaveRecurringRule(id, draft, out saveError);
                    if (!saved && session.GetRecurrenceSummary(id).MissedCount > 0) {
                        editor.RecoverRecurrenceHistory = delegate {
                            ResolveMissedRecurrences(true);
                            return session.GetRecurrenceSummary(id).MissedCount == 0;
                        };
                    }
                    return saved;
                }, RecurringTasks.Copy(rule.Recurrence), true);
                editor.SaveErrorProvider = delegate { return saveError; };
                FitRecurringWindow(editor);
                if (ShowChildDialog(editor) == true) { state = session.Snapshot(); Render(); ShowStatus("后续规则已更新，已生成事项与完成历史保持原样。"); }
            }
            finally { applicationInteraction.EditorOpen = previousEditor; if (!nested) ResumeAttention(); }
        }

        internal bool TrySaveRecurringRule(string id, TaskDraft draft, out string failure)
        {
            failure = null;
            try { session.EditRecurringRule(id, draft); }
            catch (Exception error) {
                failure = "保存失败，输入仍保留：" + error.Message;
                ShowStatus(failure); return false;
            }
            state = session.Snapshot(); return true;
        }

        private bool SetRecurringPaused(string id, bool paused)
        {
            bool saved = Execute(delegate { session.SetRecurringPaused(id, paused); }, "重复规则状态未保存。", true);
            if (saved) ShowStatus(paused ? "已暂停后续生成，已有事项保留。" : "已恢复后续生成，不追补暂停期间的历史周期。");
            return saved;
        }

        private bool StopRecurringRule(string id)
        {
            if (ShowModal("停止这条规则的后续重复？\n\n尚未生成的遗漏周期也不再补建；已生成事项与完成历史都会保留，不会删除本次事项。", "停止后续重复", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) {
                ShowStatus("已取消停止后续重复，规则保持原样。"); return false;
            }
            bool saved = Execute(delegate { session.StopRecurringRule(id); }, "后续重复尚未停止。", true);
            if (saved) ShowStatus("后续重复已停止，已有事项保留。"); return saved;
        }

        private void QueueRecurrenceCheck()
        {
            if (disposed || recurrenceOperation != null) return;
            recurrenceOperation = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate {
                recurrenceOperation = null;
                if (disposed || HostLost || InteractionBlocked || applicationInteraction.IsBusy) return;
                ProcessRecurrences(true);
            }));
        }

        internal int ProcessRecurrences(bool offerMissed)
        {
            if (disposed || HostLost || InteractionBlocked || applicationInteraction.IsBusy || state.RecurringRules.Count == 0) return 0;
            int generated = 0;
            if (!Execute(delegate { generated = session.GenerateCurrentRecurrences(); }, "重复事项未生成，可稍后重试。", false)) return 0;
            if (generated > 0) { Render(); ShowStatus("已按计划生成 " + generated + " 项重复事项。"); }
            RecurrenceSummary summary = session.GetRecurrenceSummary();
            if (summary.MissedCount > 0) {
                ShowStatus("有 " + summary.MissedCount + " 期重复计划待处理，可从“⋯ → 处理遗漏周期”选择补建或跳过。");
                if (offerMissed && !missedRecurrencesOffered) { missedRecurrencesOffered = true; ResolveMissedRecurrences(false); }
            }
            return generated;
        }

        private void ResolveMissedRecurrences(bool nested)
        {
            if (disposed || (!nested && applicationInteraction.IsBusy)) return;
            bool previousEditor = applicationInteraction.EditorOpen;
            applicationInteraction.EditorOpen = true; interaction.StopAnimation(); RevealFromEdge();
            try {
                RecurrenceSummary summary = session.GetRecurrenceSummary();
                if (summary.TotalCount == 0) { ShowStatus("当前没有待处理的遗漏周期。"); return; }
                if (summary.MissedCount == 0) {
                    int currentCount = 0;
                    if (Execute(delegate { currentCount = session.GenerateCurrentRecurrences(); }, "当期事项未生成，可稍后重试。", true))
                        ShowStatus("已按计划生成 " + currentCount + " 项当期事项。");
                    return;
                }
                int generated = 0;
                var dialog = new RecurrenceCatchUpWindow(summary.MissedCount, summary.DueTodayCount, delegate(bool include) {
                    return Execute(delegate { generated = session.ResolveMissedRecurrences(include); }, "遗漏周期未保存，仍可重试。", false);
                });
                FitRecurringWindow(dialog);
                if (ShowChildDialog(dialog) == true) {
                    state = session.Snapshot(); Render();
                    long remaining = session.GetRecurrenceSummary().MissedCount;
                    ShowStatus("已生成 " + generated + " 项。" + (remaining > 0 ? "仍有 " + remaining + " 期遗漏，可继续处理。" : "遗漏周期已处理。"));
                }
            }
            finally { applicationInteraction.EditorOpen = previousEditor; if (!nested) ResumeAttention(); }
        }

        private void EditAppearance()
        {
            if (disposed || applicationInteraction.IsBusy) return;
            applicationInteraction.EditorOpen = true; interaction.StopAnimation();
            RevealFromEdge();
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
                ShowChildDialog(editor);
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
            if (ShowModal((String.IsNullOrEmpty(item.RecurringRuleId) ? "删除这条待办？" : "删除本次事项？后续重复规则继续保留，本期不会再次生成。") + "\n\n" + description, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
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
            else if (reminders != null) reminders.Refresh();
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
            if (reminders != null) reminders.Refresh();
            try {
                using (applicationInteraction.EnterModal()) {
                    Window owner = activeDialog ?? this;
                    return owner.IsVisible ? MessageBox.Show(owner, text, title, buttons, icon, defaultResult)
                        : MessageBox.Show(text, title, buttons, icon, defaultResult);
                }
            }
            finally { dialogOpen = false; ResumeAttention(); }
        }

        internal void PrepareChildWindow(Window child)
        {
            if (child == null) throw new ArgumentNullException("child");
            Window owner = activeDialog ?? this;
            if (owner.IsVisible) child.Owner = owner;
            child.Topmost = state.Settings.AlwaysOnTop;
        }

        private bool? ShowChildDialog(Window child)
        {
            PrepareChildWindow(child);
            if (reminders != null) reminders.Refresh();
            Window previous = activeDialog;
            activeDialog = child;
            try { return child.ShowDialog(); }
            finally { activeDialog = previous; }
        }

        private void TrackMenu(ContextMenu menu)
        {
            menu.Opened += delegate { menuOpen = true; interaction.StopAnimation(); RevealFromEdge(); if (reminders != null) reminders.Refresh(); };
            menu.Closed += delegate { menuOpen = false; ResumeAttention(); };
        }

        private double GrowthProgress()
        {
            return state.Settings.EffectiveAutoExpand ? TodoRules.Expansion(state, DateTime.Now) : 0;
        }

        private void UpdateExpansion(bool immediate, bool allowDuringInteraction)
        {
            interaction.UpdateExpansion(GrowthProgress(), state.Settings.Collapsed, immediate, allowDuringInteraction);
        }

        private void ResumeAttention()
        {
            if (reminders != null) reminders.CheckNow();
            if (disposed || !interaction.IsReady || HostLost || resumeQueued) return;
            resumeQueued = true;
            resumeOperation = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate {
                resumeOperation = null;
                resumeQueued = false;
                if (disposed || !interaction.IsReady || HostLost || InteractionBlocked) return;
                ProcessRecurrences(true);
                if (deferredRender || dateStamp != DateTime.Now.ToString("yyyyMMddHHmmss")) Render();
                else UpdateExpansion(false, false);
            }));
        }

    }
}
