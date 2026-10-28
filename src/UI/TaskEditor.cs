using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerTodo
{
    internal sealed class TaskEditor : Window
    {
        private readonly TextBox taskText;
        private readonly DatePicker date;
        private readonly CheckBox useTime;
        private readonly TextBox time;
        private readonly TextBox notes;
        private readonly Expander notesSection;
        private readonly TextBlock notesCount;
        private readonly RadioButton highPriority;
        private readonly RadioButton normalPriority;
        private readonly RadioButton lowPriority;
        private readonly TextBlock error;
        private readonly TodoItem original;
        private readonly Func<TaskDraft, bool> saveDraft;
        public TaskDraft Result { get; private set; }

        public TaskEditor(TodoItem item) : this(item, false) { }

        internal TaskEditor(TodoItem item, bool focusNotes) : this(item, focusNotes, null) { }

        internal TaskEditor(TodoItem item, bool focusNotes, Func<TaskDraft, bool> saveDraft)
        {
            original = item;
            this.saveDraft = saveDraft;
            DateTime displayedDue = item == null ? DateTime.Today :
                item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
            Title = item == null ? "添加待办" : "编辑待办";
            bool showNotes = focusNotes || (item != null && !String.IsNullOrEmpty(item.Notes));
            Width = 460; Height = showNotes ? 690 : 535;
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
            var dueLabel = Theme.Text("截止日期", 13, Theme.Muted); dueLabel.Margin = new Thickness(0, 14, 0, 7); fields.Children.Add(dueLabel);
            date = new DatePicker { SelectedDate = displayedDue.Date,
                SelectedDateFormat = DatePickerFormat.Long, FontSize = 14, Padding = new Thickness(5),
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN") };
            date.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; };
            AutomationProperties.SetName(date, "截止日期"); fields.Children.Add(date);
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            useTime = new CheckBox { Content = "设置具体时刻", IsChecked = item != null && item.HasTime, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(useTime, "设置具体时刻");
            time = new TextBox { Text = item != null && item.HasTime ? displayedDue.ToString("HH:mm") : "18:00", Width = 72,
                MaxLength = 5, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(14, 0, 0, 0),
                IsEnabled = useTime.IsChecked == true, BorderBrush = Theme.Brush("#BDD0D8") };
            AutomationProperties.SetName(time, "截止时刻，24小时制");
            useTime.Checked += delegate { time.IsEnabled = true; };
            useTime.Unchecked += delegate { time.IsEnabled = false; };
            timeRow.Children.Add(useTime); timeRow.Children.Add(time); fields.Children.Add(timeRow);
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
            AutomationProperties.SetName(error, "待办编辑错误提示"); Add(root, error, 2);
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

        private void Save()
        {
            string text = taskText.Text.Trim();
            if (text.Length == 0) { error.Text = "请填写待办事项。"; taskText.Focus(); return; }
            if (notes.Text.Length > TodoRules.MaxNotesLength)
            { error.Text = "备注最多可填写" + TodoRules.MaxNotesLength.ToString(CultureInfo.InvariantCulture) + "个字符，请精简后保存。"; notesSection.IsExpanded = true; notes.Focus(); notes.BringIntoView(); return; }
            DateTime selected;
            if (String.IsNullOrWhiteSpace(date.Text) || !DateTime.TryParse(date.Text, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.AllowWhiteSpaces, out selected))
            { error.Text = "请选择有效的截止日期。"; date.Focus(); return; }
            DateTime due = selected.Date;
            if (useTime.IsChecked == true)
            {
                TimeSpan value;
                if (!TimeSpan.TryParseExact(time.Text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out value) || value.TotalHours >= 24)
                { error.Text = "请按 09:30 的格式填写时刻（00:00–23:59）。"; time.Focus(); return; }
                due = due.Add(value);
            }
            // Keep the original precision and time kind when the visible deadline
            // fields were not changed, including legacy deadlines with seconds.
            bool hasTime = useTime.IsChecked == true;
            if (original != null && hasTime == original.HasTime)
            {
                DateTime localOriginal = original.DueDate.Kind == DateTimeKind.Utc ? original.DueDate.ToLocalTime() : original.DueDate;
                if (due.Date == localOriginal.Date && (!hasTime || (due.Hour == localOriginal.Hour && due.Minute == localOriginal.Minute)))
                    due = original.DueDate;
            }
            TaskPriority priority = highPriority.IsChecked == true ? TaskPriority.High : lowPriority.IsChecked == true ? TaskPriority.Low : TaskPriority.Normal;
            var draft = new TaskDraft { Text = text, DueDate = due, HasTime = hasTime, Notes = notes.Text, Priority = priority };
            // Keep the editable fields open until persistence succeeds, so a
            // transient disk failure cannot discard a long note or other input.
            if (saveDraft != null && !saveDraft(draft))
            { error.Text = "保存失败，输入仍保留，请重试。"; return; }
            Result = draft;
            DialogResult = true;
        }
    }
}
