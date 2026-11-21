using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CornerTodo
{
    /// <summary>Edits a private list of original-file references, without reading or changing the files.</summary>
    internal sealed class FileLinksEditor : Expander
    {
        private readonly IFileLinkService service;
        private readonly Func<Window> owner;
        private readonly StackPanel rows;
        private readonly TextBlock status;
        private List<string> links;
        internal event EventHandler LinksChanged;

        internal FileLinksEditor(IEnumerable<string> paths, IFileLinkService service, Func<Window> owner)
        {
            if (service == null) throw new ArgumentNullException("service");
            if (owner == null) throw new ArgumentNullException("owner");
            this.service = service; this.owner = owner; links = FileLinkRules.Copy(paths);
            Header = "相关文件（选填）"; IsExpanded = links.Count > 0;
            Margin = new Thickness(0, 14, 0, 0); Foreground = Theme.Ink; FontSize = 13;
            AutomationProperties.SetName(this, "展开或收起相关文件");
            AutomationProperties.SetAutomationId(this, "TaskEditorFileLinks");
            var content = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var add = new Button { Content = "+ 关联本地文件…", Style = Theme.FlatButton(),
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 5, 8, 5), FontSize = 13 };
            AutomationProperties.SetName(add, "关联本地文件"); AutomationProperties.SetAutomationId(add, "TaskEditorAddFileLinks");
            add.Click += delegate { PickFiles(); }; content.Children.Add(add);
            rows = new StackPanel(); AutomationProperties.SetName(rows, "当前关联文件");
            AutomationProperties.SetAutomationId(rows, "TaskEditorFileLinkRows"); content.Children.Add(rows);
            var referenceHint = Theme.Text("仅保存文件位置；原文件移动后可重新关联。", 11, Theme.Muted);
            referenceHint.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(referenceHint);
            var removeHint = Theme.Text("移除关联不会删除原文件。", 11, Theme.Muted);
            removeHint.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(removeHint);
            status = Theme.Text("", 11, Theme.Warning); status.Margin = new Thickness(0, 6, 0, 0); status.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(status, "关联文件编辑提示"); AutomationProperties.SetAutomationId(status, "TaskEditorFileLinkStatus");
            content.Children.Add(status); Content = content;
            RenderRows();
        }

        internal List<string> FileLinks { get { return FileLinkRules.Copy(links); } }

        private void PickFiles()
        {
            try {
                IList<string> selected = service.PickFiles(owner());
                if (selected == null || selected.Count == 0) return;
                // Validate all selected paths before replacing the current draft.
                List<string> next = FileLinkRules.Normalize(links.Concat(selected));
                links = next; status.Text = ""; status.Visibility = Visibility.Collapsed;
                RenderRows(); OnLinksChanged();
            }
            catch (Exception error) {
                status.Text = "未能关联文件，请重试。当前输入已保留。" + (String.IsNullOrWhiteSpace(error.Message) ? "" : "\n" + error.Message);
                status.Visibility = Visibility.Visible;
            }
        }

        private void RenderRows()
        {
            rows.Children.Clear();
            for (int index = 0; index < links.Count; index++) {
                string path = links[index];
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                AutomationProperties.SetAutomationId(row, "TaskEditorFileLinkRow-" + index.ToString(CultureInfo.InvariantCulture));
                var description = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                var name = Theme.Text(FileName(path), 12, Theme.Ink); name.FontWeight = FontWeights.SemiBold;
                name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = path;
                AutomationProperties.SetAutomationId(name, "TaskEditorFileLinkName-" + index.ToString(CultureInfo.InvariantCulture));
                description.Children.Add(name);
                var directory = Theme.Text(DirectoryName(path), 11, Theme.Muted); directory.Margin = new Thickness(0, 3, 0, 0);
                directory.TextWrapping = TextWrapping.NoWrap; directory.TextTrimming = TextTrimming.CharacterEllipsis; directory.ToolTip = path;
                AutomationProperties.SetAutomationId(directory, "TaskEditorFileLinkDirectory-" + index.ToString(CultureInfo.InvariantCulture));
                description.Children.Add(directory); row.Children.Add(description);
                var remove = new Button { Content = "移除关联", Style = Theme.FlatButton(), FontSize = 11,
                    Padding = new Thickness(4, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center, Tag = path };
                AutomationProperties.SetName(remove, "移除关联：" + FileName(path));
                AutomationProperties.SetAutomationId(remove, "TaskEditorRemoveFileLink-" + index.ToString(CultureInfo.InvariantCulture));
                remove.Click += delegate {
                    links.Remove(path); status.Text = ""; status.Visibility = Visibility.Collapsed;
                    RenderRows(); OnLinksChanged();
                };
                Grid.SetColumn(remove, 1); row.Children.Add(remove);
                rows.Children.Add(new Border { Child = row, Background = Theme.Brush("#FFFFFF"), BorderBrush = Theme.Brush("#D3E0E6"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Margin = new Thickness(0, 3, 0, 3) });
            }
        }

        private void OnLinksChanged()
        {
            EventHandler handler = LinksChanged; if (handler != null) handler(this, EventArgs.Empty);
        }

        private static string FileName(string path)
        {
            if (String.IsNullOrEmpty(path)) return "文件";
            int separator = LastSeparator(path);
            return separator < 0 || separator == path.Length - 1 ? path : path.Substring(separator + 1);
        }

        private static string DirectoryName(string path)
        {
            if (String.IsNullOrEmpty(path)) return "";
            int separator = LastSeparator(path);
            if (separator == 0) return path.Substring(0, 1);
            if (separator == 2 && path.Length > 2 && path[1] == ':') return path.Substring(0, 3);
            return separator > 0 ? path.Substring(0, separator) : path;
        }

        private static int LastSeparator(string path)
        {
            // Backslashes are legal characters inside a POSIX file name.
            return path.StartsWith("/", StringComparison.Ordinal) ? path.LastIndexOf('/') :
                path.LastIndexOfAny(new[] { '\\', '/' });
        }
    }
}
