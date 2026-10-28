using System;
using System.Linq;

namespace CornerTodo
{
    /// <summary>Explicit value copies, independent of JSON and its compatibility defaults.</summary>
    internal static class TodoStateCopy
    {
        public static TodoState Copy(TodoState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.Items == null || state.Settings == null) throw new ArgumentException("待办列表或窗口设置缺失。", "state");
            WidgetSettings settings = state.Settings;
            return new TodoState {
                Version = state.Version,
                Items = state.Items.Select(Copy).ToList(),
                Settings = new WidgetSettings {
                    Left = settings.Left, Top = settings.Top, Width = settings.Width, Height = settings.Height,
                    Collapsed = settings.Collapsed, CompletedCollapsed = settings.CompletedCollapsed,
                    AutoExpand = settings.AutoExpand, Appearance = AppearanceSettings.Normalize(settings.Appearance)
                }
            };
        }

        public static TodoItem Copy(TodoItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            return new TodoItem {
                Id = item.Id, Text = item.Text, IsCompleted = item.IsCompleted,
                CreatedAt = item.CreatedAt, CompletedAt = item.CompletedAt,
                DueDate = item.DueDate, HasTime = item.HasTime, DeadlineSetAt = item.DeadlineSetAt
            };
        }
    }
}
