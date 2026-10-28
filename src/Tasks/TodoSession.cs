using System;
using System.Collections.Generic;

namespace CornerTodo
{
    /// <summary>
    /// Owns committed state on the UI thread. Commands save an independent candidate
    /// before publishing it; views and storage never receive the owned object graph.
    /// </summary>
    internal sealed class TodoSession
    {
        private readonly ITodoStore store;
        private readonly bool preview;
        private readonly Func<DateTime> now;
        private TodoState current;

        public TodoSession(ITodoStore store, TodoState initialState, bool preview)
            : this(store, initialState, preview, delegate { return DateTime.Now; })
        {
        }

        internal TodoSession(ITodoStore store, TodoState initialState, bool preview, Func<DateTime> now)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (now == null) throw new ArgumentNullException("now");
            this.store = store; this.preview = preview; this.now = now;
            current = TodoStateCopy.Copy(initialState);
        }

        public TodoState Snapshot()
        {
            return TodoStateCopy.Copy(current);
        }

        public TodoItem Add(TaskDraft draft)
        {
            TaskDraft input = ReadDraft(draft);
            DateTime savedAt = now();
            var item = new TodoItem {
                Id = Guid.NewGuid().ToString("N"), Text = input.Text, Notes = input.Notes, Priority = input.Priority,
                DueDate = input.DueDate, HasTime = input.HasTime,
                CreatedAt = savedAt, DeadlineSetAt = savedAt
            };
            var next = Snapshot();
            next.Items.Add(item);
            Commit(next);
            return TodoStateCopy.Copy(item);
        }

        public void Edit(string id, TaskDraft draft)
        {
            TaskDraft input = ReadDraft(draft);
            int index = Find(id);
            TodoState next = Snapshot();
            TodoItem item = next.Items[index];
            DateTime previousDeadline = TodoRules.Deadline(item);
            item.Text = input.Text; item.Notes = input.Notes; item.Priority = input.Priority;
            item.DueDate = input.DueDate; item.HasTime = input.HasTime;
            if (TodoRules.Deadline(item) != previousDeadline) item.DeadlineSetAt = now();
            Commit(next);
        }

        public void SetCompleted(string id, bool completed)
        {
            int index = Find(id);
            if (current.Items[index].IsCompleted == completed) return;
            TodoState next = Snapshot();
            TodoItem item = next.Items[index];
            item.IsCompleted = completed;
            item.CompletedAt = completed ? (DateTime?)now() : null;
            Commit(next);
        }

        public void Delete(string id)
        {
            int index = Find(id);
            TodoState next = Snapshot();
            next.Items.RemoveAt(index);
            Commit(next);
        }

        public void SetAppearance(AppearanceSettings appearance)
        {
            var value = AppearanceSettings.Normalize(appearance);
            Change(delegate(TodoState next) { next.Settings.Appearance = value; });
        }

        public void SetCompletedCollapsed(bool collapsed)
        {
            Change(delegate(TodoState next) { next.Settings.CompletedCollapsed = collapsed; });
        }

        public void SetAutoExpand(bool enabled)
        {
            Change(delegate(TodoState next) { next.Settings.AutoExpand = enabled; });
        }

        public void SetSortMode(TaskSortMode mode)
        {
            if (!Enum.IsDefined(typeof(TaskSortMode), mode)) throw new ArgumentException("请选择有效的排序方式。", "mode");
            if (current.Settings.SortMode == mode) return;
            Change(delegate(TodoState next) { next.Settings.SortMode = mode; });
        }

        public void SetCollapsed(bool collapsed)
        {
            Change(delegate(TodoState next) { next.Settings.Collapsed = collapsed; });
        }

        public void SaveGeometry(double left, double top, double width, double height)
        {
            if (!Finite(left) || !Finite(top) || !Finite(width) || !Finite(height) || width <= 0 || height <= 0)
                throw new ArgumentException("窗口位置和尺寸必须为有效数值，宽高必须大于零。");
            Change(delegate(TodoState next) {
                next.Settings.Left = left; next.Settings.Top = top;
                next.Settings.Width = width; next.Settings.Height = height;
            });
        }

        private void Change(Action<TodoState> change)
        {
            TodoState next = Snapshot();
            change(next);
            Commit(next);
        }

        private void Commit(TodoState next)
        {
            // A writer may retain or even mutate its input. Neither that reference
            // nor an exception can modify the state already owned by this session.
            if (!preview) store.Save(TodoStateCopy.Copy(next));
            current = next;
        }

        private int Find(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("必须指定待办标识。", "id");
            int index = current.Items.FindIndex(item => String.Equals(item.Id, id, StringComparison.Ordinal));
            if (index < 0) throw new KeyNotFoundException("这条待办已不存在，请刷新后重试。");
            return index;
        }

        private static TaskDraft ReadDraft(TaskDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            if (String.IsNullOrWhiteSpace(draft.Text)) throw new ArgumentException("请填写待办事项。", "draft");
            if (draft.DueDate == DateTime.MinValue) throw new ArgumentException("请选择有效的截止日期。", "draft");
            if (!Enum.IsDefined(typeof(TaskPriority), draft.Priority)) throw new ArgumentException("请选择有效的优先级。", "draft");
            string notes = draft.Notes ?? "";
            if (notes.Length > TodoRules.MaxNotesLength) throw new ArgumentException("备注最多可填写 " + TodoRules.MaxNotesLength + " 个字符。", "draft");
            return new TaskDraft { Text = draft.Text.Trim(), Notes = notes, Priority = draft.Priority, DueDate = draft.DueDate, HasTime = draft.HasTime };
        }

        private static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
