using System;

namespace CornerTodo
{
    /// <summary>Editable input only. Task identity and lifecycle timestamps belong to TodoSession.</summary>
    internal sealed class TaskDraft
    {
        public TaskDraft() { Notes = ""; Priority = TaskPriority.Normal; }
        public string Text { get; set; }
        public string Notes { get; set; }
        public TaskPriority Priority { get; set; }
        public DateTime DueDate { get; set; }
        public bool HasTime { get; set; }
    }
}
