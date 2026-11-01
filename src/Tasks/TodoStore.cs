using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CornerTodo
{
    public sealed class TodoStore : ITodoStore
    {
        private readonly string directory;
        private readonly JavaScriptSerializer serializer;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public TodoStore(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("必须指定数据目录。", "directory");
            this.directory = Path.GetFullPath(directory);
            DataFilePath = Path.Combine(this.directory, "todos.json");
            serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };
        }

        public string DataFilePath { get; private set; }
        public string LoadWarning { get; private set; }

        public TodoState Load()
        {
            LoadWarning = null;
            string backupPath = DataFilePath + ".bak";
            bool hasMain = File.Exists(DataFilePath);
            bool hasBackup = File.Exists(backupPath);
            if (!hasMain && !hasBackup) return new TodoState();

            TodoState state;
            string mainError = null;
            if (hasMain && TryRead(DataFilePath, out state, out mainError)) return state;

            string preservedMain = hasMain ? PreserveDamagedFile(DataFilePath) : null;
            string backupError = null;
            if (hasBackup && TryRead(backupPath, out state, out backupError))
            {
                LoadWarning = hasMain
                    ? "待办数据损坏，已从上一次备份恢复。损坏文件保留在：" + preservedMain
                    : "待办数据文件缺失，已从上一次备份恢复。";
                try
                {
                    WriteAtomic(state, false);
                }
                catch (IOException error)
                {
                    LoadWarning += " 当前已读取备份，但恢复写入失败：" + error.Message;
                }
                catch (UnauthorizedAccessException error)
                {
                    LoadWarning += " 当前已读取备份，但恢复写入失败：" + error.Message;
                }
                return state;
            }

            string preservedBackup = hasBackup ? PreserveDamagedFile(backupPath) : null;
            LoadWarning = "未找到可用的待办数据，当前显示为空列表。原始文件已保留";
            if (preservedMain != null) LoadWarning += "：" + preservedMain;
            if (preservedBackup != null) LoadWarning += "；" + preservedBackup;
            LoadWarning += "。";
            if (mainError != null) LoadWarning += " 数据错误：" + mainError;
            else if (backupError != null) LoadWarning += " 备份错误：" + backupError;
            return new TodoState();
        }

        public void Save(TodoState state)
        {
            Validate(state);
            WriteAtomic(state, true);
        }

        private void WriteAtomic(TodoState state, bool keepPrevious)
        {
            Validate(state);
            string json = serializer.Serialize(ToJson(state));
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, ".todos-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = Utf8.GetBytes(json);
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(DataFilePath))
                {
                    // File.Replace changes the destination atomically; a failed replacement
                    // leaves the previous data intact. Never delete the destination first.
                    File.Replace(temporaryPath, DataFilePath, keepPrevious ? DataFilePath + ".bak" : null);
                }
                else
                {
                    File.Move(temporaryPath, DataFilePath);
                }
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private bool TryRead(string path, out TodoState state, out string error)
        {
            state = null;
            error = null;
            try
            {
                // Access failures are surfaced to the caller. Invalid UTF-8 is treated as
                // damaged data, so its original bytes can be preserved and a backup tried.
                string json = File.ReadAllText(path, Utf8);
                object parsed = serializer.DeserializeObject(json);
                IDictionary<string, object> root = AsObject(parsed, "数据根节点");
                state = FromJson(root);
                Validate(state);
                // Read legacy documents without touching their bytes. The first explicit
                // save writes the new schema, including the optional deadline state.
                state.Version = TodoState.CurrentVersion;
                return true;
            }
            catch (ArgumentException exception) { error = exception.Message; }
            catch (InvalidOperationException exception) { error = exception.Message; }
            catch (InvalidDataException exception) { error = exception.Message; }
            catch (FormatException exception) { error = exception.Message; }
            catch (OverflowException exception) { error = exception.Message; }
            state = null;
            return false;
        }

        private string PreserveDamagedFile(string path)
        {
            string preserved = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            // If this copy fails, Load throws. The caller must not silently replace data
            // whose original bytes could not first be preserved.
            File.Copy(path, preserved, false);
            return preserved;
        }

        private static object ToJson(TodoState state)
        {
            List<object> items = new List<object>();
            foreach (TodoItem item in state.Items)
            {
                items.Add(new Dictionary<string, object>
                {
                    { "Id", item.Id }, { "Text", item.Text }, { "Notes", item.Notes ?? "" }, { "Priority", (int)item.Priority }, { "IsCompleted", item.IsCompleted },
                    { "CreatedAt", DateText(item.CreatedAt) },
                    { "CompletedAt", item.CompletedAt.HasValue ? DateText(item.CompletedAt.Value) : null },
                    { "HasDeadline", item.HasDeadline },
                    { "DueDate", item.HasDeadline ? DateText(item.DueDate) : null }, { "HasTime", item.HasTime },
                    { "DeadlineSetAt", item.DeadlineSetAt.HasValue ? DateText(item.DeadlineSetAt.Value) : null },
                    { "RecurringRuleId", item.RecurringRuleId },
                    { "OccurrenceDate", item.OccurrenceDate.HasValue ? DateText(item.OccurrenceDate.Value) : null },
                    { "RepeatLabel", item.RepeatLabel }
                });
            }
            List<object> rules = new List<object>();
            foreach (RecurringTaskRule rule in state.RecurringRules) {
                RecurrenceOptions repeat = rule.Recurrence;
                rules.Add(new Dictionary<string, object> {
                    { "Id", rule.Id }, { "Text", rule.Text }, { "Notes", rule.Notes ?? "" }, { "Priority", (int)rule.Priority },
                    { "HasDeadline", rule.HasDeadline }, { "HasTime", rule.HasTime },
                    { "DeadlineOffsetDays", rule.DeadlineOffsetDays }, { "DeadlineTimeTicks", rule.DeadlineTimeTicks },
                    { "IsPaused", rule.IsPaused }, { "IsStopped", rule.IsStopped },
                    { "ProcessedThrough", rule.ProcessedThrough.HasValue ? DateText(rule.ProcessedThrough.Value) : null },
                    { "Recurrence", new Dictionary<string, object> {
                        { "Frequency", (int)repeat.Frequency }, { "WeekDays", repeat.WeekDays },
                        { "DayOfMonth", repeat.DayOfMonth }, { "UseLastDay", repeat.UseLastDay }, { "StartDate", DateText(repeat.StartDate) }
                    } }
                });
            }
            WidgetSettings settings = state.Settings;
            AppearanceSettings appearance = AppearanceSettings.Normalize(settings.Appearance);
            return new Dictionary<string, object>
            {
                { "Version", TodoState.CurrentVersion }, { "Items", items }, { "RecurringRules", rules },
                { "Settings", new Dictionary<string, object>
                    {
                        { "Left", settings.Left }, { "Top", settings.Top },
                        { "Width", settings.Width }, { "Height", settings.Height },
                        { "Collapsed", settings.Collapsed },
                        { "CompletedCollapsed", settings.CompletedCollapsed },
                        { "AutoExpand", settings.AutoExpand },
                        { "EdgeAutoHide", settings.EdgeAutoHide },
                        { "SortMode", (int)settings.SortMode },
                        { "Appearance", new Dictionary<string, object>
                            {
                                { "BackgroundTransparency", appearance.BackgroundTransparency },
                                { "BackgroundColor", appearance.BackgroundColor },
                                { "PrimaryTextColor", appearance.PrimaryTextColor },
                                { "SecondaryTextColor", appearance.SecondaryTextColor }
                            }
                        }
                    }
                }
            };
        }

        private static TodoState FromJson(IDictionary<string, object> root)
        {
            TodoState state = new TodoState();
            state.Version = 1; // A document without a version belongs to the original schema.
            // An existing document without appearance fields predates appearance
            // settings. Keep its previous white, highly transparent background.
            state.Settings.Appearance = AppearanceSettings.CreateOriginal();
            object value;
            if (root.TryGetValue("Version", out value))
            {
                if (!(value is int)) throw new InvalidDataException("Version 必须为整数。");
                state.Version = (int)value;
            }
            ValidateVersion(state.Version);
            if (!root.TryGetValue("Items", out value) || !(value is IEnumerable) || value is string || value is IDictionary)
                throw new InvalidDataException("缺少有效的 Items 列表。");

            foreach (object entry in (IEnumerable)value)
            {
                IDictionary<string, object> item = AsObject(entry, "待办项");
                bool hasDeadline = Boolean(item, "HasDeadline", true);
                if (state.Version == 1 && !hasDeadline)
                    throw new InvalidDataException("版本 1 的待办数据不能声明无截止日期。");
                if (!hasDeadline && item.TryGetValue("DueDate", out value) && value != null)
                    throw new InvalidDataException("无截止日期的事项不能同时保存截止日期。");
                TodoItem model = new TodoItem
                {
                    Id = RequiredString(item, "Id"),
                    Text = RequiredString(item, "Text"),
                    Notes = OptionalNotes(item),
                    Priority = ReadPriority(item),
                    CreatedAt = RequiredDate(item, "CreatedAt"),
                    HasDeadline = hasDeadline,
                    DueDate = hasDeadline ? RequiredDate(item, "DueDate") : DateTime.MinValue,
                    IsCompleted = Boolean(item, "IsCompleted", false),
                    HasTime = Boolean(item, "HasTime", false)
                };
                if (item.TryGetValue("CompletedAt", out value) && value != null)
                    model.CompletedAt = ParseDate(value, "CompletedAt");
                if (item.TryGetValue("DeadlineSetAt", out value) && value != null)
                    model.DeadlineSetAt = ParseDate(value, "DeadlineSetAt");
                model.RecurringRuleId = OptionalText(item, "RecurringRuleId");
                model.RepeatLabel = OptionalText(item, "RepeatLabel");
                if (item.TryGetValue("OccurrenceDate", out value) && value != null) model.OccurrenceDate = ParseDate(value, "OccurrenceDate");
                state.Items.Add(model);
            }

            if (root.TryGetValue("RecurringRules", out value) && value != null) {
                if (!(value is IEnumerable) || value is string || value is IDictionary) throw new InvalidDataException("重复规则列表无效。");
                foreach (object entry in (IEnumerable)value) {
                    IDictionary<string, object> fields = AsObject(entry, "重复规则");
                    object repeatValue;
                    if (!fields.TryGetValue("Recurrence", out repeatValue)) throw new InvalidDataException("重复规则缺少周期设置。");
                    IDictionary<string, object> repeat = AsObject(repeatValue, "周期设置");
                    var rule = new RecurringTaskRule {
                        Id = RequiredString(fields, "Id"), Text = RequiredString(fields, "Text"), Notes = OptionalNotes(fields),
                        Priority = (TaskPriority)Integer(fields, "Priority", 2), HasDeadline = Boolean(fields, "HasDeadline", false),
                        HasTime = Boolean(fields, "HasTime", false), DeadlineOffsetDays = Integer(fields, "DeadlineOffsetDays", 0),
                        DeadlineTimeTicks = LongInteger(fields, "DeadlineTimeTicks", 0),
                        IsPaused = Boolean(fields, "IsPaused", false), IsStopped = Boolean(fields, "IsStopped", false),
                        Recurrence = new RecurrenceOptions {
                            Frequency = (RecurrenceFrequency)Integer(repeat, "Frequency", 0),
                            WeekDays = Integer(repeat, "WeekDays", 2), DayOfMonth = Integer(repeat, "DayOfMonth", 1),
                            UseLastDay = Boolean(repeat, "UseLastDay", false), StartDate = RequiredDate(repeat, "StartDate")
                        }
                    };
                    object cursor;
                    if (fields.TryGetValue("ProcessedThrough", out cursor) && cursor != null) rule.ProcessedThrough = ParseDate(cursor, "ProcessedThrough");
                    state.RecurringRules.Add(rule);
                }
            }

            if (root.TryGetValue("Settings", out value) && value != null)
            {
                IDictionary<string, object> settings = AsObject(value, "Settings");
                state.Settings.Left = NullableNumber(settings, "Left");
                state.Settings.Top = NullableNumber(settings, "Top");
                state.Settings.Width = Number(settings, "Width", 430);
                state.Settings.Height = Number(settings, "Height", 475);
                state.Settings.Collapsed = Boolean(settings, "Collapsed", false);
                state.Settings.CompletedCollapsed = Boolean(settings, "CompletedCollapsed", false);
                state.Settings.AutoExpand = Boolean(settings, "AutoExpand", true);
                // An optional display preference must not discard otherwise valid
                // tasks. Old, missing or malformed values keep auto-hide disabled.
                state.Settings.EdgeAutoHide = settings.TryGetValue("EdgeAutoHide", out value) && value is bool && (bool)value;
                state.Settings.SortMode = settings.TryGetValue("SortMode", out value) && value is int && Enum.IsDefined(typeof(TaskSortMode), value)
                    ? (TaskSortMode)(int)value : TaskSortMode.Deadline;
                if (settings.TryGetValue("Appearance", out value))
                    state.Settings.Appearance = ReadAppearance(value);
            }
            return state;
        }

        private static AppearanceSettings ReadAppearance(object value)
        {
            IDictionary<string, object> fields = value as IDictionary<string, object>;
            if (fields == null) return AppearanceSettings.CreateLight();
            AppearanceSettings appearance = AppearanceSettings.CreateLight();
            object field;
            if (fields.TryGetValue("BackgroundTransparency", out field)
                && (field is int || field is long || field is decimal || field is double))
                appearance.BackgroundTransparency = Convert.ToDouble(field, CultureInfo.InvariantCulture);
            if (fields.TryGetValue("BackgroundColor", out field)) appearance.BackgroundColor = field as string;
            if (fields.TryGetValue("PrimaryTextColor", out field)) appearance.PrimaryTextColor = field as string;
            if (fields.TryGetValue("SecondaryTextColor", out field)) appearance.SecondaryTextColor = field as string;
            return AppearanceSettings.Normalize(appearance);
        }

        private static void Validate(TodoState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            ValidateVersion(state.Version);
            if (state.Items == null || state.Settings == null || state.RecurringRules == null) throw new InvalidDataException("待办列表、重复规则或窗口设置缺失。");
            if (state.RecurringRules.Count > RecurringTasks.MaximumRuleCount) throw new InvalidDataException("重复规则数量过多。");
            HashSet<string> ruleIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, RecurringTaskRule> ruleLookup = new Dictionary<string, RecurringTaskRule>(StringComparer.Ordinal);
            foreach (RecurringTaskRule rule in state.RecurringRules) {
                RecurringTasks.Validate(rule);
                if (!ruleIds.Add(rule.Id)) throw new InvalidDataException("重复规则标识重复。");
                ruleLookup.Add(rule.Id, rule);
            }
            if (state.Version < 3 && state.RecurringRules.Count != 0) throw new InvalidDataException("旧格式不能保存重复规则。");
            HashSet<string> occurrenceKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TodoItem item in state.Items)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Id) || String.IsNullOrWhiteSpace(item.Text))
                    throw new InvalidDataException("待办项缺少标识或内容。");
                if (!ids.Add(item.Id)) throw new InvalidDataException("待办项标识重复。");
                if (!Enum.IsDefined(typeof(TaskPriority), item.Priority)) throw new InvalidDataException("待办项优先级无效。");
                if (item.CreatedAt == DateTime.MinValue || (item.HasDeadline && item.DueDate == DateTime.MinValue))
                    throw new InvalidDataException("待办项缺少创建时间或截止日期。");
                if (!item.HasDeadline && (state.Version == 1 || item.DueDate != DateTime.MinValue || item.HasTime || item.DeadlineSetAt.HasValue))
                    throw new InvalidDataException("无截止日期的事项不能保存日期、具体时刻或计时起点。");
                if (String.IsNullOrEmpty(item.RecurringRuleId)) {
                    if (item.OccurrenceDate.HasValue || !String.IsNullOrEmpty(item.RepeatLabel)) throw new InvalidDataException("普通事项不能保存孤立的重复标识。");
                }
                else {
                    if (state.Version < 3 || !ruleIds.Contains(item.RecurringRuleId) || !item.OccurrenceDate.HasValue ||
                        item.OccurrenceDate.Value.Kind == DateTimeKind.Utc || item.OccurrenceDate.Value.TimeOfDay != TimeSpan.Zero ||
                        String.IsNullOrWhiteSpace(item.RepeatLabel)) throw new InvalidDataException("重复事项缺少有效规则、周期日期或历史说明。");
                    RecurringTaskRule owner = ruleLookup[item.RecurringRuleId];
                    if (!owner.ProcessedThrough.HasValue || item.OccurrenceDate.Value > owner.ProcessedThrough.Value)
                        throw new InvalidDataException("重复事项的周期超出已保存的处理进度。");
                    string occurrenceKey = item.RecurringRuleId + "|" + item.OccurrenceDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    if (!occurrenceKeys.Add(occurrenceKey)) throw new InvalidDataException("同一规则的同一期事项重复。");
                }
            }
            WidgetSettings settings = state.Settings;
            if (!Enum.IsDefined(typeof(TaskSortMode), settings.SortMode)) throw new InvalidDataException("排序方式无效。");
            if (!Finite(settings.Width) || !Finite(settings.Height) || settings.Width <= 0 || settings.Height <= 0)
                throw new InvalidDataException("窗口宽高必须为有效正数。");
            if ((settings.Left.HasValue && !Finite(settings.Left.Value)) || (settings.Top.HasValue && !Finite(settings.Top.Value)))
                throw new InvalidDataException("窗口位置无效。");
        }

        private static void ValidateVersion(int version)
        {
            // An unsupported schema is not corruption. In particular, never restore
            // an older backup over a valid future-version main file.
            if (version < 1 || version > TodoState.CurrentVersion)
                throw new NotSupportedException("无法读取此版本的待办数据：" + version + "。请使用支持该数据版本的程序；原数据未被改写。");
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static string DateText(DateTime value) { return value.ToString("o", CultureInfo.InvariantCulture); }

        private static IDictionary<string, object> AsObject(object value, string field)
        {
            IDictionary<string, object> result = value as IDictionary<string, object>;
            if (result == null) throw new InvalidDataException(field + "必须为对象。");
            return result;
        }

        private static string RequiredString(IDictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || !(value is string)) throw new InvalidDataException("缺少文本字段：" + key);
            return (string)value;
        }

        private static string OptionalText(IDictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return null;
            if (!(value is string)) throw new InvalidDataException("无效文本字段：" + key);
            return (string)value;
        }

        private static long LongInteger(IDictionary<string, object> source, string key, long fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value)) return fallback;
            if (!(value is int) && !(value is long)) throw new InvalidDataException("无效整数字段：" + key);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static int Integer(IDictionary<string, object> source, string key, int fallback)
        {
            return checked((int)LongInteger(source, key, fallback));
        }

        private static string OptionalNotes(IDictionary<string, object> source)
        {
            object value;
            if (!source.TryGetValue("Notes", out value) || value == null) return "";
            if (!(value is string)) throw new InvalidDataException("无效备注字段：Notes");
            // Never truncate saved text. The editor/session limit applies to new input,
            // while existing longer notes remain readable and survive unrelated saves.
            return (string)value;
        }

        private static TaskPriority ReadPriority(IDictionary<string, object> source)
        {
            object value;
            // Optional presentation/sorting fields must not discard otherwise valid tasks.
            return source.TryGetValue("Priority", out value) && value is int && Enum.IsDefined(typeof(TaskPriority), value)
                ? (TaskPriority)(int)value : TaskPriority.Normal;
        }

        private static DateTime RequiredDate(IDictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value)) throw new InvalidDataException("缺少日期字段：" + key);
            return ParseDate(value, key);
        }

        private static DateTime ParseDate(object value, string key)
        {
            if (value is DateTime) return (DateTime)value; // JavaScriptSerializer's legacy /Date(...)/ format.
            DateTime parsed;
            if (!(value is string) || !DateTime.TryParseExact((string)value, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                throw new InvalidDataException("无效日期字段：" + key);
            return parsed;
        }

        private static bool Boolean(IDictionary<string, object> source, string key, bool fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value)) return fallback;
            if (!(value is bool)) throw new InvalidDataException("无效布尔字段：" + key);
            return (bool)value;
        }

        private static double Number(IDictionary<string, object> source, string key, double fallback)
        {
            object value;
            if (!source.TryGetValue(key, out value)) return fallback;
            if (!(value is int) && !(value is long) && !(value is decimal) && !(value is double))
                throw new InvalidDataException("无效数值字段：" + key);
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static double? NullableNumber(IDictionary<string, object> source, string key)
        {
            object value;
            return !source.TryGetValue(key, out value) || value == null ? (double?)null : Number(source, key, 0);
        }
    }
}
