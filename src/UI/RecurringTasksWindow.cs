using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CornerTodo
{
    /// <summary>Projects stored rules and forwards explicit commands; owns no task data.</summary>
    internal sealed class RecurringTasksWindow : Window
    {
        private readonly Func<TodoState> snapshot;
        private readonly Action<string> edit;
        private readonly Func<string, bool, bool> pause;
        private readonly Func<string, bool> stop;
        private readonly Action resolveMissed;
        private readonly Func<DateTime> clock;
        private readonly Func<RecurrenceSummary> getSummary;
        private readonly StackPanel rules;
        private readonly TextBlock status;
        private readonly TextBlock pending;
        private readonly Button resolve;

        internal RecurringTasksWindow(Func<TodoState> snapshot, Action<string> edit,
            Func<string, bool, bool> pause, Func<string, bool> stop, Action resolveMissed, Func<DateTime> clock = null,
            Func<RecurrenceSummary> getSummary = null)
        {
            this.snapshot = snapshot; this.edit = edit; this.pause = pause; this.stop = stop;
            this.resolveMissed = resolveMissed; this.clock = clock ?? delegate { return DateTime.Now; };
            this.getSummary = getSummary;
            Title = "重复任务管理"; Width = 560; Height = 530; MinWidth = 320; MinHeight = 280;
            Background = Theme.Brush("#F4F9FB"); Foreground = Theme.Ink; FontSize = 14;
            FontFamily = new FontFamily("Microsoft YaHei UI"); ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
            var root = new Grid { Margin = new Thickness(22, 18, 22, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var title = Theme.Text("重复规则", 22, Theme.Ink); title.FontWeight = FontWeights.SemiBold; root.Children.Add(title);
            var pendingRow = new Grid { Margin = new Thickness(0, 12, 0, 12) };
            pendingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pendingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pending = Theme.Text("", 12, Theme.Muted); pending.VerticalAlignment = VerticalAlignment.Center; pendingRow.Children.Add(pending);
            resolve = new Button { Content = "处理遗漏周期…", Style = Theme.FlatButton(), Padding = new Thickness(6, 5, 0, 5) };
            AutomationProperties.SetName(resolve, "处理遗漏周期");
            resolve.Click += delegate { if (this.resolveMissed != null) this.resolveMissed(); Refresh(); };
            Grid.SetColumn(resolve, 1); pendingRow.Children.Add(resolve); Grid.SetRow(pendingRow, 1); root.Children.Add(pendingRow);
            rules = new StackPanel();
            var scroll = new ScrollViewer { Content = rules, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            AutomationProperties.SetName(scroll, "重复规则列表"); Grid.SetRow(scroll, 2); root.Children.Add(scroll);
            status = Theme.Text("", 12, Theme.Warning); status.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetName(status, "重复规则操作提示"); Grid.SetRow(status, 3); root.Children.Add(status);
            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hint = Theme.Text("暂停或停止仅影响后续生成，已有事项保留。", 11, Theme.Muted);
            hint.Margin = new Thickness(0, 0, 10, 0); hint.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(hint);
            var close = new Button { Content = "关闭", IsCancel = true, Style = Theme.FlatButton(), MinWidth = 65 };
            AutomationProperties.SetName(close, "关闭重复任务管理"); close.Click += delegate { Close(); };
            Grid.SetColumn(close, 1); footer.Children.Add(close); Grid.SetRow(footer, 4); root.Children.Add(footer);
            Refresh();
        }

        internal void Refresh()
        {
            TodoState current = snapshot(); DateTime now = clock(); rules.Children.Clear();
            var active = current.RecurringRules.Where(rule => !rule.IsStopped).OrderBy(rule => rule.Text, StringComparer.CurrentCulture).ThenBy(rule => rule.Id, StringComparer.Ordinal).ToList();
            RecurrenceSummary summary = getSummary == null ? null : getSummary();
            long missed = summary == null ? 0 : summary.MissedCount;
            pending.Text = active.Count == 0 ? "暂无重复规则" : "共 " + active.Count + " 条规则" + (missed > 0 ? " · 有遗漏周期待处理" : "");
            resolve.IsEnabled = active.Count > 0 && (summary == null || summary.TotalCount > 0);
            if (active.Count == 0) {
                var empty = Theme.Text("添加或编辑普通事项时，勾选“重复任务”即可创建规则。", 13, Theme.Muted);
                empty.Margin = new Thickness(0, 20, 0, 20); rules.Children.Add(empty);
            }
            foreach (var rule in active) rules.Children.Add(RuleRow(rule, now));
        }

        private FrameworkElement RuleRow(RecurringTaskRule rule, DateTime now)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var content = new StackPanel { Margin = new Thickness(0, 5, 10, 9) };
            var title = Theme.Text(rule.Text, 14, Theme.Ink); title.FontWeight = FontWeights.SemiBold; content.Children.Add(title);
            var state = Theme.Text(rule.IsPaused ? "已暂停" : "已启用", 11, rule.IsPaused ? Theme.Muted : Theme.Accent);
            state.Margin = new Thickness(0, 3, 0, 2); content.Children.Add(state);
            string deadline = !rule.HasDeadline ? " · 未设截止" : " · 每期截止" + (rule.DeadlineOffsetDays == 0 ? "当天" : "顺延" + rule.DeadlineOffsetDays + "天") +
                (rule.HasTime ? " " + TimeSpan.FromTicks(rule.DeadlineTimeTicks).ToString(@"hh\:mm", CultureInfo.InvariantCulture) : "");
            content.Children.Add(Theme.Text(RecurringTasks.Label(rule.Recurrence) + deadline, 12, Theme.Muted));
            DateTime? next = rule.IsPaused ? null : RecurringTasks.NextOccurrence(rule, now);
            string nextText = rule.IsPaused ? "恢复后继续按计划生成" : next.HasValue ? "下次生成：" + next.Value.ToString(next.Value.Year == now.Year ? "M月d日" : "yyyy年M月d日", CultureInfo.InvariantCulture) : "没有后续日期";
            content.Children.Add(Theme.Text(nextText, 12, Theme.Muted)); row.Children.Add(content);
            var actions = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            AddAction(actions, "编辑", "编辑后续规则：" + rule.Text, delegate { if (edit != null) edit(rule.Id); Refresh(); });
            AddAction(actions, rule.IsPaused ? "恢复" : "暂停", "切换重复规则：" + rule.Text, delegate {
                bool success = pause != null && pause(rule.Id, !rule.IsPaused);
                status.Text = success ? "" : "操作未保存。若有遗漏周期，请先点击“处理遗漏周期”处理后重试。"; if (success) Refresh();
            });
            AddAction(actions, "停止…", "停止后续重复：" + rule.Text, delegate {
                bool success = stop != null && stop(rule.Id);
                status.Text = success ? "" : "尚未停止后续重复，已有规则保持原样。"; if (success) Refresh();
            });
            Grid.SetColumn(actions, 1); row.Children.Add(actions);
            return new Border { Child = row, BorderBrush = Theme.Brush("#D3E1E7"), BorderThickness = new Thickness(0, 0, 0, 1) };
        }

        private static void AddAction(Panel panel, string label, string name, Action action)
        {
            var button = new Button { Content = label, Style = Theme.FlatButton(), Padding = new Thickness(9, 3, 9, 3), MinWidth = 60 };
            AutomationProperties.SetName(button, name); button.Click += delegate { action(); }; panel.Children.Add(button);
        }
    }

    internal sealed class RecurrenceCatchUpWindow : Window
    {
        internal RecurrenceCatchUpWindow(long missed, int dueToday, Func<bool, bool> resolve)
        {
            Title = "处理遗漏周期"; Width = 480; Height = 290; MinWidth = 300; MinHeight = 220;
            Background = Theme.Brush("#F4F9FB"); Foreground = Theme.Ink; FontSize = 14;
            FontFamily = new FontFamily("Microsoft YaHei UI"); ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new Grid { Margin = new Thickness(22) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new StackPanel(); root.Children.Add(body);
            body.Children.Add(Theme.Text("有 " + missed + " 期历史计划尚未处理", 19, Theme.Ink));
            var text = Theme.Text("今天待生成 " + dueToday + " 项。可补建历史事项，或跳过遗漏，仅生成当期。已有事项和完成历史都会保留。每批最多补建366条，剩余可继续处理。", 13, Theme.Muted);
            text.Margin = new Thickness(0, 13, 0, 0); body.Children.Add(text);
            var error = Theme.Text("", 12, Theme.Warning); Grid.SetRow(error, 1); root.Children.Add(error);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var later = new Button { Content = "稍后处理", IsCancel = true, Style = Theme.FlatButton(), Margin = new Thickness(0, 0, 6, 0) };
            later.Click += delegate { Close(); }; AutomationProperties.SetName(later, "稍后处理遗漏周期"); buttons.Children.Add(later);
            foreach (bool include in new[] { false, true }) {
                bool includeMissed = include;
                var button = new Button { Content = includeMissed ? "补建遗漏" : "跳过遗漏", Style = includeMissed ? Theme.PrimaryButton() : Theme.FlatButton(), Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(12, 7, 12, 7) };
                AutomationProperties.SetName(button, includeMissed ? "补建遗漏周期" : "跳过遗漏周期");
                button.Click += delegate {
                    if (resolve(includeMissed)) { DialogResult = true; }
                    else error.Text = "保存失败，遗漏周期仍保留，请重试。";
                };
                buttons.Children.Add(button);
            }
            Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        }
    }
}
