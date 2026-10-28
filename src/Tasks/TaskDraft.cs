using System;

namespace CornerTodo
{
    /// <summary>Editable input only. Task identity and lifecycle timestamps belong to TodoSession.</summary>
    internal sealed class TaskDraft
    {
        public string Text { get; set; }
        public DateTime DueDate { get; set; }
        public bool HasTime { get; set; }
    }
}
