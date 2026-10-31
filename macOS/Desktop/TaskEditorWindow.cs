using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace CornerTodo.MacDesktop;

internal sealed class TaskEditorWindow : Window
{
    private readonly TodoItem? original;
    private readonly Func<TaskDraft, bool> save;
    internal TextBox TaskText { get; }
    internal TextBox Notes { get; }
    internal CalendarDatePicker Date { get; }
    internal CheckBox UseTime { get; }
    internal TimePart Hour { get; }
    internal TimePart Minute { get; }
    private readonly RadioButton high, normal, low;
    private readonly TextBlock error;
    private readonly Expander notesSection;

    internal TaskEditorWindow(TodoItem? item, Func<TaskDraft, bool> saveDraft, bool focusNotes = false)
    {
        original = item; save = saveDraft;
        Title = item == null ? "添加待办" : "编辑待办";
        bool expandNotes = focusNotes || !string.IsNullOrEmpty(item?.Notes);
        Width = 460; Height = expandNotes ? 690 : 550; MinWidth = 320; MinHeight = 300;
        ShowInTaskbar = false; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.Style(this);
        var root = new Grid { Margin = new Thickness(25, 18, 25, 20), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        root.Children.Add(Ui.Text(Title, 23, true));
        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 6, 4) };
        var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        fields.Children.Add(Ui.Text("事项", 13, brush: Ui.Muted));
        TaskText = Ui.Input("待办事项", item?.Text ?? "", Math.Max(500, item?.Text?.Length ?? 0)); TaskText.Height = 76;
        TaskText.AcceptsReturn = true; TaskText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        AutomationProperties.SetName(TaskText, "待办事项"); fields.Children.Add(TaskText);
        var due = item?.DueDate ?? DateTime.Today;
        if (due.Kind == DateTimeKind.Utc) due = due.ToLocalTime();
        var dateLabel = Ui.Text("截止日期", 13, brush: Ui.Muted); dateLabel.Margin = new Thickness(0, 8, 0, 0); fields.Children.Add(dateLabel);
        Date = new CalendarDatePicker { SelectedDate = due.Date, SelectedDateFormat = CalendarDatePickerFormat.Long,
            HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "选择截止日期" };
        AutomationProperties.SetName(Date, "截止日期"); fields.Children.Add(Date);
        UseTime = new CheckBox { Content = "设置具体时刻", IsChecked = item?.HasTime == true, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(UseTime, "设置具体时刻");
        Hour = new TimePart(24, item?.HasTime == true ? due.Hour : 18, "截止小时，24小时制");
        Minute = new TimePart(60, item?.HasTime == true ? due.Minute : 0, "截止分钟");
        Hour.IsEnabled = Minute.IsEnabled = UseTime.IsChecked == true;
        UseTime.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Hour.IsEnabled = Minute.IsEnabled = UseTime.IsChecked == true; };
        var time = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(0, 3, 0, 0) };
        time.Children.Add(UseTime); time.Children.Add(Hour);
        var colon = Ui.Text(":", 16); colon.VerticalAlignment = VerticalAlignment.Center; time.Children.Add(colon); time.Children.Add(Minute); fields.Children.Add(time);
        var priorityLabel = Ui.Text("优先级", 13, brush: Ui.Muted); priorityLabel.Margin = new Thickness(0, 8, 0, 0); fields.Children.Add(priorityLabel);
        var priorities = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12 };
        string group = "priority-" + Guid.NewGuid().ToString("N");
        RadioButton Option(string label, int column)
        {
            var radio = new RadioButton { Content = label, GroupName = group, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(radio, "优先级：" + label); Grid.SetColumn(radio, column); priorities.Children.Add(radio); return radio;
        }
        high = Option("高", 0); normal = Option("中", 1); low = Option("低", 2);
        high.IsChecked = item?.Priority == TaskPriority.High; low.IsChecked = item?.Priority == TaskPriority.Low;
        normal.IsChecked = high.IsChecked != true && low.IsChecked != true; fields.Children.Add(priorities);
        Notes = Ui.Input("备注，选填", item?.Notes ?? "", Math.Max(TodoRules.MaxNotesLength, item?.Notes?.Length ?? 0)); Notes.Height = 120; Notes.AcceptsReturn = true; Notes.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        AutomationProperties.SetName(Notes, "备注，选填");
        var count = Ui.Text("", 11, brush: Ui.Muted);
        void UpdateCount() => count.Text = "仅在详情中显示 · " + (Notes.Text?.Length ?? 0) + " / " + TodoRules.MaxNotesLength;
        Notes.TextChanged += (_, _) => UpdateCount(); UpdateCount();
        var notesContent = new StackPanel { Spacing = 6 }; notesContent.Children.Add(Notes); notesContent.Children.Add(count);
        notesSection = new Expander { Header = "备注（选填）", Content = notesContent, IsExpanded = expandNotes, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        fields.Children.Add(notesSection);
        error = Ui.Text("", 12, brush: Ui.Warning); error.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(error, 2); root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Ui.Button("取消", () => Close(false)));
        buttons.Children.Add(Ui.Button("保存", Save, true)); Grid.SetRow(buttons, 3); root.Children.Add(buttons); Content = root;
        Opened += (_, _) => { var input = focusNotes ? Notes : TaskText; input.Focus(); input.CaretIndex = input.Text?.Length ?? 0; };
        KeyDown += (_, e) => {
            if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
            if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { Save(); e.Handled = true; }
        };
    }

    internal TaskDraft BuildDraft()
    {
        string text = TaskText.Text?.Trim() ?? "";
        if (text.Length == 0) throw new InvalidOperationException("请填写待办事项。");
        string notes = Notes.Text ?? "";
        if (notes.Length > TodoRules.MaxNotesLength) { notesSection.IsExpanded = true; throw new InvalidOperationException("备注最多可填写5000个字符，请精简后保存。"); }
        DateTime selected;
        if (!string.IsNullOrWhiteSpace(Date.Text))
        {
            // The calendar's text edit can precede its SelectedDate update.
            // Save the date the user sees, rather than an older selection.
            if (!TryParseEditorDate(Date.Text, out selected))
                throw new InvalidOperationException("请选择有效的截止日期。");
        }
        else if (Date.SelectedDate.HasValue) selected = Date.SelectedDate.Value;
        else throw new InvalidOperationException("请选择有效的截止日期。");
        DateTime due = selected.Date;
        bool hasTime = UseTime.IsChecked == true;
        if (hasTime)
        {
            if (!Hour.TryValue(out var hour) || !Minute.TryValue(out var minute))
                throw new InvalidOperationException("请选择有效时刻：小时00–23，分钟00–59。");
            due = due.AddHours(hour).AddMinutes(minute);
        }
        if (original != null && hasTime == original.HasTime)
        {
            var local = original.DueDate.Kind == DateTimeKind.Utc ? original.DueDate.ToLocalTime() : original.DueDate;
            if (due.Date == local.Date && (!hasTime || (due.Hour == local.Hour && due.Minute == local.Minute))) due = original.DueDate;
        }
        return new TaskDraft { Text = text, Notes = notes, DueDate = due, HasTime = hasTime,
            Priority = high.IsChecked == true ? TaskPriority.High : low.IsChecked == true ? TaskPriority.Low : TaskPriority.Normal };
    }

    internal static bool TryParseEditorDate(string? text, out DateTime selected)
    {
        selected = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cultures = new[] { CultureInfo.CurrentCulture, CultureInfo.GetCultureInfo("zh-CN"), CultureInfo.GetCultureInfo("en-US") };
        // CalendarDatePicker's long format can have no separator before the
        // weekday (2026年11月2日星期一). General TryParse rejects that valid
        // display string, so accept the control's exact localized patterns first.
        foreach (var culture in cultures)
        {
            var patterns = new[] { culture.DateTimeFormat.LongDatePattern, culture.DateTimeFormat.ShortDatePattern,
                "yyyy年M月d日dddd", "yyyy年M月d日 dddd", "yyyy-MM-dd", "yyyy/M/d" };
            if (DateTime.TryParseExact(text, patterns, culture, DateTimeStyles.AllowWhiteSpaces, out selected)) return true;
        }
        foreach (var culture in cultures)
            if (DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out selected)) return true;
        selected = default;
        return false;
    }

    private void Save()
    {
        try
        {
            var draft = BuildDraft();
            if (!save(draft)) { error.Text = "保存失败，输入仍保留，请重试。"; return; }
            Close(true);
        }
        catch (Exception exception) { error.Text = exception.Message; }
    }
}
