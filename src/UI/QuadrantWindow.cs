using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerTodo
{
    internal sealed class QuadrantWindow : Window
    {
        private readonly Action add;
        private readonly Action<TodoItem> edit;
        private readonly Action<TodoItem, FrameworkElement> openFiles;
        private readonly Func<TodoItem, bool, bool> setCompleted;
        private readonly Func<int, bool> setUrgentHours;
        private readonly TextBlock summary, rangeLabel, rule, status, hint;
        private readonly ComboBox urgentHours;
        private readonly CheckBox includeCompleted;
        private readonly Button addButton;
        private readonly ScrollViewer statusScroll;
        private readonly QuadrantSection[] sections = new QuadrantSection[4];
        private readonly Dictionary<string, TaskRow> rows = new Dictionary<string, TaskRow>(StringComparer.Ordinal);
        private TodoState state;
        private DateTime now;
        private PanelPalette palette;
        private AppearanceSettings appearance, appearancePreview;
        private bool updating;

        internal QuadrantWindow(TodoState state, DateTime now, Action add, Action<TodoItem> edit,
            Func<TodoItem, bool, bool> setCompleted, Func<int, bool> setUrgentHours,
            Action<TodoItem, FrameworkElement> openFiles = null)
        {
            this.add = add; this.edit = edit;
            this.openFiles = openFiles;
            this.setCompleted = setCompleted; this.setUrgentHours = setUrgentHours;
            Title = "CornerTodo · 四象限";
            Width = 1160; Height = 750; MinWidth = 460; MinHeight = 340;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            var root = new Grid { Margin = new Thickness(16, 12, 16, 12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var toolbarLayout = new Grid();
            toolbarLayout.ColumnDefinitions.Add(new ColumnDefinition());
            toolbarLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            toolbarLayout.Children.Add(toolbar);
            summary = Theme.Text("", 21, Theme.Ink);
            summary.FontWeight = FontWeights.SemiBold; summary.VerticalAlignment = VerticalAlignment.Center;
            summary.Margin = new Thickness(0, 0, 22, 7);
            AutomationProperties.SetAutomationId(summary, "QuadrantSummary"); toolbar.Children.Add(summary);

            var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 7) };
            rangeLabel = Theme.Text("紧急范围", 14, Theme.Ink);
            rangeLabel.VerticalAlignment = VerticalAlignment.Center; rangeLabel.Margin = new Thickness(0, 0, 8, 0);
            range.Children.Add(rangeLabel);
            urgentHours = new ComboBox { MinWidth = 115, Padding = new Thickness(7, 4, 7, 4), VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Theme.Ink, Background = Theme.Brush("#F4F9FB") };
            AutomationProperties.SetName(urgentHours, "四象限紧急范围");
            AutomationProperties.SetAutomationId(urgentHours, "QuadrantUrgentHours");
            AutomationProperties.SetHelpText(urgentHours, "逾期事项始终归为紧急，未设截止归为不紧急。");
            foreach (int hours in new[] { 6, 24, 72, 168 })
                urgentHours.Items.Add(new ComboBoxItem { Content = RangeLabel(hours) + "内", Tag = hours });
            urgentHours.SelectionChanged += delegate { ChangeUrgentHours(); };
            range.Children.Add(urgentHours); toolbar.Children.Add(range);

            includeCompleted = new CheckBox { Content = "显示已完成", VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(3, 4, 3, 4), Margin = new Thickness(0, 0, 18, 7) };
            AutomationProperties.SetAutomationId(includeCompleted, "QuadrantIncludeCompleted");
            AutomationProperties.SetHelpText(includeCompleted, "已完成事项按完成时刻归类。");
            includeCompleted.Checked += delegate { FilterChanged(); };
            includeCompleted.Unchecked += delegate { FilterChanged(); };
            toolbar.Children.Add(includeCompleted);

            addButton = new Button { Content = "+ 添加待办", Style = Theme.PrimaryButton(),
                Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(10, 0, 0, 7), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(addButton, "添加待办"); AutomationProperties.SetAutomationId(addButton, "QuadrantAdd");
            addButton.Click += delegate {
                SetStatus("");
                try { if (this.add != null) this.add(); }
                catch (Exception error) { SetStatus("未能打开添加待办。" + error.Message); }
            };
            Grid.SetColumn(addButton, 1); toolbarLayout.Children.Add(addButton); root.Children.Add(toolbarLayout);

            rule = Theme.Text("", 12, Theme.Muted); rule.Margin = new Thickness(0, 0, 0, 10);
            AutomationProperties.SetAutomationId(rule, "QuadrantRule");
            Grid.SetRow(rule, 1); root.Children.Add(rule);
            var board = new Grid { MinWidth = 760, MinHeight = 380 };
            board.ColumnDefinitions.Add(new ColumnDefinition()); board.ColumnDefinitions.Add(new ColumnDefinition());
            board.RowDefinitions.Add(new RowDefinition()); board.RowDefinitions.Add(new RowDefinition());
            var boardScroll = new ScrollViewer { Content = board, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            AutomationProperties.SetAutomationId(boardScroll, "QuadrantBoard");
            board.SetBinding(WidthProperty, new Binding("ViewportWidth") { Source = boardScroll });
            board.SetBinding(HeightProperty, new Binding("ViewportHeight") { Source = boardScroll });
            Grid.SetRow(boardScroll, 2); root.Children.Add(boardScroll);
            for (int index = 0; index < sections.Length; index++) {
                var section = new QuadrantSection((TaskQuadrant)index);
                section.Surface.Margin = new Thickness(index % 2 == 0 ? 0 : 5, index < 2 ? 0 : 5,
                    index % 2 == 0 ? 5 : 0, index < 2 ? 5 : 0);
                Grid.SetRow(section.Surface, index / 2); Grid.SetColumn(section.Surface, index % 2);
                board.Children.Add(section.Surface); sections[index] = section;
            }

            status = Theme.Text("", 12, Theme.Warning); status.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(status, "QuadrantStatus");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            statusScroll = new ScrollViewer { Content = status, MaxHeight = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 7, 0, 0) };
            Grid.SetRow(statusScroll, 3); root.Children.Add(statusScroll);
            hint = Theme.Text("点击圆圈完成 · 点击编辑修改事项", 11, Theme.Muted);
            hint.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(hint, 4); root.Children.Add(hint);
            Refresh(state, now);
        }

        internal void Refresh(TodoState state, DateTime now, AppearanceSettings preview = null)
        {
            if (state == null) throw new ArgumentNullException("state");
            this.state = state; this.now = now;
            appearancePreview = preview == null ? null : AppearanceSettings.Normalize(preview);
            bool floating = state.Settings != null && state.Settings.AlwaysOnTop;
            if (Topmost != floating) Topmost = floating;
            AppearanceSettings current = AppearanceSettings.Normalize(preview ?? (state.Settings == null ? null : state.Settings.Appearance));
            if (appearance == null || !appearance.Matches(current)) ApplyAppearance(current);
            int hours = SavedUrgentHours;
            updating = true;
            try {
                ComboBoxItem selected = urgentHours.SelectedItem as ComboBoxItem;
                if (selected == null || (int)selected.Tag != hours)
                    urgentHours.SelectedItem = urgentHours.Items.Cast<ComboBoxItem>().Single(option => (int)option.Tag == hours);
            }
            finally { updating = false; }
            rule.Text = "重要程度由你标记，逾期或" + RangeLabel(hours) + "内到期归为紧急；未设截止归为不紧急。";
            RenderItems(hours);
        }

        private int SavedUrgentHours
        {
            get { return QuadrantRules.NormalizeUrgentHours(state.Settings == null ? QuadrantRules.DefaultUrgentHours : state.Settings.QuadrantUrgentHours); }
        }

        private void ApplyAppearance(AppearanceSettings value)
        {
            appearance = value;
            var solid = value.Clone(); solid.BackgroundTransparency = 0;
            palette = new PanelPalette(solid);
            Background = palette.Background; Foreground = palette.Ink;
            summary.Foreground = rangeLabel.Foreground = palette.Ink;
            rule.Foreground = hint.Foreground = palette.Muted; status.Foreground = palette.Warning;
            includeCompleted.Foreground = palette.Ink;
            foreach (QuadrantSection section in sections) section.ApplyAppearance(palette);
        }

        private void RenderItems(int hours)
        {
            IList<TodoItem> items = (state.Items ?? new List<TodoItem>()).Where(item => item != null).ToList();
            summary.Text = "未完成 " + items.Count(item => !item.IsCompleted) + " 项";
            var groups = new List<TodoItem>[sections.Length];
            for (int index = 0; index < groups.Length; index++) groups[index] = new List<TodoItem>();
            var retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (TodoItem item in items.Where(item => includeCompleted.IsChecked == true || !item.IsCompleted)) {
                TodoItem copy = TodoStateCopy.Copy(item);
                groups[(int)QuadrantRules.Classify(copy, now, hours)].Add(copy);
                retained.Add(item.Id);
            }
            foreach (string id in rows.Keys.Where(id => !retained.Contains(id)).ToArray()) {
                TaskRow removed = rows[id];
                ((Panel)removed.Surface.Parent).Children.Remove(removed.Surface);
                rows.Remove(id);
            }
            IInputElement focused = Keyboard.FocusedElement;
            for (int index = 0; index < groups.Length; index++) {
                var ordered = groups[index].OrderBy(item => item.IsCompleted)
                    .ThenBy(item => Enum.IsDefined(typeof(TaskPriority), item.Priority) ? (int)item.Priority : (int)TaskPriority.Normal)
                    .ThenBy(TodoRules.Deadline).ThenBy(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
                sections[index].UpdateCount(ordered);
                for (int position = 0; position < ordered.Count; position++) {
                    TodoItem item = ordered[position];
                    TaskRow row;
                    if (!rows.TryGetValue(item.Id, out row)) {
                        row = new TaskRow(EditItem, CompleteItem, openFiles);
                        rows.Add(item.Id, row);
                    }
                    Panel currentParent = row.Surface.Parent as Panel;
                    int targetIndex = position + 1;
                    if (currentParent != sections[index].Rows || currentParent.Children.IndexOf(row.Surface) != targetIndex) {
                        if (currentParent != null) currentParent.Children.Remove(row.Surface);
                        sections[index].Rows.Children.Insert(targetIndex, row.Surface);
                    }
                    row.Update(item, now, palette, index == 0 || index == 2);
                }
            }
            if (focused != null && !Object.ReferenceEquals(Keyboard.FocusedElement, focused)) {
                var element = focused as UIElement;
                if (element != null && element.IsVisible && element.IsEnabled && IsAncestorOf(element)) Keyboard.Focus(focused);
            }
        }

        private void ChangeUrgentHours()
        {
            if (updating || state == null) return;
            ComboBoxItem selected = urgentHours.SelectedItem as ComboBoxItem;
            if (selected == null || (int)selected.Tag == SavedUrgentHours) return;
            int requested = (int)selected.Tag;
            bool saved = false;
            string error = "紧急范围未能保存，请重试。";
            try { saved = setUrgentHours != null && setUrgentHours(requested); }
            catch (Exception failure) { error += failure.Message; }
            if (saved) {
                SetStatus("");
                Refresh(state, now, appearancePreview);
            }
            else {
                updating = true;
                try { urgentHours.SelectedItem = urgentHours.Items.Cast<ComboBoxItem>().Single(option => (int)option.Tag == SavedUrgentHours); }
                finally { updating = false; }
                SetStatus(error);
            }
        }

        private void FilterChanged()
        {
            if (state == null) return;
            SetStatus(""); RenderItems(SavedUrgentHours);
        }

        private void EditItem(TodoItem item)
        {
            SetStatus("");
            try { if (edit != null) edit(item); }
            catch (Exception error) { SetStatus("未能打开编辑待办。" + error.Message); }
        }

        private void CompleteItem(TodoItem item, bool completed)
        {
            bool saved = false;
            string error = "完成状态未能保存，请重试。";
            try { saved = setCompleted != null && setCompleted(item, completed); }
            catch (Exception failure) { error += failure.Message; }
            SetStatus(saved ? "" : error);
        }

        private void SetStatus(string text)
        {
            status.Text = text;
            status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            statusScroll.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(status, text);
        }

        private static string RangeLabel(int hours)
        {
            return hours >= 24 && hours % 24 == 0 && hours != 24 ? hours / 24 + "天" : hours + "小时";
        }

        private static Brush Tint(Color color, byte opacity)
        {
            var brush = new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B));
            brush.Freeze(); return brush;
        }

        private sealed class QuadrantSection
        {
            internal readonly Border Surface;
            internal readonly StackPanel Rows;
            private readonly TextBlock title, count, empty;
            private readonly Border header, stripe;
            private readonly ScrollViewer scroll;
            private readonly TaskQuadrant quadrant;

            internal QuadrantSection(TaskQuadrant quadrant)
            {
                this.quadrant = quadrant;
                var content = new Grid();
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition());
                var heading = new Grid { Margin = new Thickness(13, 10, 13, 10) };
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                heading.ColumnDefinitions.Add(new ColumnDefinition());
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                stripe = new Border { Width = 4, Height = 23, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 11, 0) };
                heading.Children.Add(stripe);
                title = Theme.Text(QuadrantRules.Label(quadrant), 19, Theme.Ink); title.FontWeight = FontWeights.SemiBold;
                title.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(title, 1); heading.Children.Add(title);
                count = Theme.Text("", 13, Theme.Muted); count.Margin = new Thickness(9, 0, 0, 0);
                count.VerticalAlignment = VerticalAlignment.Center; count.TextAlignment = TextAlignment.Right;
                AutomationProperties.SetAutomationId(count, "QuadrantCount_" + (int)quadrant);
                Grid.SetColumn(count, 2); heading.Children.Add(count);
                header = new Border { Child = heading, CornerRadius = new CornerRadius(8, 8, 0, 0) }; content.Children.Add(header);
                Rows = new StackPanel();
                empty = Theme.Text("当前没有事项。", 12, Theme.Muted); empty.Margin = new Thickness(0, 10, 0, 10); Rows.Children.Add(empty);
                scroll = new ScrollViewer { Content = Rows, Style = Theme.PanelScrollViewer(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Margin = new Thickness(13, 2, 10, 8) };
                AutomationProperties.SetAutomationId(scroll, "QuadrantItems_" + (int)quadrant);
                AutomationProperties.SetName(scroll, QuadrantRules.Label(quadrant) + "事项");
                Grid.SetRow(scroll, 1); content.Children.Add(scroll);
                Surface = new Border { Child = content, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            }

            internal void ApplyAppearance(PanelPalette palette)
            {
                Color color = quadrant == TaskQuadrant.ImportantUrgent ? palette.Warning.Color :
                    quadrant == TaskQuadrant.ImportantNotUrgent ? palette.Accent.Color :
                    quadrant == TaskQuadrant.NotImportantUrgent ? palette.Urgent.Color : palette.Muted.Color;
                Surface.BorderBrush = Tint(color, 100); Surface.Background = Tint(color, 7);
                header.Background = Tint(color, 26); stripe.Background = Tint(color, 230);
                title.Foreground = palette.Ink; count.Foreground = empty.Foreground = palette.Muted;
                scroll.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : palette.Muted;
            }

            internal void UpdateCount(IList<TodoItem> items)
            {
                int completed = items.Count(item => item.IsCompleted);
                count.Text = (items.Count - completed).ToString(CultureInfo.InvariantCulture) +
                    (completed == 0 ? "" : " · 已完成 " + completed);
                empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private sealed class TaskRow
        {
            internal readonly Border Surface;
            private readonly CheckBox complete;
            private readonly TextBlock title, deadline;
            private readonly Button edit;
            private readonly FileLinkEntry files;
            private TodoItem item;

            internal TaskRow(Action<TodoItem> editItem, Action<TodoItem, bool> setCompleted,
                Action<TodoItem, FrameworkElement> openFiles)
            {
                var row = new Grid { Margin = new Thickness(0, 8, 0, 9) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                complete = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 5, 0) };
                complete.Click += delegate {
                    TodoItem current = item;
                    bool requested = complete.IsChecked == true;
                    complete.IsChecked = current.IsCompleted;
                    if (requested != current.IsCompleted) setCompleted(current, requested);
                };
                row.Children.Add(complete);
                var text = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
                title = Theme.Text("", 15, Theme.Ink); title.FontWeight = FontWeights.SemiBold;
                deadline = Theme.Text("", 12, Theme.Muted); deadline.Margin = new Thickness(0, 4, 0, 0);
                text.Children.Add(title); text.Children.Add(deadline);
                Grid.SetColumn(text, 1); row.Children.Add(text);
                edit = new Button { Content = "编辑", Padding = new Thickness(6, 4, 6, 4), FontSize = 12,
                    Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                edit.Click += delegate { editItem(item); };
                files = new FileLinkEntry(openFiles, "QuadrantFiles_");
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(files.Button); actions.Children.Add(edit);
                Grid.SetColumn(actions, 2); row.Children.Add(actions);
                Surface = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
            }

            internal void Update(TodoItem item, DateTime now, PanelPalette palette, bool urgent)
            {
                this.item = item;
                files.Update(item, palette);
                AutomationProperties.SetAutomationId(Surface, "QuadrantRow_" + item.Id);
                AutomationProperties.SetAutomationId(complete, "QuadrantComplete_" + item.Id);
                AutomationProperties.SetAutomationId(edit, "QuadrantEdit_" + item.Id);
                AutomationProperties.SetAutomationId(title, "QuadrantTitle_" + item.Id);
                AutomationProperties.SetAutomationId(deadline, "QuadrantDeadline_" + item.Id);
                AutomationProperties.SetName(complete, (item.IsCompleted ? "恢复未完成：" : "标记完成：") + item.Text);
                AutomationProperties.SetName(edit, "编辑待办：" + item.Text);
                complete.Style = palette.CheckStyle; complete.IsChecked = item.IsCompleted;
                edit.Style = palette.ButtonStyle;
                Surface.BorderBrush = palette.Separator;
                bool overdue = TodoRules.IsOverdue(item, now);
                title.Text = item.Text; title.FontSize = (overdue ? 17 : 15) * palette.TextScale;
                title.Foreground = item.IsCompleted ? palette.Muted : palette.Ink;
                title.TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null;
                deadline.Text = TodoRules.FormatAttention(item, now) + (item.IsCompleted ? " · 已完成" : "");
                deadline.FontSize = 12 * palette.TextScale;
                deadline.Foreground = overdue ? palette.Warning : urgent && !item.IsCompleted ? palette.Urgent : palette.Muted;
                complete.ToolTip = item.IsCompleted ? "恢复未完成" : "标记完成";
                Surface.ToolTip = item.Text + "\n" + deadline.Text;
            }
        }
    }
}
