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
        public readonly Style ButtonStyle;
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
            // Keep the menu/collapse targets visible even at 100% background transparency.
            ToolbarBackground = Frozen(dark ? Color.FromArgb(244, 24, 43, 56) : Color.FromArgb(244, 244, 249, 251));
            ButtonStyle = new Style(typeof(Button), Theme.FlatButton());
            ButtonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
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

        private static SolidColorBrush Hex(string value) { return Frozen((Color)ColorConverter.ConvertFromString(value)); }
        private static SolidColorBrush Frozen(Color value)
        {
            var brush = new SolidColorBrush(value); brush.Freeze(); return brush;
        }
    }
}
