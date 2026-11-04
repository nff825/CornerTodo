using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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

    /// <summary>Panel-only action surfaces. Editor and native menu controls retain their own styles.</summary>
    internal sealed class ActionPalette
    {
        internal IBrush Fill { get; }
        internal IBrush Outline { get; }
        internal IBrush Hover { get; }
        internal IBrush Pressed { get; }
        internal IBrush Capsule { get; }
        internal IBrush Foreground { get; }
        internal ActionPalette(AppearanceSettings appearance)
        {
            var value = AppearanceSettings.Normalize(appearance);
            Color background = Color.Parse(value.BackgroundColor), muted = Color.Parse(value.SecondaryTextColor);
            bool dark = .2126 * background.R + .7152 * background.G + .0722 * background.B < 127.5;
            Color accent = Color.Parse(dark ? "#79D2E5" : "#2B8196");
            Color fill = Mix(background, muted, .18);
            byte alpha = (byte)Math.Max(180, Math.Round(255 * (1 - value.BackgroundTransparency / 100) * .8));
            Fill = WithAlpha(fill, alpha); Outline = WithAlpha(muted, 75);
            Hover = WithAlpha(Mix(fill, accent, .12), alpha);
            Pressed = WithAlpha(Mix(fill, accent, .22), alpha);
            Capsule = WithAlpha(fill, 244); Foreground = new SolidColorBrush(accent);
        }
    }

    internal static void ApplyPanelAction(Button button, ActionPalette palette, bool filled)
    {
        button.Background = filled ? palette.Fill : Brushes.Transparent;
        button.Foreground = palette.Foreground;
        button.BorderBrush = filled ? palette.Outline : Brushes.Transparent;
        button.BorderThickness = new Thickness(filled ? 1 : 0);
        button.Styles.Clear();
        button.Template = new FuncControlTemplate<Button>((parent, scope) => {
            var caption = new ContentPresenter { Name = "PART_ContentPresenter",
                HorizontalContentAlignment = parent.HorizontalContentAlignment, VerticalContentAlignment = VerticalAlignment.Center };
            scope.Register(caption.Name, caption);
            caption.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = parent });
            caption.Bind(ContentPresenter.ForegroundProperty, new Binding(nameof(Avalonia.Controls.Button.Foreground)) { Source = parent });
            var surface = new Border { Name = "ActionSurface", CornerRadius = new CornerRadius(8), Child = caption };
            scope.Register(surface.Name, surface);
            surface.Bind(Border.BackgroundProperty, new Binding(nameof(Avalonia.Controls.Button.Background)) { Source = parent, Priority = BindingPriority.Template });
            surface.Bind(Border.BorderBrushProperty, new Binding(nameof(Avalonia.Controls.Button.BorderBrush)) { Source = parent, Priority = BindingPriority.Template });
            surface.Bind(Border.BorderThicknessProperty, new Binding(nameof(Avalonia.Controls.Button.BorderThickness)) { Source = parent, Priority = BindingPriority.Template });
            surface.Bind(Border.PaddingProperty, new Binding(nameof(Avalonia.Controls.Button.Padding)) { Source = parent });
            return surface;
        });
        button.Styles.Add(new Style(s => s.OfType<Button>().Class(":pointerover").Template().OfType<Border>().Name("ActionSurface"))
        { Setters = { new Setter(Border.BackgroundProperty, filled ? palette.Hover : WithAlpha(((SolidColorBrush)palette.Foreground).Color, 40)) } });
        button.Styles.Add(new Style(s => s.OfType<Button>().Class(":pressed").Template().OfType<Border>().Name("ActionSurface"))
        { Setters = { new Setter(Border.BackgroundProperty, filled ? palette.Pressed : WithAlpha(((SolidColorBrush)palette.Foreground).Color, 65)) } });
        button.Styles.Add(new Style(s => s.OfType<Button>().Class(":focus-visible").Template().OfType<Border>().Name("ActionSurface"))
        { Setters = { new Setter(Border.BorderBrushProperty, palette.Foreground), new Setter(Border.BorderThicknessProperty, new Thickness(1)) } });
    }

    /// <summary>Retains the real Avalonia ScrollBar/Track mechanics and a 14-DIP drag target around a 5-DIP indicator.</summary>
    internal static void StylePanelScroll(ScrollViewer viewer)
    {
        viewer.Styles.Add(new Style(s => s.OfType<ScrollBar>().Class(":vertical")) {
            Setters = {
                new Setter(ScrollBar.WidthProperty, 14d), new Setter(ScrollBar.MinWidthProperty, 14d),
                new Setter(ScrollBar.MaxWidthProperty, 14d), new Setter(ScrollBar.AllowAutoHideProperty, false),
                new Setter(ScrollBar.BackgroundProperty, Brushes.Transparent),
                new Setter(ScrollBar.TemplateProperty, new FuncControlTemplate<ScrollBar>((bar, scope) => {
                    var thumb = new Thumb { Name = "PART_Thumb", MinHeight = 24, Width = 14, Background = Brushes.Transparent };
                    scope.Register(thumb.Name, thumb);
                    thumb.Template = new FuncControlTemplate<Thumb>((_, thumbScope) => {
                        var indicator = new Border { Name = "ScrollIndicator", Width = 5, CornerRadius = new CornerRadius(2.5),
                            Margin = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Center };
                        indicator.SetValue(Visual.OpacityProperty, .55, BindingPriority.Template);
                        thumbScope.Register(indicator.Name, indicator);
                        indicator.Bind(Border.BackgroundProperty, new Binding(nameof(ScrollViewer.Foreground)) { Source = viewer });
                        var hit = new Grid { Background = Brushes.Transparent }; hit.Children.Add(indicator); return hit;
                    });
                    thumb.Styles.Add(new Style(s => s.OfType<Thumb>().Class(":pointerover").Template().OfType<Border>().Name("ScrollIndicator"))
                    { Setters = { new Setter(Border.OpacityProperty, .85) } });
                    thumb.Styles.Add(new Style(s => s.OfType<Thumb>().Class(":pressed").Template().OfType<Border>().Name("ScrollIndicator"))
                    { Setters = { new Setter(Border.OpacityProperty, 1d) } });
                    RepeatButton Page(string name) {
                        var button = new RepeatButton { Name = name, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false };
                        scope.Register(name, button);
                        button.Template = new FuncControlTemplate<RepeatButton>((_, _) => new Border { Background = Brushes.Transparent });
                        return button;
                    }
                    var track = new Track { Name = "PART_Track", Orientation = Orientation.Vertical, IsDirectionReversed = true,
                        Thumb = thumb, DecreaseButton = Page("PART_PageUpButton"), IncreaseButton = Page("PART_PageDownButton") };
                    scope.Register(track.Name, track);
                    track.Bind(Track.MinimumProperty, new Binding(nameof(ScrollBar.Minimum)) { Source = bar });
                    track.Bind(Track.MaximumProperty, new Binding(nameof(ScrollBar.Maximum)) { Source = bar });
                    track.Bind(Track.ViewportSizeProperty, new Binding(nameof(ScrollBar.ViewportSize)) { Source = bar });
                    track.Bind(Track.ValueProperty, new Binding(nameof(ScrollBar.Value)) { Source = bar, Mode = BindingMode.TwoWay });
                    return track;
                }))
            }
        });
    }

    private static Color Mix(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R * (1 - amount) + second.R * amount),
        (byte)Math.Round(first.G * (1 - amount) + second.G * amount),
        (byte)Math.Round(first.B * (1 - amount) + second.B * amount));
    private static SolidColorBrush WithAlpha(Color color, byte alpha) => new(Color.FromArgb(alpha, color.R, color.G, color.B));

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
