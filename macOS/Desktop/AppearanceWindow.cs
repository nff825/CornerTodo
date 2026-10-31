using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace CornerTodo.MacDesktop;

internal sealed class AppearanceWindow : Window
{
    private readonly Func<AppearanceSettings, bool> save;
    private readonly Action<AppearanceSettings> preview;
    private readonly Slider transparency;
    private readonly TextBlock amount, error;
    private readonly TextBox backgroundColor, primaryColor, secondaryColor;

    internal AppearanceWindow(AppearanceSettings original, Action<AppearanceSettings> previewAppearance, Func<AppearanceSettings, bool> saveAppearance)
    {
        preview = previewAppearance; save = saveAppearance;
        Title = "外观设置"; Width = 450; Height = 580; MinWidth = 320; MinHeight = 300;
        ShowInTaskbar = false; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; Ui.Style(this);
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        root.Children.Add(Ui.Text("外观设置", 23, true));
        var fields = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 5, 10) };
        var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        presets.Children.Add(Ui.Button("浅色", () => Apply(AppearanceSettings.CreateLight())));
        presets.Children.Add(Ui.Button("深色", () => Apply(AppearanceSettings.CreateDark())));
        presets.Children.Add(Ui.Button("原版", () => Apply(AppearanceSettings.CreateOriginal()))); fields.Children.Add(presets);
        amount = Ui.Text("背景透明度", 13); fields.Children.Add(amount);
        transparency = new Slider { Minimum = 0, Maximum = 100, Value = original.BackgroundTransparency, TickFrequency = 1, IsSnapToTickEnabled = true };
        fields.Children.Add(transparency);
        fields.Children.Add(Ui.Text("0%为实色，100%为全透明；文字保持不透明。", 11, brush: Ui.Muted));
        TextBox ColorInput(string label, string value)
        { fields.Children.Add(Ui.Text(label, 13)); var input = Ui.Input(label + "（#RRGGBB）", value, 7); fields.Children.Add(input); return input; }
        backgroundColor = ColorInput("背景颜色", original.BackgroundColor);
        primaryColor = ColorInput("主要文字颜色", original.PrimaryTextColor);
        secondaryColor = ColorInput("次要文字颜色", original.SecondaryTextColor);
        error = Ui.Text("", 12, brush: Ui.Warning); Grid.SetRow(error, 2); root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        buttons.Children.Add(Ui.Button("取消", () => Close(false)));
        buttons.Children.Add(Ui.Button("保存", Save, true)); Grid.SetRow(buttons, 3); root.Children.Add(buttons); Content = root;
        transparency.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Preview(); };
        backgroundColor.TextChanged += (_, _) => Preview(); primaryColor.TextChanged += (_, _) => Preview(); secondaryColor.TextChanged += (_, _) => Preview();
        amount.Text = "背景透明度 " + transparency.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private void Apply(AppearanceSettings value)
    { transparency.Value = value.BackgroundTransparency; backgroundColor.Text = value.BackgroundColor; primaryColor.Text = value.PrimaryTextColor; secondaryColor.Text = value.SecondaryTextColor; Preview(); }

    internal AppearanceSettings BuildCandidate()
    {
        string Color(TextBox input)
        {
            string text = input.Text?.Trim() ?? "";
            if (text.Length != 7 || text[0] != '#' || !text.Skip(1).All(Uri.IsHexDigit)) throw new InvalidOperationException("颜色请使用 #RRGGBB 格式，例如 #193C50。");
            return text.ToUpperInvariant();
        }
        return new AppearanceSettings { BackgroundTransparency = transparency.Value, BackgroundColor = Color(backgroundColor),
            PrimaryTextColor = Color(primaryColor), SecondaryTextColor = Color(secondaryColor) };
    }

    private void Preview()
    {
        amount.Text = "背景透明度 " + transparency.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
        try { preview(BuildCandidate()); error.Text = ""; }
        catch (Exception exception) { error.Text = exception.Message; }
    }

    private void Save()
    {
        try
        {
            if (!save(BuildCandidate())) { error.Text = "外观未保存，输入仍保留，请重试。"; return; }
            Close(true);
        }
        catch (Exception exception) { error.Text = exception.Message; }
    }
}
