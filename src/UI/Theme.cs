using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace CornerTodo
{
    internal static class Theme
    {
        public static readonly Brush Ink = Brush("#193C50");
        public static readonly Brush Muted = Brush("#537486");
        public static readonly Brush Accent = Brush("#2B8196");
        public static readonly Brush Warning = Brush("#A1463C");
        public static readonly Brush Urgent = Brush("#98651D");
        public static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        public static TextBlock Text(string text, double size, Brush color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };
        }
        // Local to the main panel. Keep standard ScrollViewer/Track commands,
        // viewport sizing and input handling; only replace their presentation.
        public static Style PanelScrollViewer()
        {
            return (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollViewer'>
<Setter Property='Background' Value='Transparent'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'>
<ControlTemplate.Resources>
  <Style x:Key='PanelScrollBar' TargetType='ScrollBar'>
    <Setter Property='Width' Value='14'/><Setter Property='MinWidth' Value='14'/><Setter Property='MaxWidth' Value='14'/>
    <Setter Property='Background' Value='Transparent'/><Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Grid Background='Transparent'>
        <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'
               Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
               Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
          <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'>
            <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
          </RepeatButton></Track.DecreaseRepeatButton>
          <Track.Thumb><Thumb x:Name='PanelThumb' MinHeight='24' Foreground='{TemplateBinding Foreground}' Focusable='False'>
            <Thumb.Template><ControlTemplate TargetType='Thumb'>
              <Grid Background='Transparent'><Border x:Name='Indicator' Width='5' CornerRadius='2.5' Margin='0,2' Background='{TemplateBinding Foreground}' Opacity='0.55'/></Grid>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Indicator' Property='Opacity' Value='0.85'/></Trigger>
                <Trigger Property='IsDragging' Value='True'><Setter TargetName='Indicator' Property='Opacity' Value='1'/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Thumb.Template>
          </Thumb></Track.Thumb>
          <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'>
            <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
          </RepeatButton></Track.IncreaseRepeatButton>
        </Track>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ControlTemplate.Resources>
<Grid Background='{TemplateBinding Background}'>
  <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
  <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Grid.Column='0' Margin='0,0,3,0' ClipToBounds='True'
      CanContentScroll='{TemplateBinding CanContentScroll}' Content='{TemplateBinding Content}'
      ContentTemplate='{TemplateBinding ContentTemplate}'/>
  <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Style='{StaticResource PanelScrollBar}'
      Foreground='{TemplateBinding Foreground}' Minimum='0' Maximum='{TemplateBinding ScrollableHeight}'
      ViewportSize='{TemplateBinding ViewportHeight}' Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}'
      Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
</Grid>
</ControlTemplate></Setter.Value></Setter></Style>");
        }
        public static Style FlatButton()
        {
            return (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
              <Setter Property='Foreground' Value='#2B8196'/><Setter Property='Background' Value='Transparent'/>
              <Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='9,6'/><Setter Property='Cursor' Value='Hand'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
                <Border x:Name='Surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
                <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border>
                <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#26FFFFFF'/></Trigger>
                  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='Background' Value='#40FFFFFF'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        }
        public static Style PrimaryButton()
        {
            return (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
              <Setter Property='Foreground' Value='White'/><Setter Property='Background' Value='#2B8196'/>
              <Setter Property='BorderBrush' Value='#2B8196'/><Setter Property='BorderThickness' Value='1'/>
              <Setter Property='Padding' Value='16,8'/><Setter Property='Cursor' Value='Hand'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
                <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
                  <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#247487'/><Setter TargetName='Surface' Property='BorderBrush' Value='#247487'/></Trigger>
                  <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#1D6172'/><Setter TargetName='Surface' Property='BorderBrush' Value='#1D6172'/></Trigger>
                  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#153D50'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/><Setter Property='Cursor' Value='Arrow'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter></Style>");
        }
        public static Style CircleCheck()
        {
            return (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
              <Setter Property='Cursor' Value='Hand'/><Setter Property='Width' Value='28'/><Setter Property='Height' Value='30'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
                <Grid Background='Transparent'><Ellipse x:Name='Circle' Width='20' Height='20' HorizontalAlignment='Left' Stroke='#628697' StrokeThickness='1.2' Fill='#10FFFFFF'/>
                <Path x:Name='Tick' Data='M 5,10 L 9,14 L 16,6' Stroke='White' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/></Grid>
                <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Circle' Property='Fill' Value='#2B8196'/><Setter TargetName='Circle' Property='Stroke' Value='#2B8196'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Circle' Property='StrokeThickness' Value='2'/></Trigger>
                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Circle' Property='StrokeThickness' Value='2.5'/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter></Style>");
        }
    }

    // Per-panel brushes; editor/menu styles remain independent and fully opaque.
    internal sealed class PanelPalette
    {
        public readonly SolidColorBrush Background;
        public readonly SolidColorBrush Ink;
        public readonly SolidColorBrush Muted;
        public readonly SolidColorBrush Accent;
        public readonly SolidColorBrush Warning;
        public readonly SolidColorBrush Urgent;
        public readonly SolidColorBrush Separator;
        public readonly SolidColorBrush ToolbarBackground;
        public readonly SolidColorBrush ToolbarBorder;
        public readonly SolidColorBrush CapsuleBackground;
        public readonly Style ButtonStyle;
        public readonly Style ActionButtonStyle;
        public readonly Style CheckStyle;
        public readonly ControlTemplate ResizeTemplate;

        public PanelPalette(AppearanceSettings appearance)
        {
            AppearanceSettings value = AppearanceSettings.Normalize(appearance);
            Color backgroundColor = (Color)ColorConverter.ConvertFromString(value.BackgroundColor);
            byte alpha = (byte)Math.Round(255 * (1 - value.BackgroundTransparency / 100.0), MidpointRounding.AwayFromZero);
            Background = Frozen(Color.FromArgb(alpha, backgroundColor.R, backgroundColor.G, backgroundColor.B));
            Ink = Hex(value.PrimaryTextColor); Muted = Hex(value.SecondaryTextColor);
            bool dark = (0.2126 * backgroundColor.R + 0.7152 * backgroundColor.G + 0.0722 * backgroundColor.B) < 127.5;
            Accent = Hex(dark ? "#79D2E5" : "#2B8196");
            Warning = Hex(dark ? "#FFB4AB" : "#A1463C");
            Urgent = Hex(dark ? "#F6CA75" : "#98651D");
            Separator = Frozen(dark ? Color.FromArgb(55, 255, 255, 255) : Color.FromArgb(45, 25, 60, 80));
            // Use one restrained, theme-derived surface for both toolbar and add
            // action. A minimum fill alpha keeps both entry points findable even
            // when the user makes the main panel completely transparent.
            Color actionColor = Mix(backgroundColor, Muted.Color, 0.18);
            byte actionAlpha = (byte)Math.Max(180, Math.Round(alpha * 0.8));
            ToolbarBackground = Frozen(Color.FromArgb(actionAlpha, actionColor.R, actionColor.G, actionColor.B));
            ToolbarBorder = Frozen(Color.FromArgb(75, Muted.Color.R, Muted.Color.G, Muted.Color.B));
            // The narrow edge handle needs stronger contrast against arbitrary
            // wallpapers; retain its prior opacity independently of action fills.
            CapsuleBackground = Frozen(Color.FromArgb(244, actionColor.R, actionColor.G, actionColor.B));
            Color hoverColor = Mix(actionColor, Accent.Color, 0.12);
            Color pressedColor = Mix(actionColor, Accent.Color, 0.22);
            ButtonStyle = PanelButton(Brushes.Transparent, Brushes.Transparent,
                Frozen(Color.FromArgb(42, Accent.Color.R, Accent.Color.G, Accent.Color.B)),
                Frozen(Color.FromArgb(68, Accent.Color.R, Accent.Color.G, Accent.Color.B)));
            ActionButtonStyle = PanelButton(ToolbarBackground, ToolbarBorder,
                Frozen(Color.FromArgb(actionAlpha, hoverColor.R, hoverColor.G, hoverColor.B)),
                Frozen(Color.FromArgb(actionAlpha, pressedColor.R, pressedColor.G, pressedColor.B)));
            string outline = Muted.Color.ToString();
            string accent = Accent.Color.ToString();
            string tick = dark ? "#182B38" : "#FFFFFF";
            CheckStyle = (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
              <Setter Property='Cursor' Value='Hand'/><Setter Property='Width' Value='28'/><Setter Property='Height' Value='30'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
                <Grid Background='Transparent'><Ellipse x:Name='Circle' Width='20' Height='20' HorizontalAlignment='Left' Stroke='" + outline + @"' StrokeThickness='1.2' Fill='#01FFFFFF'/>
                <Path x:Name='Tick' Data='M 5,10 L 9,14 L 16,6' Stroke='" + tick + @"' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/></Grid>
                <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Circle' Property='Fill' Value='" + accent + @"'/><Setter TargetName='Circle' Property='Stroke' Value='" + accent + @"'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Circle' Property='StrokeThickness' Value='2'/></Trigger>
                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Circle' Property='StrokeThickness' Value='2.5'/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter></Style>");
            ResizeTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Path Data='M 10,17 L 17,10 M 14,17 L 17,14' Stroke='" + outline + @"' StrokeThickness='1.2' Opacity='0.75'/></Grid></ControlTemplate>");
        }

        private Style PanelButton(Brush background, Brush border, Brush hover, Brush pressed)
        {
            var style = new Style(typeof(Button), Theme.FlatButton());
            style.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
              <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='{TemplateBinding Padding}'>
                <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='" + hover.ToString() + @"'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='" + pressed.ToString() + @"'/></Trigger>
                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='" + Accent.ToString() + @"'/></Trigger>
                <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/><Setter Property='Cursor' Value='Arrow'/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>")));
            return style;
        }

        private static Color Mix(Color first, Color second, double amount)
        {
            return Color.FromRgb((byte)Math.Round(first.R * (1 - amount) + second.R * amount),
                (byte)Math.Round(first.G * (1 - amount) + second.G * amount),
                (byte)Math.Round(first.B * (1 - amount) + second.B * amount));
        }

        private static SolidColorBrush Hex(string value) { return Frozen((Color)ColorConverter.ConvertFromString(value)); }
        private static SolidColorBrush Frozen(Color value)
        {
            var brush = new SolidColorBrush(value); brush.Freeze(); return brush;
        }
    }
}
