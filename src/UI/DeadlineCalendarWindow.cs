using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace CornerTodo
{
    internal sealed class DeadlineCalendarWindow : Window
    {
        private enum DetailMode { Date, Overdue, Unscheduled }

        private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");
        private static readonly string[] Weekdays = { "日", "一", "二", "三", "四", "五", "六" };

        private readonly Action<TodoItem> edit;
        private readonly Func<TodoItem, bool, bool> setCompleted;
        private readonly Button previous, next, today, overdue, unscheduled;
        private readonly CheckBox includeCompleted;
        private readonly TextBlock monthTitle, detailTitle, detailCount, empty, status;
        private readonly Grid calendar;
        private readonly Border calendarSurface, detailSurface;
        private readonly ScrollViewer calendarScroll, detailsScroll;
        private readonly StackPanel detailRows;
        private readonly List<TextBlock> weekdayLabels = new List<TextBlock>();
        private readonly List<DayCell> cells = new List<DayCell>();
        private readonly Dictionary<string, DetailRow> rows = new Dictionary<string, DetailRow>(StringComparer.Ordinal);
        private readonly ControlTemplate dayTemplate;
        private TodoState state;
        private DateTime now, month, selectedDate;
        private DetailMode detailMode;
        private AppearanceSettings appearance;
        private PanelPalette palette;
        private Brush cellTint, selectedTint;

        internal DeadlineCalendarWindow(TodoState state, DateTime now, Action<TodoItem> edit,
            Func<TodoItem, bool, bool> setCompleted)
        {
            this.edit = edit;
            this.setCompleted = setCompleted;
            selectedDate = RecurringTasks.LocalDate(now);
            month = FirstOfMonth(selectedDate);
            dayTemplate = CreateDayTemplate();

            Title = "CornerTodo · 截止日历";
            Width = 1090; Height = 674; MinWidth = 460; MinHeight = 320;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            var root = new Grid { Margin = new Thickness(14, 12, 14, 14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = root;
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(toolbar);
            var monthNavigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 5) };
            toolbar.Children.Add(monthNavigation);
            previous = ActionButton("‹", "上个月", delegate { MoveMonth(-1); });
            AutomationProperties.SetAutomationId(previous, "CalendarPreviousMonth");
            previous.FontSize = 21; previous.Width = 37;
            monthNavigation.Children.Add(previous);
            monthTitle = Theme.Text("", 22, Theme.Ink);
            AutomationProperties.SetAutomationId(monthTitle, "CalendarMonthTitle");
            monthTitle.FontWeight = FontWeights.SemiBold; monthTitle.Margin = new Thickness(12, 0, 12, 0);
            monthTitle.VerticalAlignment = VerticalAlignment.Center;
            monthNavigation.Children.Add(monthTitle);
            next = ActionButton("›", "下个月", delegate { MoveMonth(1); });
            AutomationProperties.SetAutomationId(next, "CalendarNextMonth");
            next.FontSize = 21; next.Width = 37;
            monthNavigation.Children.Add(next);
            today = ActionButton("今天", "今天", delegate { SelectDate(RecurringTasks.LocalDate(this.now), false); });
            AutomationProperties.SetAutomationId(today, "CalendarToday");
            today.Margin = new Thickness(0, 0, 13, 5); toolbar.Children.Add(today);
            includeCompleted = new CheckBox { Content = "显示已完成", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 5), Padding = new Thickness(4, 3, 4, 3) };
            AutomationProperties.SetAutomationId(includeCompleted, "CalendarIncludeCompleted");
            includeCompleted.Checked += delegate { FilterChanged(); };
            includeCompleted.Unchecked += delegate { FilterChanged(); };
            toolbar.Children.Add(includeCompleted);
            overdue = ActionButton("", "查看逾期待办", delegate { ShowDetail(DetailMode.Overdue); });
            AutomationProperties.SetAutomationId(overdue, "CalendarOverdue");
            overdue.Margin = new Thickness(0, 0, 0, 5); toolbar.Children.Add(overdue);

            var body = new Grid { MinWidth = 750 };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.35, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 246 });
            var bodyScroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            AutomationProperties.SetName(bodyScroll, "截止日历内容");
            body.SetBinding(WidthProperty, new Binding("ViewportWidth") { Source = bodyScroll });
            Grid.SetRow(bodyScroll, 1); root.Children.Add(bodyScroll);

            calendar = new Grid();
            for (int column = 0; column < 7; column++) calendar.ColumnDefinitions.Add(new ColumnDefinition());
            calendarScroll = VerticalScroll(calendar, "月历");
            calendar.SetBinding(HeightProperty, new Binding("ViewportHeight") { Source = calendarScroll });
            calendarSurface = new Border { Child = calendarScroll, CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0) };
            body.Children.Add(calendarSurface);

            var details = new Grid { Margin = new Thickness(14, 12, 14, 12) };
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailSurface = new Border { Child = details, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            Grid.SetColumn(detailSurface, 1); body.Children.Add(detailSurface);
            var detailHeading = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            detailTitle = Theme.Text("", 19, Theme.Ink); detailTitle.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetAutomationId(detailTitle, "CalendarDetailTitle");
            detailCount = Theme.Text("", 12, Theme.Muted); detailCount.Margin = new Thickness(0, 5, 0, 0);
            detailHeading.Children.Add(detailTitle); detailHeading.Children.Add(detailCount); details.Children.Add(detailHeading);
            detailRows = new StackPanel();
            empty = Theme.Text("", 13, Theme.Muted); empty.Margin = new Thickness(0, 10, 0, 8);
            detailRows.Children.Add(empty);
            detailsScroll = VerticalScroll(detailRows, "日期待办详情");
            Grid.SetRow(detailsScroll, 1); details.Children.Add(detailsScroll);
            status = Theme.Text("", 12, Theme.Warning);
            status.Margin = new Thickness(0, 4, 0, 3); status.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(status, "CalendarStatus");
            AutomationProperties.SetName(status, "截止日历操作提示");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            Grid.SetRow(status, 2); details.Children.Add(status);
            unscheduled = ActionButton("", "查看未设截止的待办", delegate { ShowDetail(DetailMode.Unscheduled); });
            AutomationProperties.SetAutomationId(unscheduled, "CalendarUnscheduled");
            unscheduled.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(unscheduled, 3); details.Children.Add(unscheduled);

            BuildMonth();
            Refresh(state, now);
        }

        internal void Refresh(TodoState state, DateTime now)
        {
            if (state == null) throw new ArgumentNullException("state");
            this.state = state; this.now = now;
            // Keep the calendar above a floating panel when their bounds overlap.
            bool floating = state.Settings != null && state.Settings.AlwaysOnTop;
            if (Topmost != floating) Topmost = floating;
            AppearanceSettings current = AppearanceSettings.Normalize(state.Settings == null ? null : state.Settings.Appearance);
            if (appearance == null || !appearance.Matches(current)) ApplyPalette(current);
            Render();
        }

        private void ApplyPalette(AppearanceSettings value)
        {
            appearance = value;
            // The independent calendar uses a solid surface for compact text and grid lines.
            var solid = value.Clone(); solid.BackgroundTransparency = 0;
            palette = new PanelPalette(solid);
            Background = palette.Background; Foreground = palette.Ink;
            cellTint = Tint(palette.Muted.Color, 12);
            selectedTint = Tint(palette.Accent.Color, 36);
            calendarSurface.BorderBrush = detailSurface.BorderBrush = palette.ToolbarBorder;
            detailSurface.Background = cellTint;
            monthTitle.Foreground = detailTitle.Foreground = palette.Ink;
            detailCount.Foreground = empty.Foreground = palette.Muted;
            status.Foreground = palette.Warning;
            includeCompleted.Foreground = palette.Ink;
            foreach (Button button in new[] { previous, next, today, unscheduled }) button.Style = palette.ActionButtonStyle;
            overdue.Style = palette.ButtonStyle;
            calendarScroll.Foreground = detailsScroll.Foreground = SystemParameters.HighContrast
                ? SystemColors.WindowTextBrush : palette.Muted;
        }

        private void BuildMonth()
        {
            calendar.Children.Clear(); calendar.RowDefinitions.Clear();
            cells.Clear(); weekdayLabels.Clear();
            IList<DateTime?> dates = DeadlineCalendarData.GetMonthDays(month);
            calendar.MinHeight = 33 + 78 * (dates.Count / 7);
            calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) });
            for (int i = 0; i < dates.Count / 7; i++) calendar.RowDefinitions.Add(new RowDefinition());
            for (int column = 0; column < Weekdays.Length; column++) {
                var label = Theme.Text(Weekdays[column], 14, Theme.Ink);
                label.TextAlignment = TextAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
                label.FontWeight = FontWeights.SemiBold;
                weekdayLabels.Add(label);
                var heading = new Border { Child = label, BorderThickness = new Thickness(0, 0, column == 6 ? 0 : 1, 1) };
                Grid.SetColumn(heading, column); calendar.Children.Add(heading);
            }
            for (int index = 0; index < dates.Count; index++) {
                var cell = new DayCell(dates[index], dayTemplate);
                if (cell.Date.HasValue) {
                    cell.Button.Click += delegate { SelectDate(cell.Date.Value, true); };
                    cell.Button.PreviewKeyDown += delegate(object sender, KeyEventArgs args) { HandleDayKey(cell.Date.Value, args); };
                }
                var border = new Border { Child = cell.Button, BorderThickness = new Thickness(0, 0, index % 7 == 6 ? 0 : 1,
                    index >= dates.Count - 7 ? 0 : 1) };
                Grid.SetRow(border, 1 + index / 7); Grid.SetColumn(border, index % 7);
                calendar.Children.Add(border); cells.Add(cell);
            }
            calendarScroll.ScrollToTop();
        }

        private void MoveMonth(int delta)
        {
            if (delta < 0 && month == DateTime.MinValue.Date || delta > 0 && month.Year == 9999 && month.Month == 12) return;
            DateTime target = month.AddMonths(delta);
            SelectDate(new DateTime(target.Year, target.Month, Math.Min(selectedDate.Day, DateTime.DaysInMonth(target.Year, target.Month))), false);
        }

        private void HandleDayKey(DateTime date, KeyEventArgs args)
        {
            int shift;
            switch (args.Key) {
                case Key.Left: shift = -1; break;
                case Key.Right: shift = 1; break;
                case Key.Up: shift = -7; break;
                case Key.Down: shift = 7; break;
                default: return;
            }
            args.Handled = true;
            long ticks = date.Ticks + shift * TimeSpan.TicksPerDay;
            if (ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks) SelectDate(new DateTime(ticks), true);
        }

        private void SelectDate(DateTime date, bool focus)
        {
            selectedDate = date;
            DateTime target = FirstOfMonth(date);
            if (month != target) { month = target; BuildMonth(); }
            ShowDetail(DetailMode.Date);
            DayCell selected = cells.FirstOrDefault(cell => cell.Date == selectedDate);
            if (selected == null) return;
            selected.Button.BringIntoView();
            if (focus) selected.Button.Focus();
        }

        private void ShowDetail(DetailMode mode)
        {
            detailMode = mode;
            SetStatus("");
            Render();
            detailsScroll.ScrollToTop();
        }

        private void FilterChanged()
        {
            if (state == null) return;
            SetStatus("");
            Render();
        }

        private void Render()
        {
            var data = new DeadlineCalendarData(state, includeCompleted.IsChecked == true, now);
            DateTime localToday = RecurringTasks.LocalDate(now);
            monthTitle.Text = month.ToString("yyyy年M月", Chinese);
            previous.IsEnabled = month != DateTime.MinValue.Date;
            next.IsEnabled = month.Year != 9999 || month.Month != 12;
            for (int index = 0; index < Weekdays.Length; index++) {
                var border = (Border)calendar.Children[index];
                border.Background = palette.ToolbarBackground; border.BorderBrush = palette.ToolbarBorder;
                weekdayLabels[index].Foreground = palette.Ink;
            }
            for (int index = 0; index < cells.Count; index++) {
                ((Border)calendar.Children[index + Weekdays.Length]).BorderBrush = palette.ToolbarBorder;
                DayCell cell = cells[index];
                if (!cell.Date.HasValue) continue;
                DateTime date = cell.Date.Value;
                bool currentMonth = FirstOfMonth(date) == month;
                bool selected = date == selectedDate && detailMode == DetailMode.Date;
                bool isToday = date == localToday;
                cell.Update(currentMonth ? data.ItemsOn(date) : null, currentMonth, selected,
                    date == selectedDate, isToday, now, palette, cellTint, selectedTint);
            }
            overdue.Content = "逾期 " + data.Overdue.Count + " 项";
            overdue.Foreground = data.Overdue.Count > 0 ? palette.Urgent : palette.Muted;
            overdue.Background = detailMode == DetailMode.Overdue ? selectedTint : Brushes.Transparent;
            AutomationProperties.SetName(overdue, "查看逾期待办，共 " + data.Overdue.Count + " 项");
            unscheduled.Content = "未设截止 · " + data.Unscheduled.Count + " 项";
            unscheduled.Background = detailMode == DetailMode.Unscheduled ? selectedTint : palette.ToolbarBackground;
            AutomationProperties.SetName(unscheduled, "查看未设截止的待办，共 " + data.Unscheduled.Count + " 项");

            IList<TodoItem> items;
            switch (detailMode) {
                case DetailMode.Overdue:
                    detailTitle.Text = "逾期待办"; empty.Text = "当前没有逾期待办。"; items = data.Overdue; break;
                case DetailMode.Unscheduled:
                    detailTitle.Text = "未设截止"; empty.Text = "当前没有未设截止的事项。"; items = data.Unscheduled; break;
                default:
                    detailTitle.Text = selectedDate.ToString("M月d日 · dddd", Chinese);
                    empty.Text = "这一天没有待办。"; items = data.ItemsOn(selectedDate); break;
            }
            int completed = items.Count(item => item.IsCompleted);
            detailCount.Text = (items.Count - completed) + " 项待办" + (completed == 0 ? "" : " · " + completed + " 项已完成");
            empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateRows(items);
        }

        private void UpdateRows(IList<TodoItem> items)
        {
            var ids = new HashSet<string>(items.Select(item => item.Id), StringComparer.Ordinal);
            foreach (string id in rows.Keys.Where(id => !ids.Contains(id)).ToArray()) {
                detailRows.Children.Remove(rows[id].Surface); rows.Remove(id);
            }
            for (int index = 0; index < items.Count; index++) {
                TodoItem item = items[index];
                DetailRow row;
                if (!rows.TryGetValue(item.Id, out row)) {
                    row = new DetailRow(EditItem, ChangeCompletion);
                    rows.Add(item.Id, row);
                    detailRows.Children.Insert(index + 1, row.Surface);
                } else if (!Object.ReferenceEquals(detailRows.Children[index + 1], row.Surface)) {
                    detailRows.Children.Remove(row.Surface);
                    detailRows.Children.Insert(index + 1, row.Surface);
                }
                row.Update(item, now, detailMode == DetailMode.Overdue, palette);
            }
        }

        private void EditItem(TodoItem item)
        {
            SetStatus("");
            if (edit != null) edit(item);
        }

        private void ChangeCompletion(TodoItem item, bool value)
        {
            bool saved = setCompleted != null && setCompleted(item, value);
            SetStatus(saved ? "" : "未能保存完成状态。请检查后重试。");
        }

        private void SetStatus(string message)
        {
            status.Text = message;
            status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (message.Length > 0) {
                AutomationProperties.SetName(status, message);
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status);
                if (peer != null) peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
            }
        }

        private static DateTime FirstOfMonth(DateTime date) { return new DateTime(date.Year, date.Month, 1); }

        private static Brush Tint(Color color, byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze(); return brush;
        }

        private static Button ActionButton(string label, string name, Action action)
        {
            var button = new Button { Content = label, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4) };
            AutomationProperties.SetName(button, name);
            button.Click += delegate { action(); };
            return button;
        }

        private static ScrollViewer VerticalScroll(UIElement content, string name)
        {
            var scroll = new ScrollViewer { Content = content, Focusable = false, Style = Theme.PanelScrollViewer(),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetName(scroll, name);
            return scroll;
        }

        private static ControlTemplate CreateDayTemplate()
        {
            return (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
                <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4' ClipToBounds='True'>
                    <Border x:Name='Interaction' BorderThickness='1' CornerRadius='3' Padding='{TemplateBinding Padding}'>
                        <ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Top'/>
                    </Border>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Interaction' Property='Background' Value='#16528494'/></Trigger>
                    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Interaction' Property='BorderBrush' Value='{Binding Foreground, RelativeSource={RelativeSource TemplatedParent}}'/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>");
        }

        private sealed class DayCell
        {
            internal readonly DateTime? Date;
            internal readonly Button Button;
            private readonly TextBlock dayNumber, late, extra;
            private readonly TextBlock[] titles = new TextBlock[2];
            private readonly TextBlock[] dots = new TextBlock[2];
            private readonly Grid[] previews = new Grid[2];

            internal DayCell(DateTime? date, ControlTemplate template)
            {
                Date = date;
                var content = new StackPanel { ClipToBounds = true };
                Button = new Button { Content = content, Template = template, Padding = new Thickness(5, 2, 5, 2),
                    Cursor = Cursors.Hand, IsEnabled = date.HasValue, IsTabStop = false };
                if (date.HasValue) AutomationProperties.SetAutomationId(Button, "CalendarDay_" + date.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                var heading = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 1) };
                late = Theme.Text("逾期", 10, Theme.Urgent); late.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(late, Dock.Right); heading.Children.Add(late);
                dayNumber = Theme.Text(date.HasValue ? date.Value.Day.ToString(Chinese) : "", 14, Theme.Ink);
                heading.Children.Add(dayNumber); content.Children.Add(heading);
                for (int index = 0; index < titles.Length; index++) {
                    var row = new Grid(); previews[index] = row;
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    dots[index] = Theme.Text("●", 8, Theme.Accent); dots[index].VerticalAlignment = VerticalAlignment.Center;
                    titles[index] = Theme.Text("", 11, Theme.Ink);
                    titles[index].TextWrapping = TextWrapping.NoWrap; titles[index].TextTrimming = TextTrimming.CharacterEllipsis;
                    Grid.SetColumn(titles[index], 1); row.Children.Add(dots[index]); row.Children.Add(titles[index]);
                    content.Children.Add(row);
                }
                extra = Theme.Text("", 10, Theme.Muted); extra.TextWrapping = TextWrapping.NoWrap; content.Children.Add(extra);
                if (!date.HasValue) content.Visibility = Visibility.Collapsed;
            }

            internal void Update(IList<TodoItem> items, bool currentMonth, bool selected, bool tabStop, bool today,
                DateTime now, PanelPalette palette, Brush mutedBackground, Brush selectedBackground)
            {
                int count = items == null ? 0 : items.Count;
                int lateCount = count == 0 ? 0 : items.Count(item => TodoRules.IsOverdue(item, now));
                Button.IsTabStop = tabStop;
                Button.Background = selected ? selectedBackground : currentMonth ? Brushes.Transparent : mutedBackground;
                Button.BorderBrush = selected ? palette.Accent : Brushes.Transparent;
                Button.Foreground = palette.Accent;
                dayNumber.Foreground = today ? palette.Accent : currentMonth ? palette.Ink : palette.Muted;
                dayNumber.FontWeight = today || selected ? FontWeights.SemiBold : FontWeights.Normal;
                late.Foreground = palette.Urgent; late.Visibility = lateCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                for (int index = 0; index < titles.Length; index++) {
                    previews[index].Visibility = index < count ? Visibility.Visible : Visibility.Collapsed;
                    if (index >= count) continue;
                    TodoItem item = items[index];
                    titles[index].Text = (item.Text ?? "").Replace('\r', ' ').Replace('\n', ' ');
                    titles[index].Foreground = item.IsCompleted ? palette.Muted : palette.Ink;
                    titles[index].TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null;
                    dots[index].Foreground = TodoRules.IsOverdue(item, now) ? palette.Urgent : item.IsCompleted ? palette.Muted : palette.Accent;
                }
                extra.Text = count > 2 ? "+" + (count - 2) + " 项" : "";
                extra.Foreground = palette.Muted;
                extra.Visibility = count > 2 ? Visibility.Visible : Visibility.Collapsed;
                string name = Date.Value.ToString("yyyy年M月d日 dddd", Chinese) + (today ? "，今天" : "") +
                    (selected ? "，已选中" : "") + (currentMonth ? "，" + count + " 项" : "，相邻月份") +
                    (lateCount > 0 ? "，其中逾期 " + lateCount + " 项" : "");
                AutomationProperties.SetName(Button, name);
                Button.ToolTip = count == 0 ? name : name + "\n" + String.Join("\n", items.Take(2).Select(item => item.Text));
            }
        }

        private sealed class DetailRow
        {
            internal readonly Border Surface;
            private readonly CheckBox complete;
            private readonly Button edit;
            private readonly TextBlock title, caption;
            private TodoItem item;

            internal DetailRow(Action<TodoItem> editItem, Action<TodoItem, bool> changeCompletion)
            {
                var body = new Grid { Margin = new Thickness(0, 7, 0, 8) };
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                body.ColumnDefinitions.Add(new ColumnDefinition());
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                complete = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 4, 0) };
                complete.Click += delegate {
                    TodoItem current = item;
                    bool requested = complete.IsChecked == true;
                    // Only the confirmed saved snapshot may change this checkbox.
                    complete.IsChecked = current.IsCompleted;
                    if (requested != current.IsCompleted) changeCompletion(current, requested);
                };
                body.Children.Add(complete);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                title = Theme.Text("", 14, Theme.Ink);
                caption = Theme.Text("", 12, Theme.Muted); caption.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(title); text.Children.Add(caption);
                Grid.SetColumn(text, 1); body.Children.Add(text);
                edit = ActionButton("编辑", "编辑待办", delegate { editItem(item); });
                edit.FontSize = 11; edit.Padding = new Thickness(5, 3, 5, 3); edit.MinHeight = 28;
                edit.VerticalAlignment = VerticalAlignment.Top; edit.Margin = new Thickness(4, 0, 0, 0);
                Grid.SetColumn(edit, 2); body.Children.Add(edit);
                Surface = new Border { Child = body, BorderThickness = new Thickness(0, 1, 0, 0) };
            }

            internal void Update(TodoItem value, DateTime now, bool showDate, PanelPalette palette)
            {
                item = value;
                Surface.BorderBrush = palette.Separator;
                complete.Style = palette.CheckStyle; complete.IsChecked = item.IsCompleted;
                AutomationProperties.SetAutomationId(complete, "CalendarComplete_" + item.Id);
                AutomationProperties.SetName(complete, (item.IsCompleted ? "恢复未完成：" : "标记完成：") + item.Text);
                complete.ToolTip = item.IsCompleted ? "恢复未完成" : "标记完成";
                edit.Style = palette.ButtonStyle;
                AutomationProperties.SetAutomationId(edit, "CalendarEdit_" + item.Id);
                AutomationProperties.SetName(edit, "编辑待办：" + item.Text);
                title.Text = item.Text; title.Foreground = item.IsCompleted ? palette.Muted : palette.Ink;
                title.TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null;
                string deadline = "未设截止";
                if (item.HasDeadline) {
                    DateTime due = item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
                    if (showDate) deadline = due.ToString(item.HasTime ? "yyyy年M月d日 HH:mm" : "yyyy年M月d日", Chinese) + " 截止";
                    else deadline = item.HasTime ? due.ToString("HH:mm", Chinese) + " 截止" : "当天截止";
                }
                bool late = TodoRules.IsOverdue(item, now);
                caption.Text = deadline + (item.IsCompleted ? " · 已完成" : late ? " · 已逾期" : "");
                caption.Foreground = late ? palette.Urgent : palette.Muted;
            }
        }
    }
}
