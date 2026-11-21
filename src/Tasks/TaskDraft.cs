using System;
using System.Collections.Generic;

namespace CornerTodo
{
    /// <summary>Editable input only. Task identity and lifecycle timestamps belong to TodoSession.</summary>
    internal sealed class TaskDraft
    {
        public TaskDraft() { Notes = ""; FileLinks = new List<string>(); Priority = TaskPriority.Normal; HasDeadline = true; }
        public string Text { get; set; }
        public string Notes { get; set; }
        public List<string> FileLinks { get; set; }
        public TaskPriority Priority { get; set; }
        public bool IsImportant { get; set; }
        public DateTime DueDate { get; set; }
        public bool HasDeadline { get; set; }
        public bool HasTime { get; set; }
        public bool ReminderEnabled { get; set; }
        public int ReminderMinutesBefore { get; set; }
        public RecurrenceOptions Recurrence { get; set; }
    }
}
