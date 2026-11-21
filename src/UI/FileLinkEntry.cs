using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CornerTodo
{
    /// <summary>A compact, file-system-free entry shared by every task projection.</summary>
    internal sealed class FileLinkEntry
    {
        private readonly Action<TodoItem, FrameworkElement> open;
        private readonly string automationPrefix;
        private readonly TextBlock count;
        private readonly System.Windows.Shapes.Path icon;
        private TodoItem item;
        internal Button Button { get; private set; }

        internal FileLinkEntry(Action<TodoItem, FrameworkElement> open, string automationPrefix)
        {
            this.open = open; this.automationPrefix = automationPrefix;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            icon = new System.Windows.Shapes.Path {
                Data = Geometry.Parse("M 6,5 L 11,0 A 3,3 0 0 1 15,4 L 6,13 A 4,4 0 0 1 0,7 L 9,-2 M 3,9 L 12,0"),
                StrokeThickness = 1.2, Width = 13, Height = 15, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center };
            count = Theme.Text("", 11, Theme.Accent); count.Margin = new Thickness(4, 0, 0, 0);
            count.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(icon); content.Children.Add(count);
            Button = new Button { Content = content, Padding = new Thickness(5, 3, 5, 3),
                MinHeight = 26, Margin = new Thickness(3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            Button.Click += delegate(object sender, RoutedEventArgs e) {
                e.Handled = true;
                if (this.open != null && item != null) this.open(item, Button);
            };
        }

        internal void Update(TodoItem value, PanelPalette palette)
        {
            item = value;
            int number = value.FileLinks == null ? 0 : value.FileLinks.Count;
            count.Text = number.ToString(CultureInfo.InvariantCulture);
            count.FontSize = 11 * palette.TextScale;
            count.Foreground = icon.Stroke = value.IsCompleted ? palette.Muted : palette.Accent;
            icon.Width = 13 * palette.TextScale; icon.Height = 15 * palette.TextScale;
            Button.Style = palette.ButtonStyle;
            Button.Visibility = number == 0 ? Visibility.Collapsed : Visibility.Visible;
            Button.IsEnabled = open != null;
            Button.ToolTip = "相关文件 " + number + " · 点击打开原文件列表";
            AutomationProperties.SetAutomationId(Button, automationPrefix + value.Id);
            AutomationProperties.SetName(Button, "相关文件 " + number + "：" + value.Text);
        }
    }
}
