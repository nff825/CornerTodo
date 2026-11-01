using System;
using System.Globalization;

namespace CornerTodo
{
    public enum RecurrenceFrequency { Daily = 1, Weekly = 2, Monthly = 3 }

    public sealed class RecurrenceOptions
    {
        public RecurrenceOptions() { Frequency = RecurrenceFrequency.Daily; WeekDays = 2; DayOfMonth = 1; StartDate = DateTime.Today; }
        public RecurrenceFrequency Frequency { get; set; }
        // Sunday is bit 0, Monday bit 1, ... Saturday bit 6.
        public int WeekDays { get; set; }
        public int DayOfMonth { get; set; }
        public bool UseLastDay { get; set; }
        public DateTime StartDate { get; set; }
    }

    public sealed class RecurringTaskRule
    {
        public RecurringTaskRule() { Notes = ""; Priority = TaskPriority.Normal; Recurrence = new RecurrenceOptions(); }
        public string Id { get; set; }
        public string Text { get; set; }
        public string Notes { get; set; }
        public TaskPriority Priority { get; set; }
        public bool HasDeadline { get; set; }
        public bool HasTime { get; set; }
        public int DeadlineOffsetDays { get; set; }
        public long DeadlineTimeTicks { get; set; }
        public RecurrenceOptions Recurrence { get; set; }
        public bool IsPaused { get; set; }
        public bool IsStopped { get; set; }
        public DateTime? ProcessedThrough { get; set; }
    }

    public sealed class RecurrenceSummary
    {
        public long MissedCount { get; set; }
        public int DueTodayCount { get; set; }
        public long TotalCount { get { return MissedCount + DueTodayCount; } }
    }

    /// <summary>Pure local-calendar recurrence calculations. No clock, storage or mutable cursors.</summary>
    public static class RecurringTasks
    {
        public const int MaximumBatchSize = 366;
        public const int MaximumRuleCount = 1000;

        public static RecurrenceOptions Copy(RecurrenceOptions source)
        {
            if (source == null) return null;
            return new RecurrenceOptions { Frequency = source.Frequency, WeekDays = source.WeekDays,
                DayOfMonth = source.DayOfMonth, UseLastDay = source.UseLastDay, StartDate = source.StartDate };
        }

        public static RecurringTaskRule Copy(RecurringTaskRule source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return new RecurringTaskRule { Id = source.Id, Text = source.Text, Notes = source.Notes,
                Priority = source.Priority, HasDeadline = source.HasDeadline, HasTime = source.HasTime,
                DeadlineOffsetDays = source.DeadlineOffsetDays, DeadlineTimeTicks = source.DeadlineTimeTicks,
                Recurrence = Copy(source.Recurrence), IsPaused = source.IsPaused, IsStopped = source.IsStopped,
                ProcessedThrough = source.ProcessedThrough };
        }

        public static void Validate(RecurrenceOptions options)
        {
            if (options == null || !Enum.IsDefined(typeof(RecurrenceFrequency), options.Frequency))
                throw new ArgumentException("请选择有效的重复方式。");
            if (options.StartDate == DateTime.MinValue || options.StartDate.TimeOfDay != TimeSpan.Zero || options.StartDate.Kind == DateTimeKind.Utc)
                throw new ArgumentException("请选择有效的本地开始日期。");
            if (options.WeekDays < 0 || options.WeekDays > 127 || (options.Frequency == RecurrenceFrequency.Weekly && options.WeekDays == 0))
                throw new ArgumentException("每周重复至少选择一个星期。");
            if (options.DayOfMonth < 1 || options.DayOfMonth > 31) throw new ArgumentException("每月日期须为1至31日。");
        }

        public static void Validate(RecurringTaskRule rule)
        {
            if (rule == null || String.IsNullOrWhiteSpace(rule.Id) || String.IsNullOrWhiteSpace(rule.Text))
                throw new ArgumentException("重复规则缺少标识或事项。");
            Validate(rule.Recurrence);
            if (!Enum.IsDefined(typeof(TaskPriority), rule.Priority)) throw new ArgumentException("重复规则优先级无效。");
            if (rule.DeadlineOffsetDays < 0 || rule.DeadlineOffsetDays > 3652058 || rule.DeadlineTimeTicks < 0 || rule.DeadlineTimeTicks >= TimeSpan.TicksPerDay)
                throw new ArgumentException("重复事项截止间隔无效。");
            if (!rule.HasDeadline && (rule.HasTime || rule.DeadlineOffsetDays != 0 || rule.DeadlineTimeTicks != 0))
                throw new ArgumentException("无截止日期规则不能保存截止间隔或具体时刻。");
            if (!rule.HasTime && rule.DeadlineTimeTicks != 0) throw new ArgumentException("未设置具体时刻的规则不能保存时间。");
            if (rule.HasDeadline && rule.Recurrence.StartDate.Date > LatestDate(rule)) throw new ArgumentException("开始日期和截止间隔超出日期范围。");
            if (rule.ProcessedThrough.HasValue && (rule.ProcessedThrough.Value.TimeOfDay != TimeSpan.Zero || rule.ProcessedThrough.Value.Kind == DateTimeKind.Utc))
                throw new ArgumentException("重复规则处理进度必须为本地日期。");
        }

        public static string Label(RecurrenceOptions options)
        {
            if (options == null) return "";
            if (options.Frequency == RecurrenceFrequency.Daily) return "每日";
            if (options.Frequency == RecurrenceFrequency.Monthly)
                return options.UseLastDay ? "每月最后一天" : "每月" + options.DayOfMonth.ToString(CultureInfo.InvariantCulture) + "日";
            string[] names = { "日", "一", "二", "三", "四", "五", "六" };
            string label = "";
            for (int index = 1; index <= 7; index++) {
                int day = index % 7;
                if ((options.WeekDays & (1 << day)) != 0) label += (label.Length == 0 ? "每周" : "、周") + names[day];
            }
            return label;
        }

        public static DateTime LocalDate(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) value = value.ToLocalTime();
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
        }

        public static DateTime? NextOccurrence(RecurringTaskRule rule, DateTime now)
        {
            if (rule == null || rule.IsPaused || rule.IsStopped) return null;
            DateTime from = LocalDate(now);
            if (rule.ProcessedThrough.HasValue) {
                if (rule.ProcessedThrough.Value.Date == DateTime.MaxValue.Date) return null;
                DateTime afterCursor = rule.ProcessedThrough.Value.Date.AddDays(1);
                if (afterCursor > from) from = afterCursor;
            }
            return NextOnOrAfter(rule, from);
        }

        internal static DateTime? PendingStart(RecurringTaskRule rule)
        {
            if (rule.ProcessedThrough.HasValue && rule.ProcessedThrough.Value.Date == DateTime.MaxValue.Date) return null;
            return rule.ProcessedThrough.HasValue ? (DateTime?)rule.ProcessedThrough.Value.Date.AddDays(1) : rule.Recurrence.StartDate.Date;
        }

        internal static DateTime LatestDate(RecurringTaskRule rule)
        {
            return rule.HasDeadline ? DateTime.MaxValue.Date.AddDays(-rule.DeadlineOffsetDays) : DateTime.MaxValue.Date;
        }

        internal static DateTime? NextOnOrAfter(RecurringTaskRule rule, DateTime from)
        {
            RecurrenceOptions options = rule.Recurrence;
            from = LocalDate(from);
            if (from < options.StartDate.Date) from = options.StartDate.Date;
            DateTime last = LatestDate(rule);
            if (from > last) return null;
            if (options.Frequency == RecurrenceFrequency.Daily) return from;
            if (options.Frequency == RecurrenceFrequency.Weekly) {
                for (int count = 0; count < 7 && from <= last; count++) {
                    if ((options.WeekDays & (1 << (int)from.DayOfWeek)) != 0) return from;
                    if (from.Date == DateTime.MaxValue.Date) return null;
                    from = from.AddDays(1);
                }
                return null;
            }
            DateTime candidate = MonthDate(options, from.Year, from.Month);
            if (candidate < from) {
                if (from.Year == 9999 && from.Month == 12) return null;
                DateTime next = new DateTime(from.Year, from.Month, 1).AddMonths(1);
                candidate = MonthDate(options, next.Year, next.Month);
            }
            return candidate <= last ? (DateTime?)candidate : null;
        }

        internal static long Count(RecurringTaskRule rule, DateTime from, DateTime to)
        {
            from = LocalDate(from); to = LocalDate(to);
            if (from < rule.Recurrence.StartDate.Date) from = rule.Recurrence.StartDate.Date;
            if (to > LatestDate(rule)) to = LatestDate(rule);
            if (from > to) return 0;
            RecurrenceOptions options = rule.Recurrence;
            long days = (long)(to - from).TotalDays + 1;
            if (options.Frequency == RecurrenceFrequency.Daily) return days;
            if (options.Frequency == RecurrenceFrequency.Weekly) {
                int selected = 0;
                for (int bit = 0; bit < 7; bit++) if ((options.WeekDays & (1 << bit)) != 0) selected++;
                long result = days / 7 * selected;
                for (int offset = 0; offset < days % 7; offset++)
                    if ((options.WeekDays & (1 << (int)from.AddDays(offset).DayOfWeek)) != 0) result++;
                return result;
            }
            long months = (to.Year - from.Year) * 12L + to.Month - from.Month + 1;
            if (MonthDate(options, from.Year, from.Month) < from) months--;
            if (MonthDate(options, to.Year, to.Month) > to) months--;
            return Math.Max(0, months);
        }

        private static DateTime MonthDate(RecurrenceOptions options, int year, int month)
        {
            int last = DateTime.DaysInMonth(year, month);
            return new DateTime(year, month, options.UseLastDay ? last : Math.Min(last, options.DayOfMonth));
        }
    }
}
