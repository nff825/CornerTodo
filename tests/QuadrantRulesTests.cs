using System;
using System.Linq;
using CornerTodo;

internal static class QuadrantRulesTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Local);
    private static int checks;

    internal static void Run()
    {
        checks = 0;
        ThresholdsAndImportance();
        DeadlineBoundaries();
        CompletionReference();
        ExtremaAndLabels();
        Console.WriteLine("PASS: " + checks + " quadrant classification assertions.");
    }

    private static void ThresholdsAndImportance()
    {
        Assert(QuadrantRules.DefaultUrgentHours == 24 && new WidgetSettings().QuadrantUrgentHours == 24,
            "The quadrant default is not 24 hours.");
        Assert(!new TodoItem().IsImportant && !new RecurringTaskRule().IsImportant && !new TaskDraft().IsImportant,
            "A new task, draft or recurring rule was marked important by default.");
        foreach (int hours in new[] { 6, 24, 72, 168 }) {
            Assert(QuadrantRules.IsValidUrgentHours(hours) && QuadrantRules.NormalizeUrgentHours(hours) == hours,
                "A supported threshold was normalized away.");
            foreach (bool important in new[] { false, true }) {
                TodoItem item = Item(Now.AddHours(hours), important);
                Assert(QuadrantRules.Classify(item, Now, hours) == Urgent(important), "The exact threshold was not urgent.");
                item.DueDate = item.DueDate.AddTicks(1);
                Assert(QuadrantRules.Classify(item, Now, hours) == NotUrgent(important), "A deadline beyond the threshold was urgent.");
                item.DueDate = Now.AddDays(-30);
                Assert(QuadrantRules.Classify(item, Now, hours) == Urgent(important), "An overdue deadline was not urgent.");
                item.HasDeadline = false;
                Assert(QuadrantRules.Classify(item, Now, hours) == NotUrgent(important), "An undated task became urgent.");
            }
        }
        foreach (int invalid in new[] { Int32.MinValue, -1, 0, 12, 25, 169, Int32.MaxValue })
            Assert(!QuadrantRules.IsValidUrgentHours(invalid) && QuadrantRules.NormalizeUrgentHours(invalid) == 24 &&
                QuadrantRules.Classify(Item(Now.AddHours(20), false), Now, invalid) == TaskQuadrant.NotImportantUrgent,
                "An unsupported threshold did not fall back to 24 hours.");

        TodoItem high = Item(Now.AddDays(2), false); high.Priority = TaskPriority.High;
        TodoItem low = Item(Now.AddDays(2), true); low.Priority = TaskPriority.Low;
        Assert(QuadrantRules.Classify(high, Now, 24) == TaskQuadrant.NotImportantNotUrgent &&
            QuadrantRules.Classify(low, Now, 24) == TaskQuadrant.ImportantNotUrgent,
            "Priority was used to infer importance.");
        TodoItem twelveHours = Item(Now.AddHours(12), true);
        Assert(!TodoRules.IsUrgent(twelveHours, Now) && QuadrantRules.Classify(twelveHours, Now, 24) == TaskQuadrant.ImportantUrgent &&
            QuadrantRules.Classify(twelveHours, Now, 6) == TaskQuadrant.ImportantNotUrgent,
            "Quadrant classification changed or reused the six-hour attention threshold.");
    }

    private static void DeadlineBoundaries()
    {
        TodoItem dateOnly = Item(Now.Date, true); dateOnly.HasTime = false;
        DateTime sixHoursBeforeMidnight = Now.Date.AddHours(18);
        Assert(QuadrantRules.Classify(dateOnly, sixHoursBeforeMidnight.AddTicks(-1), 6) == TaskQuadrant.ImportantNotUrgent &&
            QuadrantRules.Classify(dateOnly, sixHoursBeforeMidnight, 6) == TaskQuadrant.ImportantUrgent &&
            QuadrantRules.Classify(dateOnly, Now.Date.AddDays(1), 6) == TaskQuadrant.ImportantUrgent,
            "A date-only deadline did not use the following midnight.");
        TodoItem utc = Item(Now.AddHours(24).ToUniversalTime(), false);
        Assert(QuadrantRules.Classify(utc, Now.ToUniversalTime(), 24) == TaskQuadrant.NotImportantUrgent &&
            QuadrantRules.Classify(utc, Now.AddTicks(-1).ToUniversalTime(), 24) == TaskQuadrant.NotImportantNotUrgent,
            "UTC and local exact-time boundaries classified differently.");
        utc.DueDate = Now.ToUniversalTime();
        Assert(QuadrantRules.Classify(utc, Now, 6) == TaskQuadrant.NotImportantUrgent, "A deadline at the current instant was not urgent.");
    }

    private static void CompletionReference()
    {
        TodoItem item = Item(Now.AddDays(3), true);
        item.IsCompleted = true; item.CompletedAt = Now.ToUniversalTime();
        Assert(QuadrantRules.Classify(item, Now.AddDays(20), 24) == TaskQuadrant.ImportantNotUrgent,
            "Completed history became urgent as the inspection clock advanced.");
        item.CompletedAt = Now.AddDays(2).ToUniversalTime();
        Assert(QuadrantRules.Classify(item, Now, 24) == TaskQuadrant.ImportantUrgent,
            "A task completed at the urgent boundary did not use its completion timestamp.");
        item.CompletedAt = null;
        Assert(QuadrantRules.Classify(item, Now.AddDays(20), 24) == TaskQuadrant.ImportantUrgent &&
            QuadrantRules.Classify(item, Now, 24) == TaskQuadrant.ImportantNotUrgent,
            "A legacy completed task without a completion timestamp did not use the supplied clock.");
        item.IsCompleted = false; item.CompletedAt = Now.AddDays(20);
        Assert(QuadrantRules.Classify(item, Now, 24) == TaskQuadrant.ImportantNotUrgent,
            "A pending task used a stale completion timestamp.");
    }

    private static void ExtremaAndLabels()
    {
        Assert(QuadrantRules.Classify(Item(DateTime.MaxValue, true), DateTime.MinValue, 168) == TaskQuadrant.ImportantNotUrgent &&
            QuadrantRules.Classify(Item(DateTime.MinValue, false), DateTime.MaxValue, 6) == TaskQuadrant.NotImportantUrgent,
            "DateTime extrema overflowed or classified incorrectly.");
        var quadrants = Enum.GetValues(typeof(TaskQuadrant)).Cast<TaskQuadrant>().ToArray();
        Assert(quadrants.Select(value => (int)value).SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            quadrants.Select(QuadrantRules.Label).Distinct().Count() == 4 && quadrants.All(value => !String.IsNullOrWhiteSpace(QuadrantRules.Label(value))),
            "Quadrant values or labels are incomplete.");
        bool rejected = false;
        try { QuadrantRules.Classify(null, Now, 24); } catch (ArgumentNullException) { rejected = true; }
        Assert(rejected, "Null classification was silently accepted.");
    }

    private static TodoItem Item(DateTime due, bool important)
    {
        return new TodoItem { Id = "quadrant", Text = "四象限测试", CreatedAt = Now.AddDays(-3), DueDate = due,
            HasDeadline = true, HasTime = true, IsImportant = important };
    }

    private static TaskQuadrant Urgent(bool important)
    { return important ? TaskQuadrant.ImportantUrgent : TaskQuadrant.NotImportantUrgent; }
    private static TaskQuadrant NotUrgent(bool important)
    { return important ? TaskQuadrant.ImportantNotUrgent : TaskQuadrant.NotImportantNotUrgent; }
    private static void Assert(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
}
