using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerTodo
{
    /// <summary>A saved list of original file locations for one task.</summary>
    internal sealed class FileLinksWindow : Window
    {
        private readonly Func<List<string>, bool> saveLinks;
        private readonly IFileLinkService service;
        private readonly Border surface;
        private readonly TextBlock heading, taskTitle, empty, notice, error;
        private readonly StackPanel rowsPanel;
        private readonly ScrollViewer fileScroll, errorScroll;
        private readonly Button add, close;
        private readonly Dictionary<string, FileRow> rows = new Dictionary<string, FileRow>(StringComparer.Ordinal);
        private List<string> links = new List<string>();
        private AppearanceSettings appearance;
        private PanelPalette palette;

        internal FileLinksWindow(TodoItem item, Func<List<string>, bool> saveLinks, IFileLinkService service = null)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (saveLinks == null) throw new ArgumentNullException("saveLinks");
            this.saveLinks = saveLinks;
            this.service = service ?? new WindowsFileLinkService();
            Title = "CornerTodo · 相关文件";
            Width = 480; Height = 360; MinWidth = 320; MinHeight = 260;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            var root = new Grid { Margin = new Thickness(18, 15, 18, 15) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface = new Border { Child = root, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                Margin = new Thickness(4), Padding = new Thickness(0) };
            Content = surface;

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            heading = Theme.Text("", 20, Theme.Ink); heading.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetAutomationId(heading, "FileLinksHeading"); header.Children.Add(heading);
            taskTitle = Theme.Text("", 12, Theme.Muted); taskTitle.Margin = new Thickness(0, 5, 0, 0);
            taskTitle.MaxHeight = 40; taskTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetAutomationId(taskTitle, "FileLinksTaskTitle"); header.Children.Add(taskTitle);
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args) {
                if (args.LeftButton == MouseButtonState.Pressed) DragMove();
            };
            root.Children.Add(header);

            rowsPanel = new StackPanel();
            empty = Theme.Text("尚未关联文件。", 14, Theme.Muted); empty.Margin = new Thickness(0, 15, 0, 20);
            AutomationProperties.SetAutomationId(empty, "FileLinksEmpty"); rowsPanel.Children.Add(empty);
            fileScroll = new ScrollViewer { Content = rowsPanel, Style = Theme.PanelScrollViewer(), Focusable = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetAutomationId(fileScroll, "FileLinksList");
            AutomationProperties.SetName(fileScroll, "相关文件列表");
            Grid.SetRow(fileScroll, 1); root.Children.Add(fileScroll);

            error = Theme.Text("", 12, Theme.Warning); error.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(error, "FileLinksError");
            AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
            errorScroll = new ScrollViewer { Content = error, MaxHeight = 55, Margin = new Thickness(0, 7, 0, 0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed, Focusable = false };
            Grid.SetRow(errorScroll, 2); root.Children.Add(errorScroll);

            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            add = new Button { Content = "+ 关联本地文件…", HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(11, 7, 11, 7), MinHeight = 34 };
            AutomationProperties.SetAutomationId(add, "FileLinksAdd"); AutomationProperties.SetName(add, "关联本地文件");
            add.Click += delegate { AddFiles(); }; footer.Children.Add(add);
            close = new Button { Content = "关闭", IsCancel = true, Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(8, 0, 0, 0), MinHeight = 34 };
            AutomationProperties.SetAutomationId(close, "FileLinksClose"); AutomationProperties.SetName(close, "关闭相关文件");
            close.Click += delegate { Close(); }; Grid.SetColumn(close, 1); footer.Children.Add(close);
            notice = Theme.Text("仅保存文件位置，原文件仍保留在原处。", 11, Theme.Muted);
            notice.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(notice, 1); Grid.SetColumnSpan(notice, 2); footer.Children.Add(notice);
            Grid.SetRow(footer, 3); root.Children.Add(footer);
            Refresh(item);
        }

        internal List<string> CurrentLinks { get { return new List<string>(links); } }

        internal void Refresh(TodoItem item, AppearanceSettings appearance = null)
        {
            if (item == null) throw new ArgumentNullException("item");
            AppearanceSettings current = AppearanceSettings.Normalize(appearance ?? this.appearance);
            if (this.appearance == null || !this.appearance.Matches(current)) ApplyAppearance(current);
            links = FileLinkRules.Normalize(item.FileLinks);
            taskTitle.Text = item.Text; taskTitle.ToolTip = item.Text;
            RenderRows();
        }

        private void ApplyAppearance(AppearanceSettings value)
        {
            appearance = value.Clone();
            var readable = value.Clone(); readable.BackgroundTransparency = Math.Min(6, readable.BackgroundTransparency);
            palette = new PanelPalette(readable);
            surface.Background = palette.Background; surface.BorderBrush = palette.ToolbarBorder;
            Foreground = heading.Foreground = palette.Ink;
            taskTitle.Foreground = empty.Foreground = notice.Foreground = palette.Muted;
            error.Foreground = palette.Warning;
            empty.FontSize = 14 * palette.TextScale;
            fileScroll.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : palette.Muted;
            add.Style = palette.ActionButtonStyle; close.Style = palette.ButtonStyle;
        }

        private void RenderRows()
        {
            heading.Text = "相关文件 " + links.Count;
            empty.Visibility = links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var retained = new HashSet<string>(links, StringComparer.Ordinal);
            foreach (string path in rows.Keys.Where(path => !retained.Contains(path)).ToArray()) {
                rowsPanel.Children.Remove(rows[path].Surface); rows.Remove(path);
            }
            for (int index = 0; index < links.Count; index++) {
                string path = links[index];
                FileRow row;
                if (!rows.TryGetValue(path, out row)) {
                    row = new FileRow(path, OpenFile, LocateFile, RelinkFile, RemoveFile);
                    rows.Add(path, row); rowsPanel.Children.Insert(index + 1, row.Surface);
                }
                else if (!Object.ReferenceEquals(rowsPanel.Children[index + 1], row.Surface)) {
                    rowsPanel.Children.Remove(row.Surface); rowsPanel.Children.Insert(index + 1, row.Surface);
                }
                row.Update(index, palette);
            }
        }

        private void AddFiles()
        {
            try {
                IList<string> picked = service.PickFiles(this);
                if (picked == null || picked.Count == 0) return;
                Save(links.Concat(picked));
            }
            catch (Exception error) { ShowError("未能关联文件。" + error.Message); }
        }

        private void RelinkFile(string path)
        {
            try {
                string picked = service.PickReplacement(this, path);
                if (picked == null) return;
                int index = links.IndexOf(path);
                if (index < 0) return;
                List<string> replacement = FileLinkRules.Normalize(new[] { picked });
                if (replacement.Count != 1) { ShowError("请选择有效的文件位置重新关联。"); return; }
                List<string> candidate = new List<string>(links); candidate[index] = replacement[0];
                Save(candidate);
            }
            catch (Exception error) { ShowError("未能重新关联。" + error.Message); }
        }

        private void RemoveFile(string path)
        {
            var candidate = new List<string>(links);
            if (candidate.Remove(path)) Save(candidate);
        }

        private void Save(IEnumerable<string> candidate)
        {
            List<string> previous = new List<string>(links);
            try {
                List<string> normalized = FileLinkRules.Normalize(candidate);
                if (links.SequenceEqual(normalized, StringComparer.Ordinal)) { ClearError(); return; }
                if (!saveLinks(new List<string>(normalized))) {
                    links = previous; RenderRows();
                    ShowError("关联尚未保存，请重试。"); return;
                }
                links = new List<string>(normalized); RenderRows(); ClearError();
            }
            catch (Exception error) {
                links = previous; RenderRows(); ShowError("关联尚未保存。" + error.Message);
            }
        }

        private void OpenFile(string path)
        {
            try { service.Open(path); ClearError(); }
            catch (Exception error) { ShowError("未能打开文件。" + error.Message); }
        }

        private void LocateFile(string path)
        {
            try { service.Locate(path); ClearError(); }
            catch (Exception error) { ShowError("未能打开所在文件夹。" + error.Message); }
        }

        private void ClearError() { error.Text = ""; error.Visibility = errorScroll.Visibility = Visibility.Collapsed; }
        private void ShowError(string message)
        {
            error.Text = message; error.Visibility = errorScroll.Visibility = Visibility.Visible;
            AutomationProperties.SetName(error, message);
        }

        internal static string FileName(string path)
        {
            if (String.IsNullOrEmpty(path)) return "";
            int separator = LastSeparator(path);
            return separator < 0 ? path : path.Substring(separator + 1);
        }

        private static string DirectoryName(string path)
        {
            int separator = LastSeparator(path);
            return separator < 0 ? "" : path.Substring(0, separator + 1);
        }

        private static int LastSeparator(string path)
        {
            // A backslash can be part of a POSIX file name.
            return path.StartsWith("/", StringComparison.Ordinal) ? path.LastIndexOf('/') :
                Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        }

        private sealed class FileRow
        {
            internal readonly Border Surface;
            private readonly TextBlock name, directory;
            private readonly Button open, more;
            private readonly MenuItem locate, relink, remove;
            private readonly string path;

            internal FileRow(string path, Action<string> openFile, Action<string> locateFile,
                Action<string> relinkFile, Action<string> removeFile)
            {
                this.path = path;
                var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = path };
                name = Theme.Text(FileName(path), 15, Theme.Ink); name.FontWeight = FontWeights.SemiBold;
                name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
                directory = Theme.Text(DirectoryName(path), 11, Theme.Muted); directory.Margin = new Thickness(0, 4, 0, 0);
                directory.TextWrapping = TextWrapping.NoWrap; directory.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(name); text.Children.Add(directory); grid.Children.Add(text);
                open = new Button { Content = "打开", Padding = new Thickness(10, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
                open.Click += delegate { openFile(path); }; Grid.SetColumn(open, 1); grid.Children.Add(open);
                more = new Button { Content = "⋯", FontSize = 19, MinWidth = 32, Padding = new Thickness(5, 1, 5, 4),
                    Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                locate = new MenuItem { Header = "打开所在文件夹" }; locate.Click += delegate { locateFile(path); };
                relink = new MenuItem { Header = "重新关联…" }; relink.Click += delegate { relinkFile(path); };
                remove = new MenuItem { Header = "移除关联" }; remove.Click += delegate { removeFile(path); };
                menu.Items.Add(locate); menu.Items.Add(relink); menu.Items.Add(new Separator()); menu.Items.Add(remove);
                more.ContextMenu = menu; more.Click += delegate { menu.IsOpen = true; };
                Grid.SetColumn(more, 2); grid.Children.Add(more);
                Surface = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
            }

            internal void Update(int index, PanelPalette palette)
            {
                Surface.BorderBrush = palette.Separator;
                name.Foreground = palette.Ink; name.FontSize = 15 * palette.TextScale;
                directory.Foreground = palette.Muted; directory.FontSize = 11 * palette.TextScale;
                open.Style = palette.ActionButtonStyle; more.Style = palette.ButtonStyle;
                string suffix = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                AutomationProperties.SetAutomationId(Surface, "FileLinkRow_" + suffix);
                AutomationProperties.SetAutomationId(name, "FileLinkName_" + suffix);
                AutomationProperties.SetAutomationId(directory, "FileLinkDirectory_" + suffix);
                AutomationProperties.SetAutomationId(open, "FileLinkOpen_" + suffix);
                AutomationProperties.SetAutomationId(more, "FileLinkMore_" + suffix);
                AutomationProperties.SetAutomationId(locate, "FileLinkLocate_" + suffix);
                AutomationProperties.SetAutomationId(relink, "FileLinkRelink_" + suffix);
                AutomationProperties.SetAutomationId(remove, "FileLinkRemove_" + suffix);
                AutomationProperties.SetName(open, "打开文件：" + FileName(path));
                AutomationProperties.SetName(more, "文件操作：" + FileName(path));
            }
        }
    }
}
