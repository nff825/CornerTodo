using System;
using System.Collections.Generic;
using System.Linq;

namespace CornerTodo
{
    internal sealed class DeadlineCalendarData
    {
        private static readonly IList<TodoItem> EmptyItems = Array.AsReadOnly(new TodoItem[0]);
        private readonly Dictionary<DateTime, IList<TodoItem>> days;

        internal DeadlineCalendarData(TodoState state, bool includeCompleted, DateTime now)
        {
            IEnumerable<TodoItem> items = state == null || state.Items == null
                ? Enumerable.Empty<TodoItem>() : state.Items;
            var ordered = items.Where(item => item != null && (includeCompleted || !item.IsCompleted))
                .OrderBy(item => item.IsCompleted)
                .ThenBy(TodoRules.Deadline)
                .ThenByDescending(item => item.HasTime)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .Select(TodoStateCopy.Copy)
                .ToList();

            // An untimed deadline expires at midnight after the selected date.
            // The calendar cell represents the date the user selected.
            days = ordered.Where(item => item.HasDeadline)
                .GroupBy(item => RecurringTasks.LocalDate(item.DueDate))
                .ToDictionary(day => day.Key, day => (IList<TodoItem>)day.ToList().AsReadOnly());
            Unscheduled = ordered.Where(item => !item.HasDeadline).ToList().AsReadOnly();
            Overdue = ordered.Where(item => TodoRules.IsOverdue(item, now)).ToList().AsReadOnly();
        }

        internal IList<TodoItem> Unscheduled { get; private set; }
        internal IList<TodoItem> Overdue { get; private set; }

        internal IList<TodoItem> ItemsOn(DateTime day)
        {
            IList<TodoItem> items;
            return days.TryGetValue(RecurringTasks.LocalDate(day), out items) ? items : EmptyItems;
        }

        internal static IList<DateTime?> GetMonthDays(DateTime month)
        {
            var first = new DateTime(month.Year, month.Month, 1);
            int leading = (int)first.DayOfWeek;
            int cells = ((leading + DateTime.DaysInMonth(first.Year, first.Month) + 6) / 7) * 7;
            var days = new List<DateTime?>(cells);
            for (int index = 0; index < cells; index++) {
                long ticks = first.Ticks + (index - leading) * TimeSpan.TicksPerDay;
                days.Add(ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                    ? (DateTime?)null : new DateTime(ticks));
            }
            return days.AsReadOnly();
        }
    }
}
