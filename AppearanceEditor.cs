using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CornerTodo
{
    /// <summary>
    /// A fixed, opaque light editor. Only values sent to preview affect the
    /// desktop panel. The caller owns restoring its saved appearance on cancel.
    /// </summary>
    internal sealed class AppearanceEditor : Window
    {
        private static readonly Brush EditorBackground = ColorBrush("#F4F9FB");
        private static readonly Brush Ink = ColorBrush("#193C50");
        private static readonly Brush Muted = ColorBrush("#537486");
        private static readonly Brush Accent = ColorBrush("#0F809B");
        private static readonly Brush Outline = ColorBrush("#BDD0D8");
        private static readonly Brush ErrorColor = ColorBrush("#A1463C");
        private readonly Action<AppearanceSettings> preview;
        private readonly Func<AppearanceSettings, bool> save;
        private readonly List<ColorControl> colorControls = new List<ColorControl>();
        private readonly Dictionary<string, PresetControl> presets = new Dictionary<string, PresetControl>();
        private readonly Slider transparency;
        private readonly TextBlock transparencyValue;
        private readonly TextBlock error;
        private AppearanceSettings current;
        private bool updating;

        internal AppearanceSettings CurrentAppearance { get { return current.Clone(); } }

        public AppearanceEditor(AppearanceSettings initial, Action<AppearanceSettings> preview,
            Func<AppearanceSettings, bool> save)
        {
            if (save == null) throw new ArgumentNullException("save");
            this.preview = preview;
            this.save = save;
            current = AppearanceSettings.Normalize(initial);
            updating = true;
            Title = "外观设置";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // A modal editor without a taskbar entry must not be minimized away.
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = EditorBackground;
            Foreground = Ink;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Rect workArea = SystemParameters.WorkArea;
            Width = Math.Min(600, Math.Max(280, workArea.Width - 32));
            Height = Math.Min(680, Math.Max(280, workArea.Height - 40));
            MinWidth = Math.Min(440, Width);
            MinHeight = Math.Min(340, Height);
            MaxHeight = Math.Max(MinHeight, workArea.Height - 16);

            Grid layout = new Grid { Margin = new Thickness(24, 20, 24, 18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = layout;
            StackPanel heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            TextBlock title = Text("外观设置", 28, Ink);
            title.FontWeight = FontWeights.Bold;
            heading.Children.Add(title);
            heading.Children.Add(Text("修改即时预览，保存后记住", 13, Muted));
            layout.Children.Add(heading);

            StackPanel body = new StackPanel { Margin = new Thickness(0, 0, 3, 0) };
            ScrollViewer scroll = new ScrollViewer { Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly };
            AutomationProperties.SetName(scroll, "外观设置内容");
            Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
            TextBlock presetHeading = Text("外观预设", 14, Ink);
            presetHeading.FontWeight = FontWeights.SemiBold;
            presetHeading.Margin = new Thickness(0, 0, 0, 9);
            body.Children.Add(presetHeading);
            Grid presetGrid = new Grid();
            for (int i = 0; i < 3; i++) presetGrid.ColumnDefinitions.Add(new ColumnDefinition());
            AddPreset(presetGrid, 0, "light", "清晰浅色");
            AddPreset(presetGrid, 1, "dark", "清晰深色");
            AddPreset(presetGrid, 2, "original", "原版通透");
            body.Children.Add(presetGrid);

            Grid transparencyGrid = new Grid { Margin = new Thickness(0, 22, 0, 19) };
            transparencyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            transparencyGrid.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock transparencyLabel = Text("背景透明度", 14, Ink);
            transparencyLabel.FontWeight = FontWeights.SemiBold;
            transparencyLabel.Margin = new Thickness(0, 1, 0, 0);
            transparencyGrid.Children.Add(transparencyLabel);
            StackPanel transparencyBody = new StackPanel();
            Grid.SetColumn(transparencyBody, 1); transparencyGrid.Children.Add(transparencyBody);
            transparencyValue = Text("", 14, Ink);
            AutomationProperties.SetName(transparencyValue, "背景透明度数值");
            transparencyBody.Children.Add(transparencyValue);
            transparency = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10,
                TickFrequency = 10, IsSnapToTickEnabled = false, Height = 28, Margin = new Thickness(0, 3, 0, 0) };
            AutomationProperties.SetName(transparency, "背景透明度");
            AutomationProperties.SetHelpText(transparency, "0% 为实色，100% 为全透明；只影响背景，不改变文字与按钮透明度。");
            transparency.ValueChanged += delegate {
                transparencyValue.Text = FormatTransparency(transparency.Value);
                if (!updating) ReadAndPreview();
            };
            transparencyBody.Children.Add(transparency);
            Grid endpoints = new Grid();
            endpoints.Children.Add(Text("0% 实色", 11, Muted));
            TextBlock transparentEnd = Text("100% 全透明", 11, Muted);
            transparentEnd.HorizontalAlignment = HorizontalAlignment.Right;
            endpoints.Children.Add(transparentEnd);
            transparencyBody.Children.Add(endpoints);
            TextBlock transparencyHint = Text("只影响背景，文字和按钮不变淡", 12, Muted);
            transparencyHint.Margin = new Thickness(0, 7, 0, 0);
            transparencyBody.Children.Add(transparencyHint);
            body.Children.Add(transparencyGrid);

            AddColor(body, "background", "面板底色", null);
            AddColor(body, "primary", "主要文字", "标题、事项");
            AddColor(body, "secondary", "次要文字", "普通日期、已完成");
            body.Children.Add(new Border { Height = 1, Background = ColorBrush("#D6E2E8"), Margin = new Thickness(0, 5, 0, 12) });
            TextBlock information = Text("ⓘ  紧急与逾期保留状态配色", 12, Muted);
            information.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(information);

            Grid footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            error = Text("", 12, ErrorColor);
            error.Visibility = Visibility.Collapsed;
            error.Margin = new Thickness(0, 0, 0, 9);
            AutomationProperties.SetName(error, "外观设置错误提示");
            Grid.SetColumnSpan(error, 2); footer.Children.Add(error);
            Button reset = Button("恢复默认", "恢复默认", false);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Background = Brushes.Transparent; reset.BorderThickness = new Thickness(0);
            reset.Padding = new Thickness(0, 7, 10, 7); reset.Foreground = Accent;
            reset.Click += delegate { SelectPreset("light"); };
            Grid.SetRow(reset, 1); footer.Children.Add(reset);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            Button cancel = Button("取消", "取消外观设置", false);
            cancel.IsCancel = true;
            cancel.Click += delegate { DialogResult = false; };
            Button saveButton = Button("保存", "保存外观设置", true);
            saveButton.Margin = new Thickness(10, 0, 0, 0);
            saveButton.Click += delegate { if (TrySave()) DialogResult = true; };
            actions.Children.Add(cancel); actions.Children.Add(saveButton);
            Grid.SetRow(actions, 1); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
            Grid.SetRow(footer, 2); layout.Children.Add(footer);
            ApplyAppearance(current, false);
        }

        internal void SelectPreset(string preset)
        {
            if (String.Equals(preset, "light", StringComparison.OrdinalIgnoreCase) || preset == "清晰浅色")
                ApplyAppearance(AppearanceSettings.CreateLight(), true);
            else if (String.Equals(preset, "dark", StringComparison.OrdinalIgnoreCase) || preset == "清晰深色")
                ApplyAppearance(AppearanceSettings.CreateDark(), true);
            else if (String.Equals(preset, "original", StringComparison.OrdinalIgnoreCase) || preset == "原版通透")
                ApplyAppearance(AppearanceSettings.CreateOriginal(), true);
            else throw new ArgumentException("Unknown appearance preset.", "preset");
        }

        internal bool TrySetColor(string role, string hex)
        {
            ColorControl control = ColorForRole(role);
            updating = true;
            try { control.Input.Text = hex ?? ""; }
            finally { updating = false; }
            return ReadAndPreview();
        }

        /// <summary>Validate and invoke the save callback. Closing is a button action.</summary>
        internal bool TrySave()
        {
            AppearanceSettings candidate;
            if (!ReadInputs(out candidate)) return false;
            current = candidate.Clone();
            try
            {
                if (!save(current.Clone()))
                {
                    ShowError("保存失败，外观尚未保存。请重试。");
                    return false;
                }
                HideError();
                return true;
            }
            catch (Exception exception)
            {
                ShowError("保存失败，外观尚未保存。" + exception.Message);
                return false;
            }
        }

        private void AddPreset(Grid parent, int column, string key, string label)
        {
            Button button = Button("", "预设：" + label, false);
            button.Height = 60;
            button.Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 2 ? 0 : 5, 0);
            button.Padding = new Thickness(13, 10, 10, 10);
            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock text = Text(label, 14, Ink);
            text.FontWeight = FontWeights.SemiBold; text.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(text);
            Border selected = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Accent,
                Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock tick = Text("✓", 14, Brushes.White);
            tick.HorizontalAlignment = HorizontalAlignment.Center; tick.VerticalAlignment = VerticalAlignment.Center;
            selected.Child = tick; Grid.SetColumn(selected, 1); content.Children.Add(selected);
            button.Content = content;
            button.Click += delegate { SelectPreset(key); };
            presets.Add(key, new PresetControl { Button = button, Selected = selected });
            Grid.SetColumn(button, column); parent.Children.Add(button);
        }

        private void AddColor(Panel parent, string role, string label, string description)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 13) };
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            TextBlock heading = Text(label, 14, Ink);
            heading.FontWeight = FontWeights.SemiBold; heading.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(heading);
            Border swatch = new Border { Width = 44, Height = 30, CornerRadius = new CornerRadius(4),
                BorderBrush = Outline, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(swatch, label + "色块");
            Grid.SetColumn(swatch, 1); row.Children.Add(swatch);
            TextBox input = new TextBox { MaxLength = 7, Height = 32, Padding = new Thickness(7, 5, 7, 4),
                Background = Brushes.White, Foreground = Ink, BorderBrush = Outline, BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            AutomationProperties.SetName(input, label + " HEX");
            AutomationProperties.SetHelpText(input, "输入六位 HEX 颜色，例如 #F4F9FB。");
            Grid.SetColumn(input, 3); row.Children.Add(input);
            ColorControl control = new ColorControl { Role = role, Label = label, Input = input, Swatch = swatch };
            colorControls.Add(control);
            input.TextChanged += delegate { if (!updating) ReadAndPreview(); };
            input.LostKeyboardFocus += delegate {
                string normalized;
                if (!TryHex(input.Text, out normalized)) return;
                updating = true;
                try { input.Text = normalized; }
                finally { updating = false; }
            };
            Button choose = Button("自定义…", "自定义" + label, false);
            choose.Padding = new Thickness(8, 5, 8, 5); choose.MinWidth = 0;
            choose.Click += delegate { ChooseColor(control); };
            Grid.SetColumn(choose, 5); row.Children.Add(choose);
            if (description != null)
            {
                TextBlock hint = Text(description, 12, Muted);
                hint.Margin = new Thickness(0, 5, 0, 0);
                Grid.SetRow(hint, 1); Grid.SetColumn(hint, 1); Grid.SetColumnSpan(hint, 5); row.Children.Add(hint);
            }
            parent.Children.Add(row);
        }

        private bool ReadAndPreview()
        {
            AppearanceSettings candidate;
            if (!ReadInputs(out candidate)) return false;
            current = candidate.Clone();
            RefreshSelection();
            PreviewCurrent();
            return true;
        }

        private bool ReadInputs(out AppearanceSettings candidate)
        {
            candidate = current.Clone();
            candidate.BackgroundTransparency = transparency.Value;
            string firstError = null;
            foreach (ColorControl control in colorControls)
            {
                string normalized;
                bool valid = TryHex(control.Input.Text, out normalized);
                control.Input.BorderBrush = valid ? Outline : ErrorColor;
                if (!valid)
                {
                    if (firstError == null) firstError = control.Label + "请输入 #RRGGBB 格式的颜色，例如 #F4F9FB。";
                    continue;
                }
                control.Swatch.Background = ColorBrush(normalized);
                SetColor(candidate, control.Role, normalized);
            }
            if (firstError != null) { ShowError(firstError); return false; }
            HideError();
            return true;
        }

        private void ApplyAppearance(AppearanceSettings value, bool notify)
        {
            current = AppearanceSettings.Normalize(value);
            updating = true;
            try
            {
                transparency.Value = current.BackgroundTransparency;
                transparencyValue.Text = FormatTransparency(current.BackgroundTransparency);
                foreach (ColorControl control in colorControls)
                {
                    string color = GetColor(current, control.Role);
                    control.Input.Text = color;
                    control.Input.BorderBrush = Outline;
                    control.Swatch.Background = ColorBrush(color);
                }
                RefreshSelection(); HideError();
            }
            finally { updating = false; }
            if (notify) PreviewCurrent();
        }

        private void RefreshSelection()
        {
            foreach (KeyValuePair<string, PresetControl> pair in presets)
            {
                AppearanceSettings preset = pair.Key == "light" ? AppearanceSettings.CreateLight() :
                    pair.Key == "dark" ? AppearanceSettings.CreateDark() : AppearanceSettings.CreateOriginal();
                bool selected = current.Matches(preset);
                pair.Value.Button.BorderBrush = selected ? Accent : Outline;
                pair.Value.Button.BorderThickness = new Thickness(selected ? 2 : 1);
                pair.Value.Selected.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void PreviewCurrent()
        {
            if (preview == null) return;
            try { preview(current.Clone()); }
            catch (Exception exception) { ShowError("预览暂未完成。" + exception.Message); }
        }

        private void ChooseColor(ColorControl control)
        {
            string normalized;
            if (!TryHex(control.Input.Text, out normalized)) normalized = GetColor(current, control.Role);
            using (Forms.ColorDialog dialog = new Forms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = Drawing.ColorTranslator.FromHtml(normalized);
                IntPtr owner = new WindowInteropHelper(this).Handle;
                Forms.DialogResult result = owner == IntPtr.Zero ? dialog.ShowDialog() : dialog.ShowDialog(new ColorDialogOwner(owner));
                if (result == Forms.DialogResult.OK)
                    TrySetColor(control.Role, "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2"));
            }
        }

        private ColorControl ColorForRole(string role)
        {
            foreach (ColorControl control in colorControls)
                if (String.Equals(role, control.Role, StringComparison.OrdinalIgnoreCase)) return control;
            throw new ArgumentException("Color role must be background, primary, or secondary.", "role");
        }
        private static string GetColor(AppearanceSettings value, string role)
        {
            return role == "background" ? value.BackgroundColor : role == "primary" ? value.PrimaryTextColor : value.SecondaryTextColor;
        }
        private static void SetColor(AppearanceSettings value, string role, string color)
        {
            if (role == "background") value.BackgroundColor = color;
            else if (role == "primary") value.PrimaryTextColor = color;
            else value.SecondaryTextColor = color;
        }
        private static bool TryHex(string text, out string normalized)
        {
            normalized = null;
            if (text == null) return false;
            string value = text.Trim();
            if (value.Length == 6) value = "#" + value;
            if (value.Length != 7 || value[0] != '#') return false;
            for (int i = 1; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i])) return false;
            normalized = value.ToUpperInvariant();
            return true;
        }
        private static string FormatTransparency(double value) { return value.ToString("0.#", CultureInfo.CurrentCulture) + "%"; }
        private void ShowError(string message) { error.Text = message; error.Visibility = Visibility.Visible; }
        private void HideError() { error.Text = ""; error.Visibility = Visibility.Collapsed; }
        private static Brush ColorBrush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static TextBlock Text(string value, double size, Brush brush)
        { return new TextBlock { Text = value, FontSize = size, Foreground = brush, TextWrapping = TextWrapping.Wrap }; }

        private static Button Button(string label, string automationName, bool primary)
        {
            Button button = new Button { Content = label, MinWidth = 82, Padding = new Thickness(15, 8, 15, 8),
                Background = primary ? Accent : Brushes.White, Foreground = primary ? Brushes.White : Ink,
                BorderBrush = primary ? Accent : Outline, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center };
            string hover = primary ? "#126C84" : "#E6F0F4";
            string pressed = primary ? "#10566A" : "#D9EAF0";
            button.Style = (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
                <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='5' Padding='{TemplateBinding Padding}'>
                  <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
                </Border><ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='" + hover + @"'/></Trigger>
                  <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='" + pressed + @"'/></Trigger>
                  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#0F809B'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            AutomationProperties.SetName(button, automationName);
            return button;
        }

        private sealed class ColorControl
        {
            public string Role, Label;
            public TextBox Input;
            public Border Swatch;
        }
        private sealed class PresetControl
        {
            public Button Button;
            public Border Selected;
        }
        private sealed class ColorDialogOwner : Forms.IWin32Window
        {
            public ColorDialogOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; private set; }
        }
    }
}
