using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerTodo
{
    /// <summary>Projects rows and forwards requests without editing task or settings objects.</summary>
    internal sealed class TaskListView
    {
        private readonly TodoViewActions actions;
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
            var pending = TodoRules.Pending(state).ToList();
            var completed = TodoRules.Completed(state).ToList();
            var heading = Theme.Text("未完成  " + pending.Count, 14, palette.Ink); heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 3, 0, 7); List.Children.Add(heading);
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
            var text = Theme.Text(item.Text, overdue ? 21 : urgent ? 19 : 15, item.IsCompleted ? palette.Muted : palette.Ink);
            if (overdue || urgent) text.FontWeight = FontWeights.SemiBold;
            if (item.IsCompleted) text.TextDecorations = TextDecorations.Strikethrough;
            textArea.Children.Add(text);
            var due = Theme.Text(TodoRules.FormatAttention(item, now), overdue || urgent ? 13 : 12, overdue ? palette.Warning : urgent ? palette.Urgent : palette.Muted);
            due.Margin = new Thickness(0, 4, 0, 0);
            textArea.Children.Add(due); Grid.SetColumn(textArea, 1); row.Children.Add(textArea);
            DateTime localDue = item.DueDate.Kind == DateTimeKind.Utc ? item.DueDate.ToLocalTime() : item.DueDate;
            row.ToolTip = item.Text + "\n截止：" + localDue.ToString(item.HasTime ? "yyyy年M月d日 HH:mm" : "yyyy年M月d日");
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "编辑" }; edit.Click += delegate { if (actions.Edit != null) actions.Edit(item); };
            var toggle = new MenuItem { Header = item.IsCompleted ? "恢复为未完成" : "标记为已完成" };
            toggle.Click += delegate { if (actions.SetCompleted != null) actions.SetCompleted(item, !item.IsCompleted); };
            var delete = new MenuItem { Header = "删除" }; delete.Click += delegate { if (actions.Delete != null) actions.Delete(item); };
            menu.Items.Add(edit); menu.Items.Add(toggle); menu.Items.Add(new Separator()); menu.Items.Add(delete);
            if (actions.TrackMenu != null) actions.TrackMenu(menu);
            row.ContextMenu = menu;
            textArea.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.ClickCount == 2) { if (actions.Edit != null) actions.Edit(item); e.Handled = true; }
            };
            return row;
        }
    }
}
