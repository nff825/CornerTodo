using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CornerTodo
{
    public enum TaskPriority { High = 1, Normal = 2, Low = 3 }
    public enum TaskSortMode { Deadline = 0, Priority = 1 }

    public sealed class TodoItem
    {
        public TodoItem() { Notes = ""; Priority = TaskPriority.Normal; }

        public string Id { get; set; }
        public string Text { get; set; }
        public string Notes { get; set; }
        public TaskPriority Priority { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime DueDate { get; set; }
        public bool HasTime { get; set; }
        public DateTime? DeadlineSetAt { get; set; }
    }

    public sealed class WidgetSettings
    {
        public WidgetSettings()
        {
            Width = 430;
            Height = 475;
            AutoExpand = true;
            Appearance = AppearanceSettings.CreateLight();
            SortMode = TaskSortMode.Deadline;
        }

        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Collapsed { get; set; }
        public bool CompletedCollapsed { get; set; }
        public bool AutoExpand { get; set; }
        public AppearanceSettings Appearance { get; set; }
        public TaskSortMode SortMode { get; set; }
    }

    public sealed class TodoState
    {
        public TodoState()
        {
            Version = 1;
            Items = new List<TodoItem>();
            Settings = new WidgetSettings();
        }

        public int Version { get; set; }
        public List<TodoItem> Items { get; set; }
        public WidgetSettings Settings { get; set; }
    }

    public static class TodoRules
    {
        public const int MaxNotesLength = 5000;

        public static string PriorityLabel(TaskPriority priority)
        {
            return priority == TaskPriority.High ? "高" : priority == TaskPriority.Low ? "低" : "中";
        }

        /// <summary>Elapsed scheduling time, independent of the final-24-hour attention curve.</summary>
        public static double TimeProgress(TodoItem item, DateTime now)
        {
            if (item == null || item.IsCompleted) return 0;
            DateTime deadline = Deadline(item);
            DateTime current = LocalTime(now);
            if (current >= deadline) return 1;
            DateTime started = LocalTime(item.DeadlineSetAt ?? item.CreatedAt);
            if (current <= started || deadline <= started) return 0;
            return Math.Max(0, Math.Min(1, (current - started).TotalSeconds / (deadline - started).TotalSeconds));
        }

        public static DateTime Deadline(TodoItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            DateTime due = LocalTime(item.DueDate);
            if (item.HasTime) return due;
            // A date without a time stays valid throughout that calendar day.
            return due.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : due.Date.AddDays(1);
        }

        public static bool IsOverdue(TodoItem item, DateTime now)
        {
            return item != null && !item.IsCompleted && LocalTime(now) >= Deadline(item);
        }

        public static bool IsUrgent(TodoItem item, DateTime now)
        {
            if (item == null || item.IsCompleted) return false;
            TimeSpan remaining = Deadline(item) - LocalTime(now);
            return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromHours(6);
        }

        public static double Urgency(TodoItem item, DateTime now)
        {
            if (item == null || item.IsCompleted) return 0;
            DateTime deadline = Deadline(item);
            double remaining = (deadline - LocalTime(now)).TotalSeconds;
            if (remaining <= 0) return 1;

            // The original scheduling interval is fixed until the deadline is
            // actually changed. Opening the app or editing text cannot restart it.
            DateTime scheduled = LocalTime(item.DeadlineSetAt ?? item.CreatedAt);
            double window = Math.Min(TimeSpan.FromHours(24).TotalSeconds, (deadline - scheduled).TotalSeconds);
            if (window <= 0 || Double.IsNaN(window) || Double.IsInfinity(window)) return 0;
            double progress = Math.Max(0, Math.Min(1, 1 - remaining / window));
            return progress * progress;
        }

        public static double Expansion(TodoState state, DateTime now)
        {
            // Only the three strongest contributions influence the single panel.
            // This result is independent of settings and never changes manual bounds.
            double[] top = Items(state).Select(item => Urgency(item, now))
                .OrderByDescending(contribution => contribution).Take(3).ToArray();
            double value = top.Length > 0 ? top[0] : 0;
            if (top.Length > 1) value += 0.15 * top[1];
            if (top.Length > 2) value += 0.05 * top[2];
            return Math.Min(1, value);
        }

        public static int OverdueCount(TodoState state, DateTime now)
        {
            return Items(state).Count(item => IsOverdue(item, now));
        }

        public static int UrgentCount(TodoState state, DateTime now)
        {
            return Items(state).Count(item => IsUrgent(item, now));
        }

        public static string FormatDue(TodoItem item, DateTime now)
        {
            if (item == null) throw new ArgumentNullException("item");
            DateTime due = LocalTime(item.DueDate);
            DateTime today = LocalTime(now).Date;
            double days = (due.Date - today).TotalDays;
            string date;
            if (days == 0) date = "今天";
            else if (days == 1) date = "明天";
            else if (days == -1) date = "昨天";
            else date = due.ToString(due.Year == today.Year ? "M月d日" : "yyyy年M月d日", CultureInfo.InvariantCulture);
            return item.HasTime ? date + " " + due.ToString("HH:mm", CultureInfo.InvariantCulture) : date;
        }

        public static string FormatAttention(TodoItem item, DateTime now)
        {
            string due = FormatDue(item, now);
            if (item.IsCompleted) return due;
            DateTime localNow = LocalTime(now);
            DateTime deadline = Deadline(item);
            if (IsOverdue(item, localNow))
                return due + " · 已逾期" + ElapsedLabel(localNow - deadline);
            if (IsUrgent(item, localNow))
                return due + " · 剩余" + RemainingLabel(deadline - localNow);
            return due;
        }

        public static IEnumerable<TodoItem> Pending(TodoState state)
        {
            IEnumerable<TodoItem> pending = Items(state).Where(item => !item.IsCompleted);
            IOrderedEnumerable<TodoItem> ordered = state != null && state.Settings != null && state.Settings.SortMode == TaskSortMode.Priority
                ? pending.OrderBy(item => Enum.IsDefined(typeof(TaskPriority), item.Priority) ? (int)item.Priority : (int)TaskPriority.Normal)
                    .ThenBy(item => Deadline(item))
                : pending.OrderBy(item => Deadline(item));
            return ordered
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id, StringComparer.Ordinal);
        }

        public static IEnumerable<TodoItem> Completed(TodoState state)
        {
            return Items(state).Where(item => item.IsCompleted)
                .OrderByDescending(item => item.CompletedAt ?? item.CreatedAt)
                .ThenByDescending(item => item.CreatedAt)
                .ThenBy(item => item.Id, StringComparer.Ordinal);
        }

        private static IEnumerable<TodoItem> Items(TodoState state)
        {
            return state == null || state.Items == null
                ? Enumerable.Empty<TodoItem>()
                : state.Items.Where(item => item != null);
        }

        private static DateTime LocalTime(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        }

        private static string RemainingLabel(TimeSpan remaining)
        {
            if (remaining.TotalMinutes < 1) return "不足1分钟";
            // Round future durations upward so that a pending task never says zero.
            int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            if (minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + "分钟";
            int hours = minutes / 60;
            int rest = minutes % 60;
            return hours.ToString(CultureInfo.InvariantCulture) + "小时" +
                (rest == 0 ? "" : rest.ToString(CultureInfo.InvariantCulture) + "分钟");
        }

        private static string ElapsedLabel(TimeSpan elapsed)
        {
            if (elapsed.TotalDays >= 1)
                return Math.Floor(elapsed.TotalDays).ToString("0", CultureInfo.InvariantCulture) + "天";
            if (elapsed.TotalHours >= 1)
                return Math.Floor(elapsed.TotalHours).ToString("0", CultureInfo.InvariantCulture) + "小时";
            if (elapsed.TotalMinutes >= 1)
                return Math.Floor(elapsed.TotalMinutes).ToString("0", CultureInfo.InvariantCulture) + "分钟";
            return "不足1分钟";
        }
    }
}
