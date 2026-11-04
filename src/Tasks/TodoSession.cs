using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

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
            if (current.Version < 1 || current.Version > TodoState.CurrentVersion)
                throw new NotSupportedException("无法读取此版本的待办数据：" + current.Version + "。请使用支持该数据版本的程序。");
            current.Version = TodoState.CurrentVersion;
        }

        public TodoState Snapshot()
        {
            return TodoStateCopy.Copy(current);
        }

        public TodoItem Add(TaskDraft draft)
        {
            TaskDraft input = ReadDraft(draft);
            DateTime savedAt = now();
            if (input.Recurrence != null) {
                var recurringState = Snapshot();
                if (recurringState.RecurringRules.Count >= RecurringTasks.MaximumRuleCount) throw new InvalidOperationException("重复规则数量已达上限。");
                RecurringTaskRule rule = RuleFromDraft(Guid.NewGuid().ToString("N"), input, savedAt);
                recurringState.RecurringRules.Add(rule);
                TodoItem generated = GenerateToday(recurringState, rule, savedAt);
                Commit(recurringState);
                return generated == null ? null : TodoStateCopy.Copy(generated);
            }
            var item = new TodoItem {
                Id = Guid.NewGuid().ToString("N"), Text = input.Text, Notes = input.Notes, Priority = input.Priority,
                DueDate = input.DueDate, HasDeadline = input.HasDeadline, HasTime = input.HasTime,
                CreatedAt = savedAt, DeadlineSetAt = input.HasDeadline ? (DateTime?)savedAt : null
            };
            ApplyReminder(item, input.ReminderEnabled, input.ReminderMinutesBefore, savedAt, null);
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
            DateTime savedAt = now();
            if (input.Recurrence != null && !String.IsNullOrEmpty(item.RecurringRuleId))
                throw new ArgumentException("修改本次事项不会改变后续重复，请使用修改后续重复规则。");
            bool previouslyScheduled = item.HasDeadline;
            DateTime previousDeadline = TodoRules.Deadline(item);
            item.Text = input.Text; item.Notes = input.Notes; item.Priority = input.Priority;
            item.DueDate = input.DueDate; item.HasDeadline = input.HasDeadline; item.HasTime = input.HasTime;
            if (!item.HasDeadline) item.DeadlineSetAt = null;
            else if (!previouslyScheduled || TodoRules.Deadline(item) != previousDeadline) item.DeadlineSetAt = now();
            ApplyReminder(item, input.ReminderEnabled, input.ReminderMinutesBefore, savedAt, current.Items[index]);
            if (input.Recurrence != null) {
                if (next.RecurringRules.Count >= RecurringTasks.MaximumRuleCount) throw new InvalidOperationException("重复规则数量已达上限。");
                RecurringTaskRule rule = RuleFromDraft(Guid.NewGuid().ToString("N"), input, savedAt);
                DateTime? first = RecurringTasks.NextOnOrAfter(rule, rule.Recurrence.StartDate);
                if (!first.HasValue) throw new ArgumentException("重复计划超出可用日期范围。");
                item.RecurringRuleId = rule.Id; item.OccurrenceDate = first.Value;
                item.RepeatLabel = RecurringTasks.Label(rule.Recurrence);
                // The existing task consumes only this occurrence, not uncreated history.
                rule.ProcessedThrough = first.Value;
                next.RecurringRules.Add(rule);
            }
            Commit(next);
        }

        public RecurrenceSummary GetRecurrenceSummary()
        {
            DateTime today = RecurringTasks.LocalDate(now());
            var summary = new RecurrenceSummary();
            foreach (RecurringTaskRule rule in current.RecurringRules) AddRecurrenceSummary(summary, rule, today);
            return summary;
        }

        public RecurrenceSummary GetRecurrenceSummary(string ruleId)
        {
            RecurringTaskRule rule = FindRule(current, ruleId);
            var summary = new RecurrenceSummary();
            AddRecurrenceSummary(summary, rule, RecurringTasks.LocalDate(now()));
            return summary;
        }

        private static void AddRecurrenceSummary(RecurrenceSummary summary, RecurringTaskRule rule, DateTime today)
        {
            if (rule.IsPaused || rule.IsStopped) return;
            DateTime? from = RecurringTasks.PendingStart(rule);
            if (!from.HasValue || from.Value > today) return;
            if (today > DateTime.MinValue.Date) summary.MissedCount += UnpreparedCount(rule, from.Value, today.AddDays(-1));
            if (UnpreparedCount(rule, today, today) > 0) summary.DueTodayCount++;
        }

        /// <summary>Only generates a current occurrence when that rule has no unresolved past periods.</summary>
        public int GenerateCurrentRecurrences()
        {
            DateTime stamp = now();
            TodoState next = Snapshot();
            int count = 0;
            foreach (RecurringTaskRule rule in next.RecurringRules) {
                if (count >= RecurringTasks.MaximumBatchSize) break;
                TodoItem item = GenerateToday(next, rule, stamp);
                if (item != null) count++;
            }
            if (count > 0 || !CursorsEqual(current, next)) Commit(next);
            return count;
        }

        /// <summary>User acknowledgement: include missed periods, or skip them and generate only today.</summary>
        public int ResolveMissedRecurrences(bool includeMissed)
        {
            DateTime stamp = now(), today = RecurringTasks.LocalDate(stamp);
            TodoState next = Snapshot();
            int generated = 0, processed = 0;
            foreach (RecurringTaskRule rule in next.RecurringRules) {
                if (processed >= RecurringTasks.MaximumBatchSize) break;
                if (rule.IsPaused || rule.IsStopped) continue;
                DateTime? from = RecurringTasks.PendingStart(rule);
                if (!from.HasValue || from.Value > today) continue;
                if (!includeMissed) {
                    DateTime? occurrence = RecurringTasks.NextOnOrAfter(rule, today);
                    if (occurrence.HasValue && occurrence.Value == today && from.Value <= today) {
                        if (AddOccurrence(next, rule, today, stamp) != null) generated++;
                        processed++;
                    }
                    AdvanceCursor(rule, Later(rule.ProcessedThrough, today));
                    continue;
                }
                DateTime? due = RecurringTasks.NextOnOrAfter(rule, from.Value);
                while (due.HasValue && due.Value <= today && processed < RecurringTasks.MaximumBatchSize) {
                    if (AddOccurrence(next, rule, due.Value, stamp) != null) generated++;
                    AdvanceCursor(rule, due.Value); processed++;
                    if (due.Value == DateTime.MaxValue.Date) break;
                    due = RecurringTasks.NextOnOrAfter(rule, due.Value.AddDays(1));
                }
            }
            if (generated > 0 || !CursorsEqual(current, next)) Commit(next);
            return generated;
        }

        public RecurringTaskRule GetRecurringRule(string id)
        {
            return RecurringTasks.Copy(FindRule(current, id));
        }

        public void EditRecurringRule(string id, TaskDraft draft)
        {
            TaskDraft input = ReadDraft(draft);
            if (input.Recurrence == null) throw new ArgumentException("请设置重复方式；停止后续重复请使用停止操作。");
            DateTime stamp = now();
            TodoState next = Snapshot();
            RecurringTaskRule originalRule = FindRule(next, id);
            if (originalRule.IsStopped) throw new InvalidOperationException("此重复规则已停止。");
            RequireNoMissed(originalRule, stamp);
            RecurringTaskRule updated = RuleFromDraft(originalRule.Id, input, stamp);
            // A current period belongs to the old rule even if a timer has not run yet.
            GenerateToday(next, originalRule, stamp);
            updated.IsPaused = originalRule.IsPaused;
            updated.ProcessedThrough = Later(originalRule.ProcessedThrough, RecurringTasks.LocalDate(stamp));
            updated.PreparedOccurrences = originalRule.PreparedOccurrences.Where(date => date > updated.ProcessedThrough.Value).ToList();
            if (updated.ReminderEnabled && originalRule.ReminderEnabled && updated.ReminderMinutesBefore == originalRule.ReminderMinutesBefore &&
                updated.DeadlineOffsetDays == originalRule.DeadlineOffsetDays && updated.DeadlineTimeTicks == originalRule.DeadlineTimeTicks &&
                SameRecurrenceSchedule(updated.Recurrence, originalRule.Recurrence))
                updated.ReminderSetAt = originalRule.ReminderSetAt;
            next.RecurringRules[next.RecurringRules.IndexOf(originalRule)] = updated;
            Commit(next);
        }

        private static bool SameRecurrenceSchedule(RecurrenceOptions first, RecurrenceOptions second)
        {
            if (first.Frequency != second.Frequency || first.StartDate != second.StartDate) return false;
            if (first.Frequency == RecurrenceFrequency.Weekly) return first.WeekDays == second.WeekDays;
            if (first.Frequency == RecurrenceFrequency.Monthly)
                return first.UseLastDay == second.UseLastDay && (first.UseLastDay || first.DayOfMonth == second.DayOfMonth);
            return true;
        }

        public void SetRecurringPaused(string id, bool paused)
        {
            RecurringTaskRule previous = FindRule(current, id);
            if (previous.IsStopped) throw new InvalidOperationException("此重复规则已停止。");
            if (previous.IsPaused == paused) return;
            DateTime stamp = now();
            if (paused) RequireNoMissed(previous, stamp);
            TodoState next = Snapshot(); RecurringTaskRule rule = FindRule(next, id);
            if (paused) GenerateToday(next, rule, stamp);
            rule.IsPaused = paused;
            if (!paused) {
                DateTime today = RecurringTasks.LocalDate(stamp);
                if (today > DateTime.MinValue.Date) AdvanceCursor(rule, Later(rule.ProcessedThrough, today.AddDays(-1)));
                // Paused intervals must not retroactively notify or prepare future periods.
                if (rule.ReminderEnabled) rule.ReminderSetAt = stamp;
            }
            Commit(next);
        }

        public void StopRecurringRule(string id)
        {
            if (FindRule(current, id).IsStopped) return;
            TodoState next = Snapshot(); RecurringTaskRule rule = FindRule(next, id);
            rule.IsStopped = true; Commit(next);
        }

        private static RecurringTaskRule FindRule(TodoState state, string id)
        {
            RecurringTaskRule rule = state.RecurringRules.FirstOrDefault(candidate => String.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (rule == null) throw new KeyNotFoundException("此重复规则已不存在。");
            return rule;
        }

        private void RequireNoMissed(RecurringTaskRule rule, DateTime stamp)
        {
            // Explicitly resolve the history before a future-only edit changes its cursor.
            if (rule.IsPaused) return;
            DateTime today = RecurringTasks.LocalDate(stamp);
            DateTime? from = RecurringTasks.PendingStart(rule);
            if (from.HasValue && today > DateTime.MinValue.Date && UnpreparedCount(rule, from.Value, today.AddDays(-1)) > 0)
                throw new InvalidOperationException("此规则还有未处理的遗漏周期，请先处理遗漏任务，再修改或暂停后续规则。");
        }

        private static RecurringTaskRule RuleFromDraft(string id, TaskDraft input, DateTime stamp)
        {
            RecurrenceOptions options = RecurringTasks.Copy(input.Recurrence);
            RecurringTasks.Validate(options);
            DateTime localDue = input.DueDate.Kind == DateTimeKind.Utc ? input.DueDate.ToLocalTime() : input.DueDate;
            int offset = input.HasDeadline ? (int)(localDue.Date - options.StartDate.Date).TotalDays : 0;
            if (offset < 0) throw new ArgumentException("重复事项的截止日期不能早于开始日期。");
            var rule = new RecurringTaskRule { Id = id, Text = input.Text, Notes = input.Notes, Priority = input.Priority,
                HasDeadline = input.HasDeadline, HasTime = input.HasTime, DeadlineOffsetDays = offset,
                DeadlineTimeTicks = input.HasTime ? localDue.TimeOfDay.Ticks : 0, Recurrence = options,
                ReminderEnabled = input.ReminderEnabled, ReminderMinutesBefore = input.ReminderEnabled ? input.ReminderMinutesBefore : 0,
                ReminderSetAt = input.ReminderEnabled ? (DateTime?)stamp : null };
            RecurringTasks.Validate(rule);
            if (!RecurringTasks.NextOnOrAfter(rule, options.StartDate).HasValue) throw new ArgumentException("重复计划超出可用日期范围。");
            return rule;
        }

        private static TodoItem GenerateToday(TodoState state, RecurringTaskRule rule, DateTime stamp)
        {
            if (rule.IsPaused || rule.IsStopped) return null;
            DateTime today = RecurringTasks.LocalDate(stamp);
            DateTime? from = RecurringTasks.PendingStart(rule);
            if (!from.HasValue || from.Value > today) return null;
            if (today > DateTime.MinValue.Date && UnpreparedCount(rule, from.Value, today.AddDays(-1)) > 0) return null;
            DateTime? occurrence = RecurringTasks.NextOnOrAfter(rule, today);
            if (!occurrence.HasValue || occurrence.Value != today) return null;
            TodoItem item = AddOccurrence(state, rule, today, stamp);
            AdvanceCursor(rule, today);
            return item;
        }

        private static TodoItem AddOccurrence(TodoState state, RecurringTaskRule rule, DateTime date, DateTime stamp)
        {
            if (rule.PreparedOccurrences.Contains(date)) return null;
            if (state.Items.Any(item => item.RecurringRuleId == rule.Id && item.OccurrenceDate == date)) return null;
            string id = rule.Id + "_" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (state.Items.Any(item => item.Id == id)) throw new InvalidOperationException("重复事项标识冲突，尚未生成。");
            var itemToAdd = new TodoItem { Id = id, Text = rule.Text, Notes = rule.Notes, Priority = rule.Priority,
                HasDeadline = rule.HasDeadline, HasTime = rule.HasTime,
                DueDate = rule.HasDeadline ? date.AddDays(rule.DeadlineOffsetDays).AddTicks(rule.DeadlineTimeTicks) : DateTime.MinValue,
                CreatedAt = stamp, DeadlineSetAt = rule.HasDeadline ? (DateTime?)stamp : null,
                RecurringRuleId = rule.Id, OccurrenceDate = date, RepeatLabel = RecurringTasks.Label(rule.Recurrence) };
            ApplyReminder(itemToAdd, rule.ReminderEnabled, rule.ReminderMinutesBefore, rule.ReminderSetAt ?? stamp, null);
            DateTime? fire = ReminderRules.GetReminderTime(itemToAdd);
            if (fire.HasValue && date < RecurringTasks.LocalDate(stamp) && ReminderRules.ToUtc(fire.Value) <= ReminderRules.ToUtc(stamp))
                itemToAdd.ReminderHandledAt = stamp;
            state.Items.Add(itemToAdd); return itemToAdd;
        }

        private static void AdvanceCursor(RecurringTaskRule rule, DateTime date)
        {
            rule.ProcessedThrough = date;
            rule.PreparedOccurrences.RemoveAll(prepared => prepared <= date);
        }

        private static long UnpreparedCount(RecurringTaskRule rule, DateTime from, DateTime to)
        {
            long count = RecurringTasks.Count(rule, from, to);
            foreach (DateTime date in rule.PreparedOccurrences) {
                if (date >= from && date <= to && RecurringTasks.Count(rule, date, date) > 0) count--;
            }
            return Math.Max(0, count);
        }

        private static void ApplyReminder(TodoItem item, bool enabled, int minutes, DateTime stamp, TodoItem previous)
        {
            if (!enabled) {
                item.ReminderEnabled = false; item.ReminderMinutesBefore = 0; item.ReminderPlanId = null;
                item.ReminderSetAt = null; item.ReminderHandledAt = null; return;
            }
            if (previous != null && previous.ReminderEnabled && previous.HasTime == item.HasTime && previous.HasDeadline == item.HasDeadline &&
                TodoRules.Deadline(previous) == TodoRules.Deadline(item) && previous.ReminderMinutesBefore == minutes) return;
            item.ReminderEnabled = true; item.ReminderMinutesBefore = minutes; item.ReminderPlanId = Guid.NewGuid().ToString("N");
            item.ReminderSetAt = stamp;
            DateTime? fire = ReminderRules.GetReminderTime(item);
            if (!fire.HasValue) throw new ArgumentException("提醒时间超出可用日期范围。");
            item.ReminderHandledAt = item.IsCompleted || ReminderRules.ToUtc(fire.Value) < ReminderRules.ToUtc(stamp) ? (DateTime?)stamp : null;
        }

        /// <summary>Atomically consumes due plans before presentation. Late recovery is one bounded batch, never a historic flood.</summary>
        public List<TodoItem> ClaimDueReminders()
        {
            DateTime stamp = now(), utc = ReminderRules.ToUtc(stamp);
            TodoState next = Snapshot();
            bool changed = PrepareReminderOccurrences(next, stamp);
            var claimed = new List<TodoItem>(); int processed = 0;
            foreach (TodoItem item in next.Items.OrderBy(candidate => ReminderRules.GetReminderTime(candidate) ?? DateTime.MaxValue).ThenBy(candidate => candidate.Id, StringComparer.Ordinal)) {
                if (!item.ReminderEnabled || item.IsCompleted || item.ReminderHandledAt.HasValue) continue;
                DateTime? fire = ReminderRules.GetReminderTime(item);
                if (!fire.HasValue || ReminderRules.ToUtc(fire.Value) > utc) continue;
                if (processed >= RecurringTasks.MaximumBatchSize) break;
                processed++;
                item.ReminderHandledAt = stamp; changed = true;
                TimeSpan late = utc - ReminderRules.ToUtc(fire.Value);
                if (late <= ReminderRules.CatchUpWindow && item.ReminderSetAt.HasValue && ReminderRules.ToUtc(fire.Value) >= ReminderRules.ToUtc(item.ReminderSetAt.Value))
                    claimed.Add(TodoStateCopy.Copy(item));
            }
            if (changed) Commit(next);
            return claimed;
        }

        private static bool PrepareReminderOccurrences(TodoState state, DateTime stamp)
        {
            DateTime utc = ReminderRules.ToUtc(stamp);
            DateTime lowerUtc = utc.Ticks < ReminderRules.CatchUpWindow.Ticks ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) : utc - ReminderRules.CatchUpWindow;
            DateTime today = RecurringTasks.LocalDate(stamp);
            bool changed = false; int preparedCount = 0;
            foreach (RecurringTaskRule rule in state.RecurringRules) {
                if (preparedCount >= RecurringTasks.MaximumBatchSize) break;
                if (!rule.ReminderEnabled || rule.IsPaused || rule.IsStopped) continue;
                int shift = rule.ReminderMinutesBefore / 1440 - rule.DeadlineOffsetDays;
                DateTime from = ShiftDate(RecurringTasks.LocalDate(lowerUtc), shift - 2), until = ShiftDate(today, shift + 2);
                DateTime? candidate = RecurringTasks.NextOnOrAfter(rule, from);
                while (candidate.HasValue && candidate.Value <= until && preparedCount < RecurringTasks.MaximumBatchSize) {
                    DateTime date = candidate.Value;
                    // Unresolved history blocks the ordinary calendar generator, but must not
                    // also suppress today's due reminder. Keep its tombstone until that history is resolved.
                    if (date >= today && (!rule.ProcessedThrough.HasValue || date > rule.ProcessedThrough.Value) && !rule.PreparedOccurrences.Contains(date)) {
                        var probe = new TodoItem { ReminderEnabled = true, ReminderMinutesBefore = rule.ReminderMinutesBefore,
                            HasDeadline = true, HasTime = true, DueDate = date.AddDays(rule.DeadlineOffsetDays).AddTicks(rule.DeadlineTimeTicks) };
                        DateTime? fire = ReminderRules.GetReminderTime(probe);
                        if (fire.HasValue && ReminderRules.ToUtc(fire.Value) >= lowerUtc && ReminderRules.ToUtc(fire.Value) <= utc &&
                            rule.ReminderSetAt.HasValue && ReminderRules.ToUtc(fire.Value) >= ReminderRules.ToUtc(rule.ReminderSetAt.Value)) {
                            if (rule.PreparedOccurrences.Count >= 20000) throw new InvalidOperationException("提前生成的周期记录过多，请先处理遗漏周期。");
                            AddOccurrence(state, rule, date, stamp); rule.PreparedOccurrences.Add(date);
                            preparedCount++; changed = true;
                        }
                    }
                    if (date == DateTime.MaxValue.Date) break;
                    candidate = RecurringTasks.NextOnOrAfter(rule, date.AddDays(1));
                }
            }
            return changed;
        }

        private static DateTime ShiftDate(DateTime date, int days)
        {
            long ticks = date.Ticks + (long)days * TimeSpan.TicksPerDay;
            return new DateTime(Math.Max(DateTime.MinValue.Ticks, Math.Min(DateTime.MaxValue.Date.Ticks, ticks)), DateTimeKind.Unspecified).Date;
        }

        private static DateTime Later(DateTime first, DateTime second) { return first > second ? first : second; }
        private static DateTime Later(DateTime? first, DateTime second) { return first.HasValue ? Later(first.Value, second) : second; }
        private static bool CursorsEqual(TodoState first, TodoState second)
        {
            return first.RecurringRules.Count == second.RecurringRules.Count && first.RecurringRules.Zip(second.RecurringRules,
                (a, b) => a.ProcessedThrough == b.ProcessedThrough).All(equal => equal);
        }

        public void SetCompleted(string id, bool completed)
        {
            int index = Find(id);
            if (current.Items[index].IsCompleted == completed) return;
            TodoState next = Snapshot();
            TodoItem item = next.Items[index];
            item.IsCompleted = completed;
            DateTime stamp = now();
            item.CompletedAt = completed ? (DateTime?)stamp : null;
            if (item.ReminderEnabled) {
                if (completed) item.ReminderHandledAt = stamp;
                else {
                    DateTime? fire = ReminderRules.GetReminderTime(item);
                    if (fire.HasValue && ReminderRules.ToUtc(fire.Value) > ReminderRules.ToUtc(stamp)) {
                        item.ReminderPlanId = Guid.NewGuid().ToString("N"); item.ReminderSetAt = stamp; item.ReminderHandledAt = null;
                    }
                    else item.ReminderHandledAt = stamp;
                }
            }
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
            Change(delegate(TodoState next) {
                if (next.Settings.AlwaysOnTop) next.Settings.FloatingAutoExpand = enabled;
                else next.Settings.AutoExpand = enabled;
            });
        }

        public void SetAlwaysOnTop(bool enabled)
        {
            if (current.Settings.AlwaysOnTop == enabled) return;
            Change(delegate(TodoState next) { next.Settings.AlwaysOnTop = enabled; });
        }

        // Frontends without Windows floating mode still edit the desktop preference.
        public void SetDesktopAutoExpand(bool enabled)
        {
            Change(delegate(TodoState next) { next.Settings.AutoExpand = enabled; });
        }

        public void SetEdgeAutoHide(bool enabled)
        {
            if (current.Settings.EdgeAutoHide == enabled) return;
            Change(delegate(TodoState next) { next.Settings.EdgeAutoHide = enabled; });
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
            next.Version = TodoState.CurrentVersion;
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
            if (draft.HasDeadline && draft.DueDate == DateTime.MinValue) throw new ArgumentException("请选择有效的截止日期。", "draft");
            if (!Enum.IsDefined(typeof(TaskPriority), draft.Priority)) throw new ArgumentException("请选择有效的优先级。", "draft");
            string notes = draft.Notes ?? "";
            if (notes.Length > TodoRules.MaxNotesLength) throw new ArgumentException("备注最多可填写 " + TodoRules.MaxNotesLength + " 个字符。", "draft");
            ReminderRules.ValidateInput(draft.ReminderEnabled, draft.ReminderMinutesBefore, draft.HasDeadline, draft.HasTime, draft.DueDate);
            return new TaskDraft { Text = draft.Text.Trim(), Notes = notes, Priority = draft.Priority,
                HasDeadline = draft.HasDeadline, DueDate = draft.HasDeadline ? draft.DueDate : DateTime.MinValue,
                HasTime = draft.HasDeadline && draft.HasTime, Recurrence = RecurringTasks.Copy(draft.Recurrence),
                ReminderEnabled = draft.ReminderEnabled, ReminderMinutesBefore = draft.ReminderEnabled ? draft.ReminderMinutesBefore : 0 };
        }

        private static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
