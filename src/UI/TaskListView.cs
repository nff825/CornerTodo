using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerTodo
{
    /// <summary>Projects rows and forwards requests without editing task or settings objects.</summary>
    internal sealed class TaskListView
    {
        private readonly TodoViewActions actions;
        private readonly List<Action<DateTime>> timeRefreshers = new List<Action<DateTime>>();
        internal StackPanel List { get; private set; }

        internal TaskListView(TodoViewActions actions)
        {
            if (actions == null) throw new ArgumentNullException("actions");
            this.actions = actions;
            List = new StackPanel();
        }

        internal void Render(TodoState state, DateTime now, PanelPalette palette)
        {
            List.Children.Clear();
            timeRefreshers.Clear();
            var pending = TodoRules.Pending(state).ToList();
            var completed = TodoRules.Completed(state).ToList();
            var section = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            section.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            section.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heading = Theme.Text("未完成  " + pending.Count, 14, palette.Ink); heading.FontWeight = FontWeights.SemiBold;
            heading.VerticalAlignment = VerticalAlignment.Center; section.Children.Add(heading);
            var sorting = new Button { Content = SortLabel(state.Settings.SortMode) + " ▾", FontSize = 11,
                Style = palette.ButtonStyle, Foreground = palette.Muted, Padding = new Thickness(6, 5, 2, 5) };
            AutomationProperties.SetName(sorting, "排序方式");
            sorting.ToolTip = "选择未完成事项的排序方式";
            sorting.ContextMenu = SortMenu(state.Settings.SortMode, sorting);
            sorting.Click += delegate { sorting.ContextMenu.IsOpen = true; };
            Grid.SetColumn(sorting, 1); section.Children.Add(sorting); List.Children.Add(section);
            if (pending.Count == 0)
            {
                var empty = Theme.Text(state.Items.Count == 0 ? "把接下来要做的事，记在这里。" : "当前待办都已完成。", 14, palette.Muted);
                empty.Margin = new Thickness(0, 15, 0, 22); List.Children.Add(empty);
            }
            foreach (var item in pending) List.Children.Add(TaskRow(item, now, palette));
            List.Children.Add(new Border { Height = 1, Background = palette.Separator, Margin = new Thickness(0, 14, 0, 11) });
            var completedToggle = new Button { Content = (state.Settings.CompletedCollapsed ? "›  " : "⌄  ") + "已完成  " + completed.Count,
                Style = palette.ButtonStyle, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = palette.Muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 3, 10, 7) };
            AutomationProperties.SetName(completedToggle, state.Settings.CompletedCollapsed ? "展开已完成" : "收起已完成");
            completedToggle.Click += delegate { if (actions.ToggleCompletedSection != null) actions.ToggleCompletedSection(); };
            List.Children.Add(completedToggle);
            if (!state.Settings.CompletedCollapsed)
            {
                foreach (var item in completed) List.Children.Add(TaskRow(item, now, palette));
                if (completed.Count == 0)
                {
                    var empty = Theme.Text("完成的事项会保留在这里", 12, palette.Muted); empty.Margin = new Thickness(0, 5, 0, 10); List.Children.Add(empty);
                }
            }
        }

        private FrameworkElement TaskRow(TodoItem item, DateTime now, PanelPalette palette)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 7), Background = Brushes.Transparent, Tag = item.Id };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var check = new CheckBox { Style = palette.CheckStyle, IsChecked = item.IsCompleted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0) };
            AutomationProperties.SetName(check, (item.IsCompleted ? "恢复未完成：" : "完成：") + item.Text);
            check.Click += delegate {
                bool saved = actions.SetCompleted != null && actions.SetCompleted(item, !item.IsCompleted);
                if (!saved) check.IsChecked = item.IsCompleted;
            };
            row.Children.Add(check);
            var textArea = new StackPanel { Margin = new Thickness(0, 2, 5, 0) };
            bool overdue = TodoRules.IsOverdue(item, now);
            bool urgent = TodoRules.IsUrgent(item, now);
            var titleLine = new Grid();
            titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = Theme.Text(item.Text, overdue ? 21 : urgent ? 19 : 15, item.IsCompleted ? palette.Muted : palette.Ink);
            if (overdue || urgent) text.FontWeight = FontWeights.SemiBold;
            if (item.IsCompleted) text.TextDecorations = TextDecorations.Strikethrough;
            titleLine.Children.Add(text);
            var details = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(7, 1, 0, 0) };
            var priorityText = Theme.Text(TodoRules.PriorityLabel(item.Priority), 10,
                item.Priority == TaskPriority.High && !item.IsCompleted ? palette.Ink : palette.Muted);
            if (item.Priority == TaskPriority.High && !item.IsCompleted) priorityText.FontWeight = FontWeights.SemiBold;
            var priority = new Border { Child = priorityText, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 2, 0, 0),
                Background = Tint(palette.Ink, 16), Tag = "priority", ToolTip = "优先级：" + priorityText.Text };
            AutomationProperties.SetName(priority, "优先级：" + priorityText.Text); details.Children.Add(priority);
            if (!String.IsNullOrWhiteSpace(item.Notes)) {
                var note = new Button { Content = new System.Windows.Shapes.Path {
                    Data = Geometry.Parse("M 2,1 L 10,1 L 14,5 L 14,17 L 2,17 Z M 10,1 L 10,5 L 14,5 M 5,8 L 11,8 M 5,11 L 11,11 M 5,14 L 9,14"),
                    Stroke = palette.Muted, StrokeThickness = 1.2, Width = 14, Height = 18, Stretch = Stretch.Uniform },
                    Style = palette.ButtonStyle, Width = 26, Height = 26, Padding = new Thickness(5, 3, 5, 3),
                    Margin = new Thickness(3, 0, 0, 0), ToolTip = "查看备注" };
                AutomationProperties.SetName(note, "查看备注：" + item.Text);
                note.Click += delegate(object sender, RoutedEventArgs e) {
                    e.Handled = true;
                    if (actions.EditNotes != null) actions.EditNotes(item);
                    else if (actions.Edit != null) actions.Edit(item);
                };
                details.Children.Add(note);
            }
            Grid.SetColumn(details, 1); titleLine.Children.Add(details); textArea.Children.Add(titleLine);
            var due = Theme.Text(TodoRules.FormatAttention(item, now), overdue || urgent ? 13 : 12, overdue ? palette.Warning : urgent ? palette.Urgent : palette.Muted);
            due.Tag = "deadline";
            due.Margin = new Thickness(0, 4, 0, 0);
            textArea.Children.Add(due);
            if (!item.IsCompleted) textArea.Children.Add(TimeBar(item, now, palette));
            Grid.SetColumn(textArea, 1); row.Children.Add(textArea);
            DateTime localDue = item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
            string description = item.Text + "\n截止：" + localDue.ToString(item.HasTime ? "yyyy年M月d日 HH:mm" : "yyyy年M月d日");
            var rowTip = new ToolTip { Content = description + (item.IsCompleted ? "" : "\n" + ProgressLabel(item, now)) };
            row.ToolTip = rowTip;
            if (!item.IsCompleted) timeRefreshers.Add(delegate(DateTime current) {
                bool past = TodoRules.IsOverdue(item, current), soon = TodoRules.IsUrgent(item, current);
                due.Text = TodoRules.FormatAttention(item, current);
                due.Foreground = past ? palette.Warning : soon ? palette.Urgent : palette.Muted;
                rowTip.Content = description + "\n" + ProgressLabel(item, current);
            });
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "编辑" }; edit.Click += delegate { if (actions.Edit != null) actions.Edit(item); };
            var toggle = new MenuItem { Header = item.IsCompleted ? "恢复为未完成" : "标记为已完成" };
            toggle.Click += delegate { if (actions.SetCompleted != null) actions.SetCompleted(item, !item.IsCompleted); };
            var delete = new MenuItem { Header = "删除" }; delete.Click += delegate { if (actions.Delete != null) actions.Delete(item); };
            menu.Items.Add(edit); menu.Items.Add(toggle); menu.Items.Add(new Separator()); menu.Items.Add(delete);
            if (actions.TrackMenu != null) actions.TrackMenu(menu);
            row.ContextMenu = menu;
            textArea.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                DependencyObject target = e.OriginalSource as DependencyObject;
                while (target != null && target != textArea) {
                    if (target is ButtonBase) return;
                    target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
                }
                if (e.ClickCount == 2) { if (actions.Edit != null) actions.Edit(item); e.Handled = true; }
            };
            return row;
        }

        private ContextMenu SortMenu(TaskSortMode mode, Button target)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (TaskSortMode choice in new[] { TaskSortMode.Deadline, TaskSortMode.Priority }) {
                TaskSortMode value = choice;
                var option = new MenuItem { Header = SortLabel(value), IsCheckable = true, IsChecked = mode == value,
                    Tag = value, ToolTip = value == TaskSortMode.Priority ? "高 → 中 → 低，同级按截止时间排序" : "越早截止越靠前" };
                option.Click += delegate {
                    bool saved = actions.SetSortMode != null && actions.SetSortMode(value);
                    // State belongs to the coordinator. Keep the existing projection
                    // intact if persistence failed, including clicking the current mode.
                    foreach (MenuItem entry in menu.Items)
                        entry.IsChecked = (TaskSortMode)entry.Tag == (saved ? value : mode);
                };
                menu.Items.Add(option);
            }
            if (actions.TrackMenu != null) actions.TrackMenu(menu);
            return menu;
        }

        private static string SortLabel(TaskSortMode value)
        {
            return value == TaskSortMode.Priority ? "优先级优先" : "截止时间优先";
        }

        private static string ProgressLabel(TodoItem item, DateTime now)
        {
            // Round down until the actual deadline, so 99.9% never says it is due.
            double progress = TodoRules.TimeProgress(item, now);
            int percent = progress >= 1 ? 100 : (int)Math.Min(99, Math.Floor(progress * 100 + 0.00000001));
            return "时间已用 " + percent.ToString(CultureInfo.InvariantCulture) + "%（不是完成进度）";
        }

        internal void UpdateTimes(DateTime now)
        {
            // Update the current controls while hovered; preserve ordering, focus,
            // menus and scroll position until the coordinator can safely repaint.
            foreach (Action<DateTime> refresh in timeRefreshers) refresh(now);
        }

        private FrameworkElement TimeBar(TodoItem item, DateTime now, PanelPalette palette)
        {
            double progress = TodoRules.TimeProgress(item, now);
            Brush color = TodoRules.IsOverdue(item, now) ? palette.Warning : TodoRules.IsUrgent(item, now) ? palette.Urgent : palette.Accent;
            var tooltip = new ToolTip { Content = ProgressLabel(item, now) };
            var hitArea = new Grid { Height = 11, Background = Brushes.Transparent, Tag = "time-progress", ToolTip = tooltip };
            AutomationProperties.SetName(hitArea, item.Text + "：" + ProgressLabel(item, now));
            var track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5),
                VerticalAlignment = VerticalAlignment.Center, Background = Tint(palette.Muted, 35) };
            var fillLayout = new Grid { Height = 3, VerticalAlignment = VerticalAlignment.Center };
            fillLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(progress, GridUnitType.Star) });
            fillLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - progress, GridUnitType.Star) });
            var fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = color,
                Tag = "time-fill", Visibility = progress <= 0 ? Visibility.Hidden : Visibility.Visible };
            fillLayout.Children.Add(fill);
            hitArea.Children.Add(track); hitArea.Children.Add(fillLayout);
            timeRefreshers.Add(delegate(DateTime current) {
                double fraction = TodoRules.TimeProgress(item, current);
                fillLayout.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star);
                fillLayout.ColumnDefinitions[1].Width = new GridLength(1 - fraction, GridUnitType.Star);
                fill.Visibility = fraction <= 0 ? Visibility.Hidden : Visibility.Visible;
                fill.Background = TodoRules.IsOverdue(item, current) ? palette.Warning : TodoRules.IsUrgent(item, current) ? palette.Urgent : palette.Accent;
                tooltip.Content = ProgressLabel(item, current);
                AutomationProperties.SetName(hitArea, item.Text + "：" + ProgressLabel(item, current));
            });
            return hitArea;
        }

        private static Brush Tint(SolidColorBrush brush, byte alpha)
        {
            Color color = brush.Color;
            var result = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)); result.Freeze(); return result;
        }
    }
}
