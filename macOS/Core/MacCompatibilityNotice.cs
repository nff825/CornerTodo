using System;
using System.Collections.Generic;
using System.Linq;

namespace CornerTodo.MacCore
{
    internal static class MacCompatibilityNotice
    {
        internal static string ForState(TodoState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            var features = new List<string>();
            if (state.RecurringRules.Count > 0) features.Add("重复规则");
            if (state.Items.Any(item => item.ReminderEnabled) || state.RecurringRules.Any(rule => rule.ReminderEnabled)) features.Add("提醒配置");
            if (state.Settings.AlwaysOnTop || state.Settings.FloatingAutoExpand) features.Add("Windows 悬浮设置");
            if (state.Items.Any(item => item.IsImportant) || state.RecurringRules.Any(rule => rule.IsImportant)) features.Add("重要标记");
            if (state.Items.Any(item => item.FileLinks != null && item.FileLinks.Count > 0) ||
                state.RecurringRules.Any(rule => rule.FileLinks != null && rule.FileLinks.Count > 0)) features.Add("原文件路径");
            return features.Count == 0 ? "" : "已保留导入的" + string.Join("、", features) + "。此 Mac 开发版暂不执行重复生成、到期提醒或悬浮显示，也未提供四象限与关联文件操作。";
        }
    }
}
