using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace CornerTodo.MacDesktop;

internal static class Ui
{
    internal static readonly IBrush Ink = Brush.Parse("#193C50");
    internal static readonly IBrush Muted = Brush.Parse("#537486");
    internal static readonly IBrush Accent = Brush.Parse("#247C8C");
    internal static readonly IBrush Background = Brush.Parse("#F4F9FB");
    internal static readonly IBrush Warning = Brush.Parse("#A1463C");
    internal static readonly FontFamily Font = new("PingFang SC, Microsoft YaHei UI, Inter");

    internal static TextBlock Text(string text, double size = 14, bool bold = false, IBrush? brush = null) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = brush ?? Ink, FontFamily = Font, TextWrapping = TextWrapping.Wrap
    };

    internal static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7), MinHeight = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center, Cursor = new(Avalonia.Input.StandardCursorType.Hand) };
        if (primary) { button.Classes.Add("primary"); button.Background = Accent; button.Foreground = Brushes.White; }
        else { button.Foreground = Accent; button.Background = Brushes.Transparent; }
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, text);
        return button;
    }

    internal static TextBox Input(string name, string value, int? maximum = null) => new()
    {
        Text = value, Watermark = name, FontSize = 14, Padding = new Thickness(9, 7),
        Background = Brushes.White, Foreground = Ink, BorderBrush = Brush.Parse("#BDD0D8"),
        MaxLength = maximum ?? 0
    };

    internal static void Style(Window window)
    {
        window.FontFamily = Font; window.FontSize = 14;
        window.Foreground = Ink; window.Background = Background;
        window.Styles.Add(new Style(x => x.OfType<Button>().Class("primary").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        { Setters = { new Setter(ContentPresenter.BackgroundProperty, Accent), new Setter(ContentPresenter.ForegroundProperty, Brushes.White) } });
        window.Styles.Add(new Style(x => x.OfType<Button>().Class("primary").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        { Setters = { new Setter(ContentPresenter.BackgroundProperty, Brush.Parse("#1D6878")), new Setter(ContentPresenter.ForegroundProperty, Brushes.White) } });
    }

    internal static PixelRect WorkArea(Window window)
    {
        var area = (window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary)?.WorkingArea;
        return area.HasValue && area.Value.Width > 0 && area.Value.Height > 0 ? area.Value : new PixelRect(0, 0, 1440, 900);
    }

    internal static PanelBounds WorkAreaDip(Window window)
    {
        var area = WorkArea(window); double scale = Math.Max(0.1, window.RenderScaling);
        return new PanelBounds(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
    }

    internal static void FitDialog(Window dialog, Window owner)
    {
        var area = WorkArea(owner); var scale = Math.Max(0.1, owner.RenderScaling);
        dialog.MinWidth = Math.Min(dialog.MinWidth, Math.Max(1, area.Width / scale - 24));
        dialog.MinHeight = Math.Min(dialog.MinHeight, Math.Max(1, area.Height / scale - 24));
        dialog.MaxWidth = Math.Max(dialog.MinWidth, area.Width / scale - 24);
        dialog.MaxHeight = Math.Max(dialog.MinHeight, area.Height / scale - 24);
        dialog.Width = Math.Min(dialog.Width, dialog.MaxWidth); dialog.Height = Math.Min(dialog.Height, dialog.MaxHeight);
    }

    internal static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var dialog = new Window { Title = title, Width = 420, Height = 250, MinWidth = 280, MinHeight = 200,
            ShowInTaskbar = false, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Style(dialog); FitDialog(dialog, owner);
        var root = new Grid { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(Text(title, 22, true));
        var body = Text(message, 14); body.Margin = new Thickness(0, 15, 0, 12); Grid.SetRow(body, 1); root.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        buttons.Children.Add(Button("取消", () => dialog.Close(false)));
        buttons.Children.Add(Button("确认删除", () => dialog.Close(true), true));
        Grid.SetRow(buttons, 2); root.Children.Add(buttons); dialog.Content = root;
        return await dialog.ShowDialog<bool>(owner);
    }

    internal sealed class ActionCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}

/// <summary>Editable numeric input with an explicit menu containing every valid hour or minute.</summary>
internal sealed class TimePart : Border
{
    internal TextBox Input { get; }
    internal Button Choose { get; }
    private readonly int maximum;

    internal TimePart(int count, int initial, string name)
    {
        maximum = count - 1; Width = 60; MinHeight = 36;
        BorderBrush = Brush.Parse("#BDD0D8"); BorderThickness = new Thickness(1); Background = Brushes.White;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20") };
        Input = new TextBox { Text = initial.ToString("00", CultureInfo.InvariantCulture), MaxLength = 2,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Ui.Ink,
            Padding = new Thickness(6, 5), VerticalContentAlignment = VerticalAlignment.Center };
        Choose = new Button { Content = "⌄", Padding = new Thickness(2), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Foreground = Ui.Accent, HorizontalContentAlignment = HorizontalAlignment.Center };
        var values = new ContextMenu { MaxHeight = 260 };
        values.ItemsSource = Enumerable.Range(0, count).Select(value => new MenuItem {
            Header = value.ToString("00", CultureInfo.InvariantCulture),
            Command = new Ui.ActionCommand(() => Input.Text = value.ToString("00", CultureInfo.InvariantCulture))
        }).ToArray();
        Choose.Click += (_, _) => values.Open(Choose);
        Input.LostFocus += (_, _) => { if (TryValue(out var value)) Input.Text = value.ToString("00", CultureInfo.InvariantCulture); };
        AutomationProperties.SetName(Input, name); AutomationProperties.SetName(Choose, "点选" + name);
        Grid.SetColumn(Choose, 1); row.Children.Add(Input); row.Children.Add(Choose); Child = row;
    }

    internal bool TryValue(out int value)
    {
        value = 0; string text = Input.Text?.Trim() ?? "";
        return text.Length is >= 1 and <= 2 && text.All(c => c is >= '0' and <= '9') &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= maximum;
    }
}
