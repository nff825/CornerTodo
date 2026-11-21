using System;
using System.Collections.Generic;
using System.Linq;
using CornerTodo;

internal static class DeadlineCalendarTests
{
    private static readonly DateTime Day = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Local);

    internal static void Run()
    {
        DateGroupingAndOrder();
        OverdueAndUnscheduled();
        SavedOccurrencesOnly();
        MonthDays();
        Console.WriteLine("PASS: deadline calendar data grouping, saved occurrences and month grid.");
    }

    private static void DateGroupingAndOrder()
    {
        var state = new TodoState();
        state.Settings.SortMode = TaskSortMode.Priority;
        TodoItem untimed = Item("date-only", Day, false);
        untimed.HasTime = false;
        TodoItem utc = Item("early-utc", Day.AddMinutes(15).ToUniversalTime(), false);
        TodoItem late = Item("late", Day.AddHours(19), false);
        late.Priority = TaskPriority.High;
        TodoItem completed = Item("completed", Day.AddHours(6), true);
        completed.CompletedAt = Day.AddDays(1).AddHours(9);
        TodoItem tomorrow = Item("next-day", Day.AddDays(1), false);
        state.Items.AddRange(new[] { untimed, late, completed, tomorrow, utc });

        var all = new DeadlineCalendarData(state, true, Day.AddHours(12));
        ExpectIds(all.ItemsOn(Day.AddHours(16)), "early-utc", "late", "date-only", "completed");
        ExpectIds(all.ItemsOn(Day.AddDays(1)), "next-day");
        ExpectIds(all.ItemsOn(Day.AddDays(-1)));
        var pending = new DeadlineCalendarData(state, false, Day.AddHours(12));
        ExpectIds(pending.ItemsOn(Day), "early-utc", "late", "date-only");
        ExpectIds(state.Items, "date-only", "late", "completed", "next-day", "early-utc");
    }

    private static void OverdueAndUnscheduled()
    {
        var state = new TodoState();
        TodoItem untimed = Item("date-only", Day, false);
        untimed.HasTime = false;
        TodoItem timed = Item("timed", Day.AddHours(12), false);
        TodoItem done = Item("done", Day.AddDays(-1), true);
        TodoItem noDate = Item("no-date", DateTime.MinValue, false);
        noDate.HasDeadline = false;
        noDate.HasTime = false;
        TodoItem doneNoDate = Item("done-no-date", DateTime.MinValue, true);
        doneNoDate.HasDeadline = false;
        doneNoDate.HasTime = false;
        state.Items.AddRange(new[] { untimed, timed, done, noDate, doneNoDate });

        var exact = new DeadlineCalendarData(state, true, Day.AddHours(12));
        ExpectIds(exact.Overdue, "timed");
        ExpectIds(exact.ItemsOn(Day), "timed", "date-only");
        ExpectIds(exact.Unscheduled, "no-date", "done-no-date");
        ExpectIds(exact.ItemsOn(DateTime.MinValue));
        var midnight = new DeadlineCalendarData(state, false, Day.AddDays(1));
        ExpectIds(midnight.Overdue, "timed", "date-only");
        ExpectIds(midnight.ItemsOn(Day), "timed", "date-only");
        ExpectIds(midnight.ItemsOn(Day.AddDays(1)));
        ExpectIds(midnight.Unscheduled, "no-date");
    }

    private static void SavedOccurrencesOnly()
    {
        var state = new TodoState();
        var rule = new RecurringTaskRule {
            Id = "daily", Text = "重复任务", HasDeadline = true, HasTime = true,
            DeadlineTimeTicks = TimeSpan.FromHours(9).Ticks, ReminderEnabled = true,
            ReminderMinutesBefore = 1440, ReminderSetAt = Day.AddDays(-1), ProcessedThrough = Day.Date,
            Recurrence = new RecurrenceOptions { Frequency = RecurrenceFrequency.Daily, StartDate = Day.Date }
        };
        rule.PreparedOccurrences.Add(Day.AddDays(1).Date);
        state.RecurringRules.Add(rule);
        TodoItem occurrence = Item("prepared", Day.AddDays(1).AddHours(9), false);
        occurrence.RecurringRuleId = rule.Id;
        occurrence.OccurrenceDate = Day.AddDays(1).Date;
        occurrence.RepeatLabel = "每日";
        occurrence.ReminderEnabled = true;
        occurrence.ReminderMinutesBefore = 1440;
        occurrence.ReminderPlanId = "original-reminder-plan";
        occurrence.ReminderSetAt = Day.AddDays(-1);
        state.Items.Add(occurrence);

        var calendar = new DeadlineCalendarData(state, false, Day.AddHours(12));
        ExpectIds(calendar.ItemsOn(Day.AddDays(1)), "prepared");
        ExpectIds(calendar.ItemsOn(Day.AddDays(2)));
        ExpectIds(calendar.Overdue);
        TodoItem projected = calendar.ItemsOn(Day.AddDays(1)).Single();
        Assert(projected.RecurringRuleId == rule.Id && projected.OccurrenceDate == occurrence.OccurrenceDate &&
            projected.ReminderPlanId == occurrence.ReminderPlanId && !projected.ReminderHandledAt.HasValue,
            "Projection changed recurrence or reminder metadata.");
        Assert(rule.ProcessedThrough == Day.Date && rule.PreparedOccurrences.Count == 1 && state.Items.Count == 1 &&
            !occurrence.ReminderHandledAt.HasValue, "Projection processed an occurrence or reminder.");
        Assert(!Object.ReferenceEquals(projected, occurrence), "Projection exposed the original mutable task.");
    }

    private static void MonthDays()
    {
        ExpectMonth(new DateTime(2026, 2, 20), new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), 28);
        ExpectMonth(new DateTime(2024, 2, 1), new DateTime(2024, 1, 28), new DateTime(2024, 3, 2), 35);
        ExpectMonth(new DateTime(2026, 5, 1), new DateTime(2026, 4, 26), new DateTime(2026, 6, 6), 42);
        ExpectMonth(new DateTime(2026, 12, 1), new DateTime(2026, 11, 29), new DateTime(2027, 1, 2), 35);

        IList<DateTime?> minimum = DeadlineCalendarData.GetMonthDays(DateTime.MinValue);
        Assert(minimum.Count == 35 && minimum[0] == null && minimum[1] == DateTime.MinValue &&
            minimum.Last() == new DateTime(1, 2, 3), "The calendar did not pad the first supported month correctly.");
        IList<DateTime?> maximum = DeadlineCalendarData.GetMonthDays(DateTime.MaxValue);
        Assert(maximum.Count == 35 && maximum[0] == new DateTime(9999, 11, 28) &&
            maximum[33] == DateTime.MaxValue.Date && maximum[34] == null, "The calendar did not pad the final supported month correctly.");
    }

    private static void ExpectMonth(DateTime month, DateTime first, DateTime last, int count)
    {
        IList<DateTime?> days = DeadlineCalendarData.GetMonthDays(month);
        Assert(days.Count == count && days[0] == first && days.Last() == last &&
            days.SequenceEqual(Enumerable.Range(0, count).Select(index => (DateTime?)first.AddDays(index))),
            "Wrong day grid for " + month.ToString("yyyy-MM") + ".");
    }

    private static TodoItem Item(string id, DateTime due, bool completed)
    {
        return new TodoItem { Id = id, Text = id, DueDate = due, HasTime = true, IsCompleted = completed,
            CreatedAt = Day.AddDays(-5), CompletedAt = completed ? (DateTime?)Day : null };
    }

    private static void ExpectIds(IEnumerable<TodoItem> items, params string[] expected)
    {
        string[] actual = items.Select(item => item.Id).ToArray();
        Assert(actual.SequenceEqual(expected), "Expected [" + String.Join(", ", expected) +
            "] but got [" + String.Join(", ", actual) + "].");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
