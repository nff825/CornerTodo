using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerTodo
{
    internal sealed class TaskEditor : Window
    {
        private readonly TextBox taskText;
        private readonly CheckBox useDeadline;
        private readonly StackPanel deadlineFields;
        private readonly TextBlock noDeadlineHint;
        private readonly DatePicker date;
        private readonly CheckBox useTime;
        private readonly ComboBox hour;
        private readonly ComboBox minute;
        private readonly TextBox notes;
        private readonly Expander notesSection;
        private readonly TextBlock notesCount;
        private readonly RadioButton highPriority;
        private readonly RadioButton normalPriority;
        private readonly RadioButton lowPriority;
        private readonly TextBlock error;
        private readonly TodoItem original;
        private readonly Func<TaskDraft, bool> saveDraft;
        private readonly double fullEditorHeight;
        private readonly CheckBox useRecurrence;
        private readonly StackPanel recurrenceFields;
        private readonly ComboBox recurrenceFrequency;
        private readonly StackPanel weekDaysRow;
        private readonly CheckBox[] weekDays = new CheckBox[7];
        private readonly StackPanel monthDayRow;
        private readonly ComboBox monthDay;
        private readonly DatePicker recurrenceStart;
        private readonly TextBlock recurrenceSummary;
        private readonly bool editingRule;
        private readonly Button recoverRecurrence;
        public TaskDraft Result { get; private set; }
        internal Func<string> SaveErrorProvider { get; set; }
        internal Func<bool> RecoverRecurrenceHistory { get; set; }

        public TaskEditor(TodoItem item) : this(item, false) { }

        internal TaskEditor(TodoItem item, bool focusNotes) : this(item, focusNotes, null) { }

        internal TaskEditor(TodoItem item, bool focusNotes, Func<TaskDraft, bool> saveDraft)
            : this(item, focusNotes, saveDraft, null, item == null || String.IsNullOrEmpty(item.RecurringRuleId)) { }

        internal TaskEditor(TodoItem item, bool focusNotes, Func<TaskDraft, bool> saveDraft,
            RecurrenceOptions initialRecurrence, bool allowRecurrence)
        {
            original = item;
            this.saveDraft = saveDraft;
            DateTime displayedDue = item == null || !item.HasDeadline ? DateTime.Today :
                item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
            editingRule = initialRecurrence != null;
            Title = editingRule ? "修改后续重复规则" : item == null ? "添加待办" : "编辑待办";
            bool showNotes = focusNotes || (item != null && !String.IsNullOrEmpty(item.Notes));
            fullEditorHeight = (showNotes ? 690 : 535) + (allowRecurrence ? 36 : 26);
            Width = 460; Height = fullEditorHeight - (item != null && !item.HasDeadline ? 85 : 0);
            MinWidth = 320; MinHeight = 300;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = Theme.Brush("#F4F9FB");
            Foreground = Theme.Ink;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            var root = new Grid { Margin = new Thickness(25, 18, 25, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var heading = Theme.Text(Title, 23, Theme.Ink); heading.FontWeight = FontWeights.SemiBold;
            Add(root, heading, 0);
            var fields = new StackPanel { Margin = new Thickness(0, 0, 6, 4) };
            var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, -6, 0),
                PanningMode = PanningMode.VerticalOnly };
            AutomationProperties.SetName(scroll, "待办编辑内容"); Add(root, scroll, 1);
            var label = Theme.Text("事项", 13, Theme.Muted); label.Margin = new Thickness(0, 14, 0, 7); fields.Children.Add(label);
            taskText = new TextBox { Text = item == null ? "" : item.Text, Height = 76, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MaxLength = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8), Background = Brushes.White, BorderBrush = Theme.Brush("#BDD0D8"), FontSize = 15 };
            AutomationProperties.SetName(taskText, "待办事项"); fields.Children.Add(taskText);
            useDeadline = new CheckBox { Content = "设置截止日期", IsChecked = item == null || item.HasDeadline,
                Foreground = Theme.Ink, Margin = new Thickness(0, 16, 0, 7) };
            AutomationProperties.SetName(useDeadline, "设置截止日期"); fields.Children.Add(useDeadline);
            noDeadlineHint = Theme.Text("未设截止，可随时补充", 12, Theme.Muted);
            AutomationProperties.SetName(noDeadlineHint, "无截止日期说明"); fields.Children.Add(noDeadlineHint);
            deadlineFields = new StackPanel();
            AutomationProperties.SetName(deadlineFields, "截止日期与时刻设置"); fields.Children.Add(deadlineFields);
            date = new DatePicker { SelectedDate = displayedDue.Date,
                SelectedDateFormat = DatePickerFormat.Long, FontSize = 14, Padding = new Thickness(5),
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN") };
            date.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; };
            AutomationProperties.SetName(date, "截止日期"); deadlineFields.Children.Add(date);
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            useTime = new CheckBox { Content = "设置具体时刻", IsChecked = item != null && item.HasDeadline && item.HasTime, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(useTime, "设置具体时刻");
            hour = TimePart(24, item != null && item.HasDeadline && item.HasTime ? displayedDue.Hour : 18, "截止小时，24小时制");
            minute = TimePart(60, item != null && item.HasDeadline && item.HasTime ? displayedDue.Minute : 0, "截止分钟");
            hour.Margin = new Thickness(14, 0, 0, 0);
            hour.IsEnabled = minute.IsEnabled = useTime.IsChecked == true;
            useTime.Checked += delegate { hour.IsEnabled = minute.IsEnabled = true; };
            useTime.Unchecked += delegate { hour.IsEnabled = minute.IsEnabled = false; };
            var separator = Theme.Text(":", 15, Theme.Ink); separator.Width = 14;
            separator.TextAlignment = TextAlignment.Center; separator.VerticalAlignment = VerticalAlignment.Center;
            timeRow.Children.Add(useTime); timeRow.Children.Add(hour); timeRow.Children.Add(separator); timeRow.Children.Add(minute);
            deadlineFields.Children.Add(timeRow);
            useDeadline.Checked += delegate { UpdateDeadlineFields(); };
            useDeadline.Unchecked += delegate { UpdateDeadlineFields(); };
            UpdateDeadlineFields();
            useRecurrence = new CheckBox { Content = "重复任务", IsChecked = initialRecurrence != null,
                Margin = new Thickness(0, 15, 0, 6), Visibility = allowRecurrence ? Visibility.Visible : Visibility.Collapsed,
                IsEnabled = true };
            AutomationProperties.SetName(useRecurrence, "重复任务"); fields.Children.Add(useRecurrence);
            if (editingRule) useRecurrence.ToolTip = "取消后保存会要求确认停止后续重复，已有事项保留。";
            if (!allowRecurrence) {
                var originHint = Theme.Text("仅编辑本次事项，不改变后续重复规则。", 12, Theme.Muted);
                originHint.Margin = new Thickness(0, 14, 0, 0); fields.Children.Add(originHint);
            }
            recurrenceFields = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            AutomationProperties.SetName(recurrenceFields, "重复任务设置"); fields.Children.Add(recurrenceFields);
            recurrenceFrequency = new ComboBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(7, 5, 7, 5), MaxDropDownHeight = 180 };
            recurrenceFrequency.Items.Add("每日"); recurrenceFrequency.Items.Add("每周"); recurrenceFrequency.Items.Add("每月");
            recurrenceFrequency.SelectedIndex = initialRecurrence == null ? 0 : (int)initialRecurrence.Frequency - 1;
            AutomationProperties.SetName(recurrenceFrequency, "重复方式");
            recurrenceFields.Children.Add(RecurrenceField("重复方式", recurrenceFrequency));
            weekDaysRow = new StackPanel(); var dayChoices = new UniformGrid { Columns = 7 };
            string[] dayLabels = { "日", "一", "二", "三", "四", "五", "六" };
            for (int order = 0; order < 7; order++) {
                int day = (order + 1) % 7;
                weekDays[day] = new CheckBox { Content = dayLabels[day], Style = WeekDayStyle(),
                    IsChecked = initialRecurrence == null ? day == (int)DateTime.Today.DayOfWeek : (initialRecurrence.WeekDays & (1 << day)) != 0,
                    Margin = new Thickness(0, 0, order == 6 ? 0 : 3, 0) };
                AutomationProperties.SetName(weekDays[day], "重复星期" + dayLabels[day]);
                dayChoices.Children.Add(weekDays[day]);
                weekDays[day].Checked += delegate { UpdateRecurrenceFields(); };
                weekDays[day].Unchecked += delegate { UpdateRecurrenceFields(); };
            }
            weekDaysRow.Children.Add(RecurrenceField("重复日", dayChoices)); recurrenceFields.Children.Add(weekDaysRow);
            monthDayRow = new StackPanel();
            monthDay = new ComboBox { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(7, 5, 7, 5), MaxDropDownHeight = 220 };
            for (int day = 1; day <= 31; day++) monthDay.Items.Add(day + "日");
            monthDay.Items.Add("每月最后一天");
            monthDay.SelectedIndex = initialRecurrence == null ? DateTime.Today.Day - 1 : initialRecurrence.UseLastDay ? 31 : initialRecurrence.DayOfMonth - 1;
            AutomationProperties.SetName(monthDay, "每月生成日期");
            monthDayRow.Children.Add(RecurrenceField("生成日期", monthDay));
            var shortMonthHint = Theme.Text("短月按当月最后一天生成，下月仍按原定日期。", 11, Theme.Muted);
            shortMonthHint.Margin = new Thickness(0, 3, 0, 6); monthDayRow.Children.Add(shortMonthHint);
            recurrenceFields.Children.Add(monthDayRow);
            recurrenceStart = new DatePicker { SelectedDate = initialRecurrence == null ? DateTime.Today : initialRecurrence.StartDate,
                SelectedDateFormat = DatePickerFormat.Long, Padding = new Thickness(5), Language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN") };
            recurrenceStart.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; };
            AutomationProperties.SetName(recurrenceStart, "重复开始日期");
            recurrenceFields.Children.Add(RecurrenceField("开始日期", recurrenceStart));
            recurrenceSummary = Theme.Text("", 11, Theme.Muted); recurrenceSummary.Margin = new Thickness(0, 3, 0, 2);
            recurrenceFields.Children.Add(recurrenceSummary);
            var deadlineHint = Theme.Text("截止日期可不设；设置后，每期按与开始日期的间隔顺延。", 11, Theme.Muted);
            recurrenceFields.Children.Add(deadlineHint);
            useRecurrence.Checked += delegate { UpdateRecurrenceFields(); };
            useRecurrence.Unchecked += delegate { UpdateRecurrenceFields(); };
            recurrenceFrequency.SelectionChanged += delegate { UpdateRecurrenceFields(); };
            monthDay.SelectionChanged += delegate { UpdateRecurrenceFields(); };
            recurrenceStart.SelectedDateChanged += delegate {
                if (useDeadline.IsChecked == true && recurrenceStart.SelectedDate.HasValue && date.SelectedDate.HasValue && date.SelectedDate.Value.Date < recurrenceStart.SelectedDate.Value.Date)
                    date.SelectedDate = recurrenceStart.SelectedDate.Value.Date;
                UpdateRecurrenceFields();
            };
            UpdateRecurrenceFields();
            var priorityLabel = Theme.Text("优先级", 13, Theme.Muted); priorityLabel.Margin = new Thickness(0, 16, 0, 7); fields.Children.Add(priorityLabel);
            var priorities = new Grid();
            for (int i = 0; i < 3; i++) priorities.ColumnDefinitions.Add(new ColumnDefinition());
            Style priorityStyle = PriorityStyle();
            highPriority = PriorityOption("高", TaskPriority.High, priorityStyle);
            normalPriority = PriorityOption("中", TaskPriority.Normal, priorityStyle);
            lowPriority = PriorityOption("低", TaskPriority.Low, priorityStyle);
            Grid.SetColumn(normalPriority, 1); Grid.SetColumn(lowPriority, 2);
            priorities.Children.Add(highPriority); priorities.Children.Add(normalPriority); priorities.Children.Add(lowPriority);
            TaskPriority selectedPriority = item == null ? TaskPriority.Normal : item.Priority;
            highPriority.IsChecked = selectedPriority == TaskPriority.High;
            lowPriority.IsChecked = selectedPriority == TaskPriority.Low;
            normalPriority.IsChecked = !highPriority.IsChecked.Value && !lowPriority.IsChecked.Value;
            fields.Children.Add(priorities);

            notes = new TextBox { Text = item == null ? "" : item.Notes ?? "", Height = 120, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MaxLength = TodoRules.MaxNotesLength, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8), Background = Brushes.White, BorderBrush = Theme.Brush("#BDD0D8"), FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0) };
            AutomationProperties.SetName(notes, "备注，选填");
            notesCount = Theme.Text("", 11, Theme.Muted); notesCount.Margin = new Thickness(0, 6, 0, 0);
            AutomationProperties.SetName(notesCount, "备注字数");
            var notesContent = new StackPanel(); notesContent.Children.Add(notes); notesContent.Children.Add(notesCount);
            notesSection = new Expander { Header = "备注（选填）", Content = notesContent, IsExpanded = showNotes,
                Margin = new Thickness(0, 18, 0, 0), Foreground = Theme.Ink, FontSize = 13 };
            AutomationProperties.SetName(notesSection, "展开或收起备注"); fields.Children.Add(notesSection);
            notes.TextChanged += delegate { UpdateNotesCount(); }; UpdateNotesCount();

            error = Theme.Text("", 12, Theme.Warning); error.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetName(error, "待办编辑错误提示");
            var errorArea = new StackPanel(); errorArea.Children.Add(error);
            recoverRecurrence = new Button { Content = "处理遗漏并保留输入…", Style = Theme.FlatButton(),
                HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Padding = new Thickness(0, 4, 8, 4) };
            AutomationProperties.SetName(recoverRecurrence, "保留草稿处理遗漏周期");
            recoverRecurrence.Click += delegate {
                if (RecoverRecurrenceHistory != null && RecoverRecurrenceHistory()) {
                    error.Text = "遗漏周期已处理，输入仍保留，请再次点击保存。";
                    recoverRecurrence.Visibility = Visibility.Collapsed;
                }
                else error.Text = "遗漏周期尚未处理完，输入仍保留，可继续处理或稍后重试。";
            };
            errorArea.Children.Add(recoverRecurrence);
            var errorScroll = new ScrollViewer { Content = errorArea, MaxHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            AutomationProperties.SetName(errorScroll, "待办保存错误详情"); Add(root, errorScroll, 2);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "取消", MinWidth = 70, Style = Theme.FlatButton(), IsCancel = true };
            var save = new Button { Content = "保存", MinWidth = 78, Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(16, 8, 16, 8), Style = Theme.PrimaryButton() };
            cancel.Click += delegate { DialogResult = false; };
            save.Click += delegate { Save(); };
            AutomationProperties.SetName(cancel, "取消编辑待办"); AutomationProperties.SetName(save, "保存待办");
            actions.Children.Add(cancel); actions.Children.Add(save); Add(root, actions, 3);
            TextBox initialFocus = focusNotes ? notes : taskText;
            FocusManager.SetFocusedElement(this, initialFocus);
            Loaded += delegate {
                initialFocus.Focus(); initialFocus.CaretIndex = initialFocus.Text.Length;
                if (focusNotes) initialFocus.BringIntoView();
            };
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, delegate(object sender, ExecutedRoutedEventArgs e) { Save(); e.Handled = true; }));
            InputBindings.Add(new KeyBinding(ApplicationCommands.Save, new KeyGesture(Key.Enter, ModifierKeys.Control)));
        }

        private static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }

        private static ComboBox TimePart(int count, int selected, string name)
        {
            var input = new ComboBox { Width = 60, IsEditable = true, IsTextSearchEnabled = false,
                StaysOpenOnEdit = true, MaxDropDownHeight = 180, Padding = new Thickness(6, 5, 3, 5),
                Background = Brushes.White, BorderBrush = Theme.Brush("#BDD0D8"), Foreground = Theme.Ink,
                VerticalContentAlignment = VerticalAlignment.Center };
            for (int value = 0; value < count; value++) input.Items.Add(value.ToString("00", CultureInfo.InvariantCulture));
            input.SelectedIndex = selected;
            AutomationProperties.SetName(input, name);
            return input;
        }

        private static bool TryTimePart(ComboBox input, int maximum, out int value)
        {
            string text = input.Text.Trim(); value = 0;
            if (text.Length < 1 || text.Length > 2) return false;
            for (int index = 0; index < text.Length; index++)
                if (text[index] < '0' || text[index] > '9') return false;
            return Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= maximum;
        }

        private static RadioButton PriorityOption(string label, TaskPriority priority, Style style)
        {
            var option = new RadioButton { Content = label, Tag = priority, GroupName = "task-priority", Style = style,
                Margin = new Thickness(priority == TaskPriority.High ? 0 : 5, 0, priority == TaskPriority.Low ? 0 : 5, 0) };
            AutomationProperties.SetName(option, "优先级：" + label); return option;
        }

        private static Style PriorityStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>
<Setter Property='Foreground' Value='#173D4F'/><Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#BDD0D8'/><Setter Property='Padding' Value='12,7'/><Setter Property='HorizontalContentAlignment' Value='Center'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'><Border x:Name='surface' CornerRadius='4' BorderThickness='1' BorderBrush='{TemplateBinding BorderBrush}' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' RecognizesAccessKey='True'/></Border>
<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#187C87'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter Property='Background' Value='#187C87'/><Setter Property='Foreground' Value='White'/><Setter Property='BorderBrush' Value='#187C87'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#0C5363'/><Setter TargetName='surface' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>");
        }

        private void UpdateNotesCount()
        {
            notesCount.Text = "仅在详情中显示 · " + notes.Text.Length.ToString(CultureInfo.InvariantCulture) + " / " + TodoRules.MaxNotesLength.ToString(CultureInfo.InvariantCulture);
            notesCount.Foreground = notes.Text.Length > TodoRules.MaxNotesLength ? Theme.Warning : Theme.Muted;
        }

        private void UpdateDeadlineFields()
        {
            bool enabled = useDeadline.IsChecked == true;
            deadlineFields.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            noDeadlineHint.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            UpdateEditorHeight();
        }

        private void UpdateEditorHeight()
        {
            double extra = useRecurrence != null && useRecurrence.IsChecked == true ? (recurrenceFrequency != null && recurrenceFrequency.SelectedIndex == 0 ? 118 : 172) : 0;
            Height = Math.Max(MinHeight, Math.Min(MaxHeight, fullEditorHeight - (useDeadline.IsChecked == true ? 0 : 85) + extra));
        }

        private void UpdateRecurrenceFields()
        {
            if (recurrenceFields == null || recurrenceSummary == null) return;
            recurrenceFields.Visibility = useRecurrence.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            weekDaysRow.Visibility = recurrenceFrequency.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            monthDayRow.Visibility = recurrenceFrequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            var options = ReadRecurrenceOptions();
            recurrenceSummary.Text = options == null ? "" : RecurringTasks.Label(options) + "生成一次";
            UpdateEditorHeight();
        }

        private RecurrenceOptions ReadRecurrenceOptions()
        {
            if (useRecurrence.IsChecked != true) return null;
            int selectedDays = 0;
            for (int day = 0; day < weekDays.Length; day++) if (weekDays[day] != null && weekDays[day].IsChecked == true) selectedDays |= 1 << day;
            return new RecurrenceOptions { Frequency = (RecurrenceFrequency)(recurrenceFrequency.SelectedIndex + 1),
                WeekDays = selectedDays, DayOfMonth = monthDay.SelectedIndex == 31 ? 31 : monthDay.SelectedIndex + 1,
                UseLastDay = monthDay.SelectedIndex == 31, StartDate = recurrenceStart.SelectedDate ?? DateTime.Today };
        }

        private static FrameworkElement RecurrenceField(string title, UIElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Theme.Text(title, 12, Theme.Muted); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            Grid.SetColumn(control, 1); row.Children.Add(control); return row;
        }

        private static Style WeekDayStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
<Setter Property='Foreground' Value='#173D4F'/><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#BDD0D8'/><Setter Property='HorizontalContentAlignment' Value='Center'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Border x:Name='surface' CornerRadius='4' Padding='2,6' BorderThickness='1' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'><ContentPresenter HorizontalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter Property='Background' Value='#187C87'/><Setter Property='BorderBrush' Value='#187C87'/><Setter Property='Foreground' Value='White'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#173D4F'/><Setter TargetName='surface' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        }

        private void Save()
        {
            string text = taskText.Text.Trim();
            if (text.Length == 0) { error.Text = "请填写待办事项。"; taskText.Focus(); return; }
            if (notes.Text.Length > TodoRules.MaxNotesLength)
            { error.Text = "备注最多可填写" + TodoRules.MaxNotesLength.ToString(CultureInfo.InvariantCulture) + "个字符，请精简后保存。"; notesSection.IsExpanded = true; notes.Focus(); notes.BringIntoView(); return; }
            bool hasDeadline = useDeadline.IsChecked == true;
            bool hasTime = hasDeadline && useTime.IsChecked == true;
            DateTime due = DateTime.MinValue;
            if (hasDeadline)
            {
                DateTime selected;
                if (String.IsNullOrWhiteSpace(date.Text) || !DateTime.TryParse(date.Text, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.AllowWhiteSpaces, out selected))
                { error.Text = "请选择有效的截止日期。"; date.Focus(); return; }
                due = selected.Date;
                if (hasTime)
                {
                    int selectedHour, selectedMinute;
                    if (!TryTimePart(hour, 23, out selectedHour))
                    { error.Text = "请选择或填写有效时刻：小时为00–23。"; hour.Focus(); return; }
                    if (!TryTimePart(minute, 59, out selectedMinute))
                    { error.Text = "请选择或填写有效时刻：分钟为00–59。"; minute.Focus(); return; }
                    hour.Text = selectedHour.ToString("00", CultureInfo.InvariantCulture);
                    minute.Text = selectedMinute.ToString("00", CultureInfo.InvariantCulture);
                    due = due.AddHours(selectedHour).AddMinutes(selectedMinute);
                }
            }
            // Keep the original precision and time kind when the visible deadline
            // fields were not changed, including legacy deadlines with seconds.
            if (hasDeadline && original != null && original.HasDeadline && hasTime == original.HasTime)
            {
                DateTime localOriginal = original.DueDate.Kind == DateTimeKind.Utc ? original.DueDate.ToLocalTime() : original.DueDate;
                if (due.Date == localOriginal.Date && (!hasTime || (due.Hour == localOriginal.Hour && due.Minute == localOriginal.Minute)))
                    due = original.DueDate;
            }
            TaskPriority priority = highPriority.IsChecked == true ? TaskPriority.High : lowPriority.IsChecked == true ? TaskPriority.Low : TaskPriority.Normal;
            RecurrenceOptions recurrence = ReadRecurrenceOptions();
            if (recurrence != null) {
                DateTime start;
                if (String.IsNullOrWhiteSpace(recurrenceStart.Text) || !DateTime.TryParse(recurrenceStart.Text, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.AllowWhiteSpaces, out start))
                { error.Text = "请选择有效的重复开始日期。"; recurrenceStart.Focus(); return; }
                recurrence.StartDate = start.Date;
                if (recurrence.Frequency == RecurrenceFrequency.Weekly && recurrence.WeekDays == 0)
                { error.Text = "请至少选择一个重复星期。"; weekDays[1].Focus(); return; }
                if (hasDeadline && due.Date < start.Date)
                { error.Text = "截止日期不能早于重复开始日期；也可取消设置截止日期。"; date.Focus(); return; }
            }
            var draft = new TaskDraft { Text = text, HasDeadline = hasDeadline, DueDate = due, HasTime = hasTime, Notes = notes.Text, Priority = priority, Recurrence = recurrence };
            // Keep the editable fields open until persistence succeeds, so a
            // transient disk failure cannot discard a long note or other input.
            if (saveDraft != null && !saveDraft(draft))
            {
                string detail = SaveErrorProvider == null ? null : SaveErrorProvider();
                error.Text = !String.IsNullOrWhiteSpace(detail) ? detail : editingRule ?
                    "后续规则未保存，输入仍保留。若有遗漏周期，请先在规则管理中处理后重试。" : "保存失败，输入仍保留，请重试。";
                recoverRecurrence.Visibility = RecoverRecurrenceHistory == null ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
            Result = draft;
            DialogResult = true;
        }
    }
}
