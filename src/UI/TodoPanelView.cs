using System;
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
    internal sealed class TodoViewActions
    {
        public Action Add { get; set; }
        public Action ToggleCollapsed { get; set; }
        public Action ShowMenu { get; set; }
        public Action RevealFromEdge { get; set; }
        public Action ToggleCompletedSection { get; set; }
        public Func<TodoItem, bool, bool> SetCompleted { get; set; }
        public Action<TodoItem> Edit { get; set; }
        public Action<TodoItem> EditNotes { get; set; }
        public Func<TaskSortMode, bool> SetSortMode { get; set; }
        public Action<TodoItem> Delete { get; set; }
        public Action<ContextMenu> TrackMenu { get; set; }
    }

    /// <summary>Projects panel controls without owning application state or native window behavior.</summary>
    internal sealed class TodoPanelView
    {
        private readonly TaskListView taskList;
        private readonly TextBlock edgePending;
        private readonly TextBlock edgeOverdue;
        private readonly Border edgeSeparator;
        private bool edgeHidden;
        internal Button EdgeButton { get; private set; }
        internal Border Surface { get; private set; }
        internal Grid Root { get; private set; }
        internal Grid Header { get; private set; }
        internal ScrollViewer Scroll { get; private set; }
        internal Thumb Grip { get; private set; }
        internal Button AddButton { get; private set; }
        internal Button CollapseButton { get; private set; }
        internal Button MoreButton { get; private set; }
        internal TextBlock DateText { get; private set; }
        internal TextBlock AttentionText { get; private set; }
        internal TextBlock StatusText { get; private set; }
        internal TextBlock TitleText { get; private set; }
        internal Border ToolbarSurface { get; private set; }
        internal StackPanel List { get { return taskList.List; } }
        internal PanelPalette Palette { get; private set; }

        internal TodoPanelView(TodoViewActions actions)
        {
            if (actions == null) throw new ArgumentNullException("actions");
            Palette = new PanelPalette(AppearanceSettings.CreateLight());
            Surface = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(19, 15, 17, 12) };
            Root = new Grid(); Surface.Child = Root;
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Header = new Grid { Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
            Header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            TitleText = Theme.Text("待办清单", 25, Theme.Ink); TitleText.FontWeight = FontWeights.Bold;
            DateText = Theme.Text("", 12, Theme.Muted); DateText.Margin = new Thickness(0, 4, 0, 0);
            AttentionText = Theme.Text("", 12, Theme.Muted); AttentionText.Margin = new Thickness(0, 7, 0, 0);
            titles.Children.Add(TitleText); titles.Children.Add(DateText); titles.Children.Add(AttentionText); Header.Children.Add(titles);
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            CollapseButton = new Button { Content = "⌃", FontSize = 21, Style = Theme.FlatButton(), Width = 34, Height = 32, ToolTip = "收起面板" };
            CollapseButton.Click += delegate { if (actions.ToggleCollapsed != null) actions.ToggleCollapsed(); };
            MoreButton = new Button { Content = "⋯", FontSize = 22, Style = Theme.FlatButton(), Width = 34, Height = 32, ToolTip = "更多" };
            AutomationProperties.SetName(MoreButton, "更多操作");
            MoreButton.Click += delegate { if (actions.ShowMenu != null) actions.ShowMenu(); };
            toolbar.Children.Add(CollapseButton); toolbar.Children.Add(MoreButton);
            ToolbarSurface = new Border { Child = toolbar, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(ToolbarSurface, 1); Header.Children.Add(ToolbarSurface);
            Root.Children.Add(Header);
            taskList = new TaskListView(actions);
            Scroll = new ScrollViewer { Content = taskList.List, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            Grid.SetRow(Scroll, 1); Root.Children.Add(Scroll);
            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            AddButton = new Button { Content = "+  添加待办", FontSize = 15, Style = Theme.FlatButton(), HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0, 7, 12, 7) };
            AddButton.Click += delegate { if (actions.Add != null) actions.Add(); }; footer.Children.Add(AddButton);
            Grip = new Thumb { Width = 20, Height = 20, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Path Data='M 10,17 L 17,10 M 14,17 L 17,14' Stroke='#708797' StrokeThickness='1.2' Opacity='0.55'/></Grid></ControlTemplate>") };
            footer.Children.Add(Grip);
            Grid.SetRow(footer, 2); Root.Children.Add(footer);
            StatusText = Theme.Text("", 11, Theme.Warning); StatusText.Visibility = Visibility.Collapsed; StatusText.Margin = new Thickness(0, 5, 0, 0);
            Grid.SetRow(StatusText, 3); Root.Children.Add(StatusText);
            var edgeContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var edgeLabel = Theme.Text("待办", 12, Palette.Ink); edgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            edgePending = Theme.Text("0", 24, Palette.Ink); edgePending.HorizontalAlignment = HorizontalAlignment.Center;
            edgePending.FontWeight = FontWeights.SemiBold; edgePending.Margin = new Thickness(0, 4, 0, 5);
            edgeSeparator = new Border { Height = 1, Margin = new Thickness(4, 2, 4, 7), Background = Palette.Separator };
            edgeOverdue = Theme.Text("逾期 0", 10, Palette.Muted); edgeOverdue.HorizontalAlignment = HorizontalAlignment.Center;
            edgeContent.Children.Add(edgeLabel); edgeContent.Children.Add(edgePending);
            edgeContent.Children.Add(edgeSeparator); edgeContent.Children.Add(edgeOverdue);
            EdgeButton = new Button { Content = edgeContent, Padding = new Thickness(1, 7, 1, 7),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand, Focusable = false, Style = Palette.ButtonStyle };
            EdgeButton.Click += delegate { if (actions.RevealFromEdge != null) actions.RevealFromEdge(); };
            AutomationProperties.SetName(EdgeButton, "展开贴边待办");
        }

        internal void Render(TodoState state, DateTime now, bool preview, AppearanceSettings appearancePreview)
        {
            if (state == null) throw new ArgumentNullException("state");
            ApplyPalette(appearancePreview ?? state.Settings.Appearance);
            UpdateHeader(state, now, preview);
            double previousScroll = Scroll.VerticalOffset;
            taskList.Render(state, now, Palette);
            UpdateCollapsed(state, now);
            Scroll.ScrollToVerticalOffset(previousScroll);
        }

        internal void UpdateHeader(TodoState state, DateTime now, bool preview)
        {
            int overdue = TodoRules.OverdueCount(state, now);
            int urgent = TodoRules.UrgentCount(state, now);
            string summary = "逾期 " + overdue + " 项 · 6小时内 " + urgent + " 项";
            AttentionText.Text = summary;
            AttentionText.Foreground = overdue > 0 ? Palette.Warning : urgent > 0 ? Palette.Urgent : Palette.Muted;
            DateText.Text = state.Settings.Collapsed ? summary : now.ToString("M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN")) + (preview ? " · 示例" : "");
            int pending = TodoRules.Pending(state).Count();
            edgePending.Text = pending.ToString(CultureInfo.InvariantCulture);
            edgePending.FontSize = pending < 100 ? 24 : pending < 1000 ? 18 : 14;
            edgePending.MaxWidth = 36; edgePending.TextWrapping = TextWrapping.NoWrap;
            edgePending.TextTrimming = TextTrimming.CharacterEllipsis;
            edgeOverdue.Text = "逾期 " + overdue.ToString(CultureInfo.InvariantCulture);
            edgeOverdue.Foreground = overdue > 0 ? Palette.Warning : urgent > 0 ? Palette.Urgent : Palette.Muted;
            string edgeSummary = "未完成 " + pending + " 项，其中逾期 " + overdue + " 项；点击展开待办";
            EdgeButton.ToolTip = edgeSummary;
            AutomationProperties.SetName(EdgeButton, edgeSummary);
        }

        internal void SetEdgeHidden(bool hidden)
        {
            edgeHidden = hidden;
            Surface.Child = hidden ? (UIElement)EdgeButton : Root;
            Surface.Padding = hidden ? new Thickness(3, 4, 3, 4) : new Thickness(19, 15, 17, 12);
            Surface.CornerRadius = hidden ? new CornerRadius(16, 0, 0, 16) : new CornerRadius(12);
            Surface.BorderThickness = hidden ? new Thickness(1, 1, 0, 1) : new Thickness(0);
            // The handle must remain findable even with a fully transparent panel.
            Surface.Background = hidden ? Palette.ToolbarBackground : Palette.Background;
            Surface.BorderBrush = Palette.Separator;
        }

        internal void UpdateTimes(DateTime now) { taskList.UpdateTimes(now); }

        private void ApplyPalette(AppearanceSettings appearance)
        {
            Palette = new PanelPalette(appearance);
            Surface.Background = edgeHidden ? Palette.ToolbarBackground : Palette.Background;
            Surface.BorderBrush = Palette.Separator;
            TitleText.Foreground = Palette.Ink;
            DateText.Foreground = Palette.Muted;
            StatusText.Foreground = Palette.Warning;
            CollapseButton.Style = MoreButton.Style = AddButton.Style = Palette.ButtonStyle;
            ToolbarSurface.Background = Palette.ToolbarBackground;
            Grip.Template = Palette.ResizeTemplate;
            EdgeButton.Style = Palette.ButtonStyle;
            edgePending.Foreground = Palette.Ink;
            ((StackPanel)EdgeButton.Content).Children.OfType<TextBlock>().First().Foreground = Palette.Ink;
            edgeSeparator.Background = Palette.Separator;
        }

        internal void UpdateCollapsed(TodoState state, DateTime now)
        {
            bool collapsed = state.Settings.Collapsed;
            Scroll.Visibility = AddButton.Visibility = Grip.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            AttentionText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            Grip.IsEnabled = !state.Settings.AutoExpand || TodoRules.Expansion(state, now) <= 0.00001;
            Grip.Opacity = Grip.IsEnabled ? 1 : 0.3;
            Grip.ToolTip = Grip.IsEnabled ? "调整常态大小" : "当前按截止时间扩大；关闭自动扩展后可调整常态大小";
            CollapseButton.Content = collapsed ? "⌄" : "⌃"; CollapseButton.ToolTip = collapsed ? "展开面板" : "收起面板";
            AutomationProperties.SetName(CollapseButton, collapsed ? "展开面板" : "收起面板");
        }
    }
}
