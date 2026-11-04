using System;
using System.Globalization;

namespace CornerTodo
{
    /// <summary>Pure reminder arithmetic and validation; no timers or presentation.</summary>
    public static class ReminderRules
    {
        public const int MaxDays = 3650;
        public const int MaxMinutes = MaxDays * 1440 + 1439;
        public static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(24);

        public static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }

        public static DateTime? GetReminderTime(TodoItem item)
        {
            if (item == null || !item.ReminderEnabled || !item.HasDeadline || !item.HasTime || item.DueDate == DateTime.MinValue ||
                item.ReminderMinutesBefore < 0 || item.ReminderMinutesBefore > MaxMinutes) return null;
            DateTime deadline = ToUtc(item.DueDate);
            if (item.DueDate.Kind != DateTimeKind.Utc && deadline.ToLocalTime() != item.DueDate) return null;
            long before = (long)item.ReminderMinutesBefore * TimeSpan.TicksPerMinute;
            if (deadline.Ticks < before) return null;
            DateTime utc = new DateTime(deadline.Ticks - before, DateTimeKind.Utc);
            DateTime local = utc.ToLocalTime();
            // At DateTime extrema conversion may clamp instead of throwing.
            return ToUtc(local) == utc ? (DateTime?)local : null;
        }

        public static string FormatLeadTime(int minutes)
        {
            if (minutes < 0 || minutes > MaxMinutes) throw new ArgumentOutOfRangeException("minutes");
            if (minutes == 0) return "到期时";
            int days = minutes / 1440, hours = minutes % 1440 / 60, rest = minutes % 60;
            return "提前" + (days > 0 ? days.ToString(CultureInfo.InvariantCulture) + "天" : "")
                + (hours > 0 ? hours.ToString(CultureInfo.InvariantCulture) + "小时" : "")
                + (rest > 0 ? rest.ToString(CultureInfo.InvariantCulture) + "分钟" : "");
        }

        internal static void ValidateInput(bool enabled, int minutes, bool hasDeadline, bool hasTime, DateTime due)
        {
            if (minutes < 0 || minutes > MaxMinutes) throw new ArgumentException("提醒提前时长须在0至3650天23小时59分钟之间。");
            if (!enabled) return;
            if (!hasDeadline || !hasTime) throw new ArgumentException("请先设置截止日期和具体时刻，再开启提醒。");
            var probe = new TodoItem { ReminderEnabled = true, ReminderMinutesBefore = minutes, HasDeadline = true, HasTime = true, DueDate = due };
            if (!GetReminderTime(probe).HasValue) throw new ArgumentException("提醒时间超出可用日期范围，请缩短提前时长。");
        }

        internal static void ValidateItem(TodoItem item)
        {
            ValidateInput(item.ReminderEnabled, item.ReminderMinutesBefore, item.HasDeadline, item.HasTime, item.DueDate);
            if (!item.ReminderEnabled) {
                if (item.ReminderMinutesBefore != 0 || !String.IsNullOrEmpty(item.ReminderPlanId) || item.ReminderSetAt.HasValue || item.ReminderHandledAt.HasValue)
                    throw new ArgumentException("关闭提醒的事项不能保留孤立提醒状态。");
                return;
            }
            if (String.IsNullOrWhiteSpace(item.ReminderPlanId) || !item.ReminderSetAt.HasValue || item.ReminderSetAt.Value == DateTime.MinValue ||
                (item.ReminderHandledAt.HasValue && item.ReminderHandledAt.Value == DateTime.MinValue))
                throw new ArgumentException("事项缺少有效的提醒计划或处理状态。");
        }

        internal static void ValidateRule(RecurringTaskRule rule)
        {
            if (rule.ReminderMinutesBefore < 0 || rule.ReminderMinutesBefore > MaxMinutes) throw new ArgumentException("重复规则的提醒提前时长无效。");
            if (!rule.ReminderEnabled) {
                if (rule.ReminderMinutesBefore != 0 || rule.ReminderSetAt.HasValue) throw new ArgumentException("关闭提醒的规则不能保留提醒设置。");
                return;
            }
            if (!rule.HasDeadline || !rule.HasTime || !rule.ReminderSetAt.HasValue || rule.ReminderSetAt.Value == DateTime.MinValue)
                throw new ArgumentException("重复提醒需要具体截止时刻和启用时间。");
        }
    }
}
