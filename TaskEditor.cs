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
        private readonly TextBlock error;
        private readonly TodoItem original;
        public TodoItem Result { get; private set; }

        public TaskEditor(TodoItem item)
        {
            original = item;
            DateTime displayedDue = item == null ? DateTime.Today :
                item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
            Title = item == null ? "添加待办" : "编辑待办";
            Width = 430; Height = 425;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = Theme.Brush("#F4F9FB");
            Foreground = Theme.Ink;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            var root = new Grid { Margin = new Thickness(25, 18, 25, 20) };
            for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var heading = Theme.Text(Title, 23, Theme.Ink); heading.FontWeight = FontWeights.SemiBold;
            Add(root, heading, 0);
            var label = Theme.Text("事项", 13, Theme.Muted); label.Margin = new Thickness(0, 18, 0, 7); Add(root, label, 1);
            taskText = new TextBox { Text = item == null ? "" : item.Text, Height = 85, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MaxLength = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8), Background = Brushes.White, BorderBrush = Theme.Brush("#BDD0D8"), FontSize = 15 };
            AutomationProperties.SetName(taskText, "待办事项"); Add(root, taskText, 2);
            var dueLabel = Theme.Text("截止日期", 13, Theme.Muted); dueLabel.Margin = new Thickness(0, 17, 0, 7); Add(root, dueLabel, 3);
            date = new DatePicker { SelectedDate = displayedDue.Date,
                SelectedDateFormat = DatePickerFormat.Long, FontSize = 14, Padding = new Thickness(5),
                Language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN") };
            date.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; };
            AutomationProperties.SetName(date, "截止日期"); Add(root, date, 4);
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            useTime = new CheckBox { Content = "设置具体时刻", IsChecked = item != null && item.HasTime, VerticalAlignment = VerticalAlignment.Center };
            time = new TextBox { Text = item != null && item.HasTime ? displayedDue.ToString("HH:mm") : "18:00", Width = 72,
                MaxLength = 5, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(14, 0, 0, 0),
                IsEnabled = useTime.IsChecked == true, BorderBrush = Theme.Brush("#BDD0D8") };
            AutomationProperties.SetName(time, "截止时刻，24小时制");
            useTime.Checked += delegate { time.IsEnabled = true; };
            useTime.Unchecked += delegate { time.IsEnabled = false; };
            timeRow.Children.Add(useTime); timeRow.Children.Add(time); Add(root, timeRow, 5);
            error = Theme.Text("", 12, Theme.Warning); error.Margin = new Thickness(0, 8, 0, 0); Add(root, error, 6);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", MinWidth = 70, Style = Theme.FlatButton(), IsCancel = true };
            var save = new Button { Content = "保存", MinWidth = 78, Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(16, 8, 16, 8), Style = Theme.PrimaryButton() };
            cancel.Click += delegate { DialogResult = false; };
            save.Click += delegate { Save(); };
            actions.Children.Add(cancel); actions.Children.Add(save); Add(root, actions, 8);
            Loaded += delegate { taskText.Focus(); taskText.CaretIndex = taskText.Text.Length; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Save(); e.Handled = true; }
            };
        }

        private static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
        private void Save()
        {
            string text = taskText.Text.Trim();
            if (text.Length == 0) { error.Text = "请填写待办事项。"; taskText.Focus(); return; }
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
            DateTime savedAt = DateTime.Now;
            var proposedDeadline = new TodoItem { DueDate = due, HasTime = hasTime };
            bool deadlineChanged = original == null || TodoRules.Deadline(original) != TodoRules.Deadline(proposedDeadline);
            Result = new TodoItem { Id = original == null ? Guid.NewGuid().ToString("N") : original.Id,
                Text = text, DueDate = due, HasTime = hasTime,
                CreatedAt = original == null ? savedAt : original.CreatedAt,
                DeadlineSetAt = deadlineChanged ? (DateTime?)savedAt : original.DeadlineSetAt,
                IsCompleted = original != null && original.IsCompleted,
                CompletedAt = original == null ? null : original.CompletedAt };
            DialogResult = true;
        }
    }
}
