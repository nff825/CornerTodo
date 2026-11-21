using System;

namespace CornerTodo
{
    public enum TaskQuadrant
    {
        ImportantUrgent = 0,
        ImportantNotUrgent = 1,
        NotImportantUrgent = 2,
        NotImportantNotUrgent = 3
    }

    public static class QuadrantRules
    {
        public const int DefaultUrgentHours = 24;

        public static bool IsValidUrgentHours(int hours)
        {
            return hours == 6 || hours == 24 || hours == 72 || hours == 168;
        }

        public static int NormalizeUrgentHours(int hours)
        {
            return IsValidUrgentHours(hours) ? hours : DefaultUrgentHours;
        }

        public static TaskQuadrant Classify(TodoItem item, DateTime now, int urgentHours)
        {
            if (item == null) throw new ArgumentNullException("item");
            DateTime reference = item.IsCompleted && item.CompletedAt.HasValue ? item.CompletedAt.Value : now;
            if (reference.Kind == DateTimeKind.Utc) reference = reference.ToLocalTime();
            bool urgent = item.HasDeadline && TodoRules.Deadline(item) - reference <= TimeSpan.FromHours(NormalizeUrgentHours(urgentHours));
            if (item.IsImportant) return urgent ? TaskQuadrant.ImportantUrgent : TaskQuadrant.ImportantNotUrgent;
            return urgent ? TaskQuadrant.NotImportantUrgent : TaskQuadrant.NotImportantNotUrgent;
        }

        public static string Label(TaskQuadrant quadrant)
        {
            switch (quadrant) {
                case TaskQuadrant.ImportantUrgent: return "重要且紧急";
                case TaskQuadrant.ImportantNotUrgent: return "重要不紧急";
                case TaskQuadrant.NotImportantUrgent: return "紧急不重要";
                case TaskQuadrant.NotImportantNotUrgent: return "不重要不紧急";
                default: throw new ArgumentOutOfRangeException("quadrant");
            }
        }
    }
}
