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
    internal sealed class TodoViewActions
    {
        public Action Add { get; set; }
        public Action ToggleCollapsed { get; set; }
        public Action ShowMenu { get; set; }
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
        }

        internal void UpdateTimes(DateTime now) { taskList.UpdateTimes(now); }

        private void ApplyPalette(AppearanceSettings appearance)
        {
            Palette = new PanelPalette(appearance);
            Surface.Background = Palette.Background;
            TitleText.Foreground = Palette.Ink;
            DateText.Foreground = Palette.Muted;
            StatusText.Foreground = Palette.Warning;
            CollapseButton.Style = MoreButton.Style = AddButton.Style = Palette.ButtonStyle;
            ToolbarSurface.Background = Palette.ToolbarBackground;
            Grip.Template = Palette.ResizeTemplate;
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
