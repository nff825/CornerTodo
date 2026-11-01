using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CornerTodo;

internal static class ViewTests
{
    private static readonly DateTime Now = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Local);
    private static int checks;
    private static int groups;

    [STAThread]
    public static int Main()
    {
        Application application = null;
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Run("Completion request targets and failed-save visual rollback", CompletionRequests);
            Run("Edit/delete and toolbar request routing", EditDeleteAndToolbarRequests);
            Run("Completed section and collapsed-state projection", CompletedSectionProjection);
            Run("Palette and header projection without model changes", PaletteAndHeaderProjection);
            Run("Scroll position and deadline order survive repaint", ScrollProjection);
            Run("Notes entry and compact priority labels preserve task state", NotesAndPriorityProjection);
            Run("Sort selection requests and failed-save rollback", SortRequests);
            Run("Thin elapsed-time bars, hover detail and completed visibility", TimeProgressProjection);
            Run("No-deadline rows preserve task actions without time or attention output", OptionalDeadlineProjection);
            Run("Slim local scrollbar preserves paging, drag, theme and overflow behavior", SlimScrollbarBehavior);
            Run("Edge capsule counts, palette, reveal requests and restored panel", EdgeCapsuleProjection);
            Assert(application.Windows.Count == 0, "View tests unexpectedly created a Window.");
            TaskEditorTests.Run();
            RecurringUiTests.Run();
            Console.WriteLine("PASS: " + groups + " view behavior groups, " + checks + " checks.");
            Console.WriteLine("Real control trees and unshown editors; no Window.Show, store, session, Data or startup registration is used.");
            Console.WriteLine("Native double-click input still needs interactive verification; this test does not set nonpublic MouseButtonEventArgs state.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { if (application != null) application.Shutdown(); }
    }

    private sealed class Requests
    {
        internal bool AcceptCompletion;
        internal bool AcceptSort;
        internal int Adds, Collapses, Menus, CompletedSections, EdgeReveals;
        internal readonly List<Tuple<string, bool>> Completion = new List<Tuple<string, bool>>();
        internal readonly List<string> Edits = new List<string>();
        internal readonly List<string> Deletes = new List<string>();
        internal readonly List<string> Notes = new List<string>();
        internal readonly List<TaskSortMode> Sorting = new List<TaskSortMode>();
        internal readonly List<ContextMenu> Tracked = new List<ContextMenu>();
        internal TodoViewActions Actions()
        {
            return new TodoViewActions {
                Add = delegate { Adds++; }, ToggleCollapsed = delegate { Collapses++; },
                ShowMenu = delegate { Menus++; }, ToggleCompletedSection = delegate { CompletedSections++; },
                RevealFromEdge = delegate { EdgeReveals++; },
                SetCompleted = delegate(TodoItem item, bool target) { Completion.Add(Tuple.Create(item.Id, target)); return AcceptCompletion; },
                Edit = delegate(TodoItem item) { Edits.Add(item.Id); }, Delete = delegate(TodoItem item) { Deletes.Add(item.Id); },
                EditNotes = delegate(TodoItem item) { Notes.Add(item.Id); },
                SetSortMode = delegate(TaskSortMode value) { Sorting.Add(value); return AcceptSort; },
                TrackMenu = delegate(ContextMenu menu) { Tracked.Add(menu); }
            };
        }
    }

    private static TodoState Fixture()
    {
        TodoState state = new TodoState(); state.Settings.AutoExpand = false;
        state.Items.Add(Item("later", "向李老师汇报研究进展", Now.AddDays(2), false));
        state.Items.Add(Item("urgent", "确认下午的实验方案", Now.AddHours(2), false));
        state.Items.Add(Item("overdue", "发送论文修改稿", Now.AddHours(-3), false));
        state.Items.Add(Item("done", "完成阶段实验记录归档", Now.AddDays(-1), true));
        return state;
    }

    private static TodoItem Item(string id, string text, DateTime due, bool completed)
    {
        return new TodoItem { Id = id, Text = text, DueDate = due, HasTime = true,
            CreatedAt = Now.AddDays(-3), DeadlineSetAt = Now.AddDays(-2), IsCompleted = completed,
            CompletedAt = completed ? (DateTime?)Now.AddHours(-1) : null };
    }

    private static TodoPanelView CreateView(Requests requests, TodoState state)
    {
        TodoPanelView view = new TodoPanelView(requests.Actions());
        TextElement.SetFontFamily(view.Surface, new FontFamily("Microsoft YaHei UI"));
        TextElement.SetFontSize(view.Surface, 12);
        view.Render(state, Now, true, null);
        Layout(view, 430, 620);
        return view;
    }

    private static void CompletionRequests()
    {
        TodoState state = Fixture(); string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Grid row = Row(view, "later"); CheckBox check = row.Children.OfType<CheckBox>().Single();
        Assert(requests.Tracked.Contains(row.ContextMenu), "The task context menu was not handed to the coordinator.");
        check.IsChecked = true; Click(check);
        Assert(check.IsChecked == false, "A rejected completion did not restore the checkbox.");
        Assert(requests.Completion.Last().Item1 == "later" && requests.Completion.Last().Item2, "Checkbox requested the wrong task or target state.");
        MenuClick(row.ContextMenu, "标记为已完成");
        Assert(requests.Completion.Count == 2 && requests.Completion[0].Equals(requests.Completion[1]),
            "Checkbox and context menu did not request the same pending-task target.");
        requests.AcceptCompletion = true;
        check.IsChecked = true; Click(check);
        Assert(check.IsChecked == true, "An accepted request was incorrectly rolled back by the view.");
        Assert(!state.Items.Single(item => item.Id == "later").IsCompleted, "The view committed a completion itself.");
        Grid completed = Row(view, "done"); CheckBox completedCheck = completed.Children.OfType<CheckBox>().Single();
        requests.AcceptCompletion = false;
        completedCheck.IsChecked = false; Click(completedCheck);
        Assert(completedCheck.IsChecked == true, "A rejected reopen did not restore the completed checkbox.");
        Tuple<string, bool> checkboxTarget = requests.Completion.Last();
        MenuClick(completed.ContextMenu, "恢复为未完成");
        Assert(checkboxTarget.Item1 == "done" && !checkboxTarget.Item2 && checkboxTarget.Equals(requests.Completion.Last()),
            "Completed checkbox and context menu requested different reopen targets.");
        Assert(StateValue(state) == before, "Completion requests mutated the supplied state.");
    }

    private static void EditDeleteAndToolbarRequests()
    {
        TodoState state = Fixture(); string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Grid row = Row(view, "urgent");
        MenuClick(row.ContextMenu, "编辑");
        Assert(requests.Edits.SequenceEqual(new[] { "urgent" }), "Edit menu requested the wrong task.");
        StackPanel textArea = row.Children.OfType<StackPanel>().Single();
        MouseButtonEventArgs click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
        click.RoutedEvent = UIElement.MouseLeftButtonDownEvent;
        textArea.RaiseEvent(click);
        Assert(requests.Edits.SequenceEqual(new[] { "urgent" }) && !click.Handled,
            "An ordinary task-text MouseDown incorrectly requested editing.");
        MenuClick(row.ContextMenu, "删除");
        Assert(requests.Deletes.SequenceEqual(new[] { "urgent" }), "Delete menu requested the wrong task.");
        Click(view.AddButton); Click(view.CollapseButton); Click(view.MoreButton);
        Assert(requests.Adds == 1 && requests.Collapses == 1 && requests.Menus == 1, "Toolbar buttons did not forward one request each.");
        Assert(StateValue(state) == before, "Edit, delete or toolbar requests changed the supplied state.");
    }

    private static void CompletedSectionProjection()
    {
        TodoState state = Fixture(); Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        TextBlock completedText = Texts(view).Single(text => text.Text == "完成阶段实验记录归档");
        Assert(completedText.TextDecorations != null && completedText.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough),
            "Completed task lost its strikethrough.");
        Button section = Descendants(view.List).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "收起已完成");
        string before = StateValue(state); Click(section);
        Assert(requests.CompletedSections == 1 && StateValue(state) == before, "Completed section button must request, not commit, a setting change.");
        state.Settings.CompletedCollapsed = true; view.Render(state, Now, false, null); Layout(view, 430, 620);
        Assert(!Rows(view).Any(row => Object.Equals(row.Tag, "done")), "A collapsed completed section still projects task rows.");
        Assert(Descendants(view.List).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "展开已完成"), "Completed section cannot be expanded again.");
        state.Settings.Collapsed = true; view.Render(state, Now, false, null);
        Assert(view.Scroll.Visibility == Visibility.Collapsed && view.AddButton.Visibility == Visibility.Collapsed && view.Grip.Visibility == Visibility.Collapsed,
            "Panel collapse did not hide its list and footer controls.");
        Assert(view.DateText.Text == "逾期 1 项 · 6小时内 1 项" && AutomationProperties.GetName(view.CollapseButton) == "展开面板",
            "Collapsed header lost the attention summary or expand action.");
        state.Settings.Collapsed = false; view.UpdateCollapsed(state, Now);
        Assert(view.Scroll.Visibility == Visibility.Visible && view.AddButton.Visibility == Visibility.Visible, "UpdateCollapsed did not restore the panel controls.");
    }

    private static void PaletteAndHeaderProjection()
    {
        TodoState state = Fixture(); string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Grid existingRow = Row(view, "urgent");
        view.UpdateHeader(state, Now.AddHours(3), false);
        Assert(Object.ReferenceEquals(existingRow, Row(view, "urgent")), "Updating only the header rebuilt the task list during interaction.");
        Assert(view.AttentionText.Text == "逾期 2 项 · 6小时内 0 项", "Header-only time refresh projected incorrect counts.");
        AppearanceSettings dark = AppearanceSettings.CreateDark(); dark.BackgroundTransparency = 100;
        view.StatusText.Text = "示例操作失败"; view.StatusText.Visibility = Visibility.Visible;
        view.Render(state, Now, false, dark); Grid host = Layout(view, 430, 620);
        SolidColorBrush background = view.Surface.Background as SolidColorBrush;
        Assert(background != null && background.Color.A == 0 && view.Surface.Opacity == 1, "Background-only transparency changed the wrong layer.");
        TextBlock normal = Texts(view).Single(text => text.Text == "向李老师汇报研究进展");
        Assert(((SolidColorBrush)normal.Foreground).Color == (Color)ColorConverter.ConvertFromString(dark.PrimaryTextColor) && normal.Opacity == 1,
            "Previewed primary text color or opacity is incorrect.");
        Assert(((SolidColorBrush)view.DateText.Foreground).Color == (Color)ColorConverter.ConvertFromString(dark.SecondaryTextColor), "Previewed secondary text color is incorrect.");
        Assert(view.StatusText.Text == "示例操作失败" && view.StatusText.Visibility == Visibility.Visible, "Rendering erased a coordinator-owned status message.");
        Assert(((SolidColorBrush)view.ToolbarSurface.Background).Color.A > 0, "Transparent panel hid the menu controls.");
        RenderTargetBitmap bitmap = new RenderTargetBitmap(645, 930, 144, 144, PixelFormats.Pbgra32); bitmap.Render(host);
        byte[] pixels = new byte[645 * 930 * 4]; bitmap.CopyPixels(pixels, 645 * 4, 0);
        Assert(pixels.Where((value, index) => index % 4 == 3).Any(alpha => alpha > 0), "The real view tree rendered no visible pixels.");
        Assert(StateValue(state) == before, "Palette preview or header projection mutated the supplied state.");
    }

    private static void ScrollProjection()
    {
        TodoState state = new TodoState(); state.Settings.AutoExpand = false;
        for (int i = 0; i < 30; i++) state.Items.Add(Item("scroll-" + i, "滚动示例事项 " + i, Now.AddDays(i + 1), false));
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Layout(view, 430, 420); view.Scroll.ScrollToVerticalOffset(140); Layout(view, 430, 420);
        double previous = view.Scroll.VerticalOffset;
        Assert(previous > 0, "The scrolling fixture did not reach a nonzero offset.");
        view.Render(state, Now, false, AppearanceSettings.CreateDark()); Layout(view, 430, 420);
        Assert(Math.Abs(view.Scroll.VerticalOffset - previous) < 1, "A render reset the task-list scroll position.");
        Assert(Rows(view).Select(row => (string)row.Tag).SequenceEqual(state.Items.Select(item => item.Id)), "The view changed deadline order while repainting.");
    }

    private static void NotesAndPriorityProjection()
    {
        TodoState state = Fixture();
        state.Items.Single(item => item.Id == "later").Notes = "第一行备注\n联系维修人员";
        state.Items.Single(item => item.Id == "later").Priority = TaskPriority.High;
        state.Items.Single(item => item.Id == "done").Notes = "完成后仍然保留";
        state.Items.Single(item => item.Id == "overdue").Priority = TaskPriority.Low;
        string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Button[] notes = Descendants(view.List).OfType<Button>()
            .Where(button => AutomationProperties.GetName(button).StartsWith("查看备注：", StringComparison.Ordinal)).ToArray();
        Assert(notes.Length == 2, "Only tasks with notes, including completed tasks, should have note entries.");
        foreach (Button note in notes) Click(note);
        Assert(requests.Notes.SequenceEqual(new[] { "later", "done" }) && requests.Completion.Count == 0 && requests.Edits.Count == 0,
            "Clicking a notes entry must target notes, without completing or double-opening a task.");
        Assert(!Texts(view).Any(text => text.Text.Contains("联系维修人员")), "Notes leaked into the compact task list.");
        string[] labels = Descendants(view.List).OfType<Border>().Where(border => Object.Equals(border.Tag, "priority"))
            .Select(border => ((TextBlock)border.Child).Text).ToArray();
        Assert(labels.SequenceEqual(new[] { "低", "中", "高", "中" }), "Priority labels do not match tasks in deadline order.");
        Assert(!Texts(view).Any(text => text.Text == "P1" || text.Text == "P2" || text.Text == "P3"), "Numeric priority codes should not be displayed.");
        Assert(StateValue(state) == before, "Notes or priority projection changed supplied state.");
        Layout(view, 310, 420);
        foreach (Grid row in Rows(view)) {
            TextBlock title = Descendants(row).OfType<TextBlock>().Single(text => text.Text == state.Items.Single(item => item.Id == (string)row.Tag).Text);
            Assert(title.ActualWidth > 90, "Narrow panel labels crowded out task text.");
        }
    }

    private static void SortRequests()
    {
        TodoState state = Fixture(); state.Items.Single(item => item.Id == "later").Priority = TaskPriority.High;
        string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Button sort = Descendants(view.List).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "排序方式");
        ContextMenu menu = sort.ContextMenu;
        Assert(requests.Tracked.Contains(menu), "Sort menu must pause panel interactions through the coordinator.");
        MenuClick(menu, "优先级优先");
        Assert(requests.Sorting.SequenceEqual(new[] { TaskSortMode.Priority }), "Sort request selected the wrong mode.");
        Assert(menu.Items.OfType<MenuItem>().Single(item => item.IsChecked).Header.Equals("截止时间优先"), "Rejected sort did not restore the old selection.");
        Assert(StateValue(state) == before, "The view must not save sorting itself.");
        requests.AcceptSort = true; MenuClick(menu, "优先级优先");
        Assert(menu.Items.OfType<MenuItem>().Single(item => item.IsChecked).Header.Equals("优先级优先"), "Accepted mode was not reflected in the menu.");
        state.Settings.SortMode = TaskSortMode.Priority; view.Render(state, Now, false, null); Layout(view, 430, 620);
        Assert(Rows(view).First().Tag.Equals("later"), "Priority-first mode did not move a future high-priority task ahead of normal tasks.");
        sort = Descendants(view.List).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "排序方式");
        Assert(((string)sort.Content).StartsWith("优先级优先", StringComparison.Ordinal), "Sort button does not show the active mode.");
        Assert(Rows(view).Last().Tag.Equals("done"), "Sort mode moved completed tasks into pending rows.");
    }

    private static void TimeProgressProjection()
    {
        TodoState state = new TodoState(); state.Settings.AutoExpand = false;
        TodoItem half = Item("half", "五天周期示例", Now.AddDays(2.5), false);
        half.CreatedAt = Now.AddDays(-2.5); half.DeadlineSetAt = half.CreatedAt; state.Items.Add(half);
        state.Items.Add(Item("due", "截止时仍未完成", Now, false));
        state.Items.Add(Item("done", "已完成的事项", Now.AddDays(1), true));
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Assert(Descendants(view.List).OfType<Grid>().Count(grid => Object.Equals(grid.Tag, "time-progress")) == 2,
            "Only pending tasks should display time bars.");
        Grid progress = Descendants(Row(view, "half")).OfType<Grid>().Single(grid => Object.Equals(grid.Tag, "time-progress"));
        Assert(Tip(progress).Contains("50%") && Tip(progress).Contains("时间已用"), "Hover text must explain elapsed time.");
        Assert(!Texts(view).Any(text => text.Text.Contains("%")), "Time percentages should not consume permanent list space.");
        Border fill = Descendants(progress).OfType<Border>().Single(border => Object.Equals(border.Tag, "time-fill"));
        Assert(Math.Abs(fill.ActualHeight - 3) < 0.1 && Math.Abs(fill.ActualWidth / progress.ActualWidth - 0.5) < 0.01,
            "Time bar must be a thin 3-DIP line showing the elapsed fraction.");
        Grid due = Row(view, "due");
        Grid dueProgress = Descendants(due).OfType<Grid>().Single(grid => Object.Equals(grid.Tag, "time-progress"));
        Assert(Tip(dueProgress).Contains("100%") && due.Children.OfType<CheckBox>().Single().IsChecked == false,
            "A full time bar must not mark a task completed.");
        Grid originalHalfRow = Row(view, "half");
        view.UpdateTimes(Now.AddDays(1)); Layout(view, 430, 620);
        Assert(Object.ReferenceEquals(originalHalfRow, Row(view, "half")) && Tip(progress).Contains("70%"),
            "Hovered time updates must advance existing controls without rebuilding or reordering rows.");
        view.UpdateTimes(half.DueDate.AddTicks(-1));
        Assert(Tip(progress).Contains("99%"), "Rounding must not report 100% before the actual deadline.");
        view.UpdateTimes(Now.AddDays(4)); Layout(view, 430, 620);
        Assert(Tip(progress).Contains("100%") && Descendants(originalHalfRow).OfType<TextBlock>()
            .Single(text => Object.Equals(text.Tag, "deadline")).Text.Contains("已逾期"),
            "A hovered task did not switch its existing time bar and deadline text to overdue.");
        view.Render(state, Now.AddDays(1), false, AppearanceSettings.CreateDark()); Layout(view, 430, 620);
        progress = Descendants(Row(view, "half")).OfType<Grid>().Single(grid => Object.Equals(grid.Tag, "time-progress"));
        Assert(Tip(progress).Contains("70%"), "Time refresh did not advance the progress display.");
        Assert(!Descendants(Row(view, "done")).OfType<Grid>().Any(grid => Object.Equals(grid.Tag, "time-progress")), "Completed row displayed a time bar after repaint.");
    }

    private static void OptionalDeadlineProjection()
    {
        TodoState state = Fixture();
        TodoItem undated = new TodoItem { Id = "undated", Text = "整理实验数据", HasDeadline = false,
            Priority = TaskPriority.High, CreatedAt = Now.AddDays(-100), Notes = "之后再安排具体日期" };
        state.Items.Add(undated); string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Assert(Rows(view).Select(row => (string)row.Tag).SequenceEqual(new[] { "overdue", "urgent", "later", "undated", "done" }),
            "Deadline-first projection should keep undated tasks after every dated pending task.");
        Grid rowWithoutDate = Row(view, "undated");
        TextBlock title = Descendants(rowWithoutDate).OfType<TextBlock>().Single(text => text.Text == undated.Text);
        TextBlock label = Descendants(rowWithoutDate).OfType<TextBlock>().Single(text => Object.Equals(text.Tag, "deadline"));
        Assert(label.Text == "未设截止" && ((SolidColorBrush)label.Foreground).Color == view.Palette.Muted.Color,
            "Undated tasks should have a readable secondary-color deadline label.");
        Assert(title.FontSize == 15 && ((SolidColorBrush)title.Foreground).Color == view.Palette.Ink.Color,
            "Undated task titles should remain active normal-size text, regardless of task age.");
        Assert(!Descendants(rowWithoutDate).OfType<Grid>().Any(grid => Object.Equals(grid.Tag, "time-progress")),
            "Undated tasks should not display even an empty elapsed-time bar.");
        string tooltip = Tip(rowWithoutDate);
        Assert(tooltip.Contains("未设截止") && !tooltip.Contains("0001") && !tooltip.Contains("9999") && !tooltip.Contains("时间已用"),
            "Undated task tooltip exposed a sentinel date or misleading elapsed percentage.");
        Assert(view.AttentionText.Text == "逾期 1 项 · 6小时内 1 项", "Undated task incorrectly changed attention statistics.");
        view.SetEdgeHidden(true); Layout(view, 48, 152);
        Assert(AutomationProperties.GetName(view.EdgeButton).Contains("未完成 4 项，其中逾期 1 项"),
            "Undated tasks must still count as pending in the edge capsule.");
        view.SetEdgeHidden(false); Layout(view, 430, 620);
        view.UpdateTimes(Now.AddYears(3));
        Assert(Object.ReferenceEquals(rowWithoutDate, Row(view, "undated")) && label.Text == "未设截止" && Tip(rowWithoutDate) == tooltip,
            "Time refresh added attention or sentinel dates to an undated task.");
        MenuClick(rowWithoutDate.ContextMenu, "标记为已完成");
        Assert(requests.Completion.Last().Item1 == undated.Id && requests.Completion.Last().Item2,
            "Undated task lost its completion action.");
        state.Settings.SortMode = TaskSortMode.Priority; view.Render(state, Now, false, null); Layout(view, 430, 620);
        Assert(Rows(view).First().Tag.Equals("undated"), "High-priority undated task should precede lower-priority dated tasks in priority mode.");
        state.Settings.SortMode = TaskSortMode.Deadline;
        Assert(StateValue(state) == before, "Projecting or operating on an undated task mutated the caller's state.");
    }

    private static void SlimScrollbarBehavior()
    {
        TodoState state = new TodoState(); state.Settings.AutoExpand = false;
        for (int i = 0; i < 30; i++) state.Items.Add(Item("slim-" + i, "滚动测试事项 " + i, Now.AddDays(i + 1), false));
        TodoPanelView view = CreateView(new Requests(), state); Layout(view, 430, 420);
        ScrollBar bar = (ScrollBar)view.Scroll.Template.FindName("PART_VerticalScrollBar", view.Scroll);
        Assert(bar != null && bar.Visibility == Visibility.Visible && bar.Orientation == Orientation.Vertical,
            "Overflow should expose a real vertical ScrollBar in the panel's local template.");
        Assert(bar.ActualWidth >= 12 && bar.ActualWidth <= 14 && bar.Maximum == view.Scroll.ScrollableHeight &&
            Math.Abs(bar.ViewportSize - view.Scroll.ViewportHeight) < 1, "Scrollbar hit width or viewport binding is incorrect.");
        Track track = (Track)bar.Template.FindName("PART_Track", bar);
        Assert(track != null && track.Thumb != null && track.Thumb.ActualWidth >= 12,
            "Slim presentation lost the full-width draggable thumb/standard track.");
        Border indicator = (Border)track.Thumb.Template.FindName("Indicator", track.Thumb);
        Assert(indicator != null && indicator.ActualWidth >= 4 && indicator.ActualWidth <= 6 && indicator.Opacity > 0,
            "The slim scrollbar indicator is missing, too wide, or invisible.");
        Assert(Descendants(bar).OfType<RepeatButton>().Count() == 2 && track.IncreaseRepeatButton.ActualHeight > 0,
            "Transparent page areas should remain available below the initial thumb position.");
        view.Scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
        Layout(view, 430, 420);
        Assert(view.Scroll.VerticalOffset > 0, "Mouse-wheel events stopped scrolling the real styled viewer.");
        view.Scroll.ScrollToTop(); Layout(view, 430, 420);
        ScrollBar.PageDownCommand.Execute(null, view.Scroll); Layout(view, 430, 420);
        Assert(view.Scroll.VerticalOffset > 0, "The viewer's standard keyboard paging command stopped working.");
        view.Scroll.ScrollToTop(); Layout(view, 430, 420);
        ScrollBar.PageDownCommand.Execute(null, bar); Layout(view, 430, 420);
        double afterPage = view.Scroll.VerticalOffset;
        Assert(afterPage > 0, "Page-down on the styled scrollbar did not scroll its real viewer.");
        Assert(track.DecreaseRepeatButton.ActualHeight > 0 && track.IncreaseRepeatButton.ActualHeight > 0,
            "Both transparent page targets should remain available away from the top/bottom edges.");
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 20));
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 20, false));
        Layout(view, 430, 420);
        Assert(view.Scroll.VerticalOffset > afterPage, "Standard thumb dragging stopped working after styling.");
        Assert(bar.ActualWidth == 14 && indicator.ActualWidth == 5,
            "Dragging should change the scroll offset without resizing the scrollbar gutter or indicator.");
        double dragged = view.Scroll.VerticalOffset;
        AppearanceSettings dark = AppearanceSettings.CreateDark(); dark.BackgroundTransparency = 100;
        view.Render(state, Now, false, dark); Layout(view, 430, 420);
        Assert(Math.Abs(view.Scroll.VerticalOffset - dragged) < 1, "Recoloring the scrollbar reset its scroll position.");
        bar = (ScrollBar)view.Scroll.Template.FindName("PART_VerticalScrollBar", view.Scroll);
        track = (Track)bar.Template.FindName("PART_Track", bar);
        indicator = (Border)track.Thumb.Template.FindName("Indicator", track.Thumb);
        Assert(((SolidColorBrush)indicator.Background).Color.A > 0 && indicator.Opacity > 0 && ((SolidColorBrush)view.Surface.Background).Color.A == 0,
            "Fully transparent panel preferences made the scrollbar invisible.");
        Assert(((SolidColorBrush)indicator.Background).Color == ((SolidColorBrush)view.Scroll.Foreground).Color,
            "The scrollbar did not follow the panel foreground palette.");
        view.Scroll.ScrollToBottom(); Layout(view, 430, 420);
        Assert(Math.Abs(view.Scroll.VerticalOffset - view.Scroll.ScrollableHeight) < 1, "The last task could not be reached with the styled scrollbar.");
        TodoState empty = new TodoState(); view.Render(empty, Now, false, null); Layout(view, 430, 620);
        Assert(view.Scroll.ComputedVerticalScrollBarVisibility == Visibility.Collapsed, "Non-overflowing content should not reserve a visible scrollbar.");
        empty.Items.Add(Item("long", new string('长', 500), Now.AddDays(2), false));
        view.Render(empty, Now, false, null); Layout(view, 310, 300);
        Assert(view.Scroll.ScrollableHeight > 0 && view.Scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible,
            "A single long task should retain scrolling when it exceeds the available height.");
    }

    private static void EdgeCapsuleProjection()
    {
        TodoState state = Fixture(); string before = StateValue(state);
        Requests requests = new Requests(); TodoPanelView view = CreateView(requests, state);
        Assert(view.Surface.Child == view.Root, "Default view unexpectedly displayed the edge capsule.");
        view.SetEdgeHidden(true); Layout(view, 48, 152);
        Assert(view.Surface.Child == view.EdgeButton && view.Root.Parent == null, "Hidden view retained full-size content in its layout.");
        string label = AutomationProperties.GetName(view.EdgeButton);
        Assert(label.Contains("未完成 3 项") && label.Contains("其中逾期 1 项"), "Capsule counts duplicated overdue tasks or included completed tasks.");
        Assert(!view.EdgeButton.Focusable, "Capsule click can retain keyboard focus and prevent auto-hide.");
        Click(view.EdgeButton);
        Assert(requests.EdgeReveals == 1 && view.Surface.Child == view.EdgeButton, "Capsule should request reveal, not own display state.");
        Rect target = view.EdgeButton.TransformToAncestor(view.Surface).TransformBounds(new Rect(new Point(), view.EdgeButton.RenderSize));
        Assert(target.Left >= 0 && target.Right <= 48.1 && target.Top >= 0 && target.Bottom <= 152.1, "Capsule click target escaped its small bounds.");
        AppearanceSettings transparent = AppearanceSettings.CreateLight(); transparent.BackgroundTransparency = 100;
        view.Render(state, Now, false, transparent); Layout(view, 48, 152);
        Assert(view.Surface.Child == view.EdgeButton && ((SolidColorBrush)view.Surface.Background).Color.A >= 230,
            "Refreshing or making the panel transparent hid or expanded its capsule.");
        Assert(((SolidColorBrush)view.Palette.Ink).Color.A == 255, "Capsule text became transparent.");
        view.Render(state, Now, false, AppearanceSettings.CreateDark()); Layout(view, 48, 152);
        Assert(view.Surface.Child == view.EdgeButton && Texts(view).Any(text => text.Text == "3"), "Dark palette lost the capsule or its count.");
        view.SetEdgeHidden(false); Layout(view, 430, 620);
        Assert(view.Surface.Child == view.Root && view.Surface.Padding.Left == 19, "Reveal did not restore normal panel layout and padding.");
        Assert(Rows(view).Count() == 4 && StateValue(state) == before, "Edge projection changed tasks, their order or settings.");
        TodoState empty = new TodoState(); view.Render(empty, Now, false, null); view.SetEdgeHidden(true); Layout(view, 48, 152);
        Assert(AutomationProperties.GetName(view.EdgeButton).Contains("未完成 0 项，其中逾期 0 项"), "Empty capsule does not explain its zero state.");
        view.SetEdgeHidden(false); view.Render(state, Now, false, null); state.Settings.Collapsed = true;
        view.Render(state, Now, false, null); view.SetEdgeHidden(true); view.SetEdgeHidden(false);
        Assert(view.Scroll.Visibility == Visibility.Collapsed && view.CollapseButton.Content.ToString() == "⌄", "Edge reveal changed the user's separate manual collapse state.");
    }

    private static Grid Layout(TodoPanelView view, double width, double height)
    {
        Grid host = VisualTreeHelper.GetParent(view.Surface) as Grid;
        if (host == null) { host = new Grid(); host.Children.Add(view.Surface); }
        host.Width = width; host.Height = height;
        host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { })); host.UpdateLayout();
        return host;
    }

    private static string Tip(FrameworkElement element) { return Convert.ToString(((ToolTip)element.ToolTip).Content); }

    private static IEnumerable<Grid> Rows(TodoPanelView view) { return view.List.Children.OfType<Grid>().Where(row => row.Tag is string); }
    private static Grid Row(TodoPanelView view, string id) { return Rows(view).Single(row => Object.Equals(row.Tag, id)); }
    private static IEnumerable<TextBlock> Texts(TodoPanelView view) { return Descendants(view.Surface).OfType<TextBlock>(); }
    private static void Click(ButtonBase button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button)); }
    private static void MenuClick(ContextMenu menu, string caption)
    {
        MenuItem item = menu.Items.OfType<MenuItem>().Single(value => Object.Equals(value.Header, caption));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }
    private static string StateValue(TodoState state)
    {
        StringBuilder value = new StringBuilder(); WidgetSettings settings = state.Settings;
        value.Append(state.Version).Append('|').Append(settings.Left).Append('|').Append(settings.Top).Append('|').Append(settings.Width).Append('|').Append(settings.Height)
            .Append('|').Append(settings.AutoExpand).Append('|').Append(settings.EdgeAutoHide).Append('|').Append(settings.Collapsed).Append('|').Append(settings.CompletedCollapsed).Append('|').Append(settings.SortMode);
        AppearanceSettings appearance = settings.Appearance;
        value.Append('|').Append(appearance.BackgroundTransparency).Append('|').Append(appearance.BackgroundColor).Append('|').Append(appearance.PrimaryTextColor).Append('|').Append(appearance.SecondaryTextColor);
        foreach (TodoItem item in state.Items) value.Append('|').Append(item.Id).Append('|').Append(item.Text).Append('|').Append(item.Notes).Append('|').Append(item.Priority).Append('|').Append(item.IsCompleted).Append('|').Append(item.HasDeadline).Append('|').Append(item.HasTime)
            .Append('|').Append(item.CreatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|').Append(item.DueDate.ToString("O", CultureInfo.InvariantCulture))
            .Append('|').Append(item.CompletedAt.HasValue ? item.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture) : "null")
            .Append('|').Append(item.DeadlineSetAt.HasValue ? item.DeadlineSetAt.Value.ToString("O", CultureInfo.InvariantCulture) : "null");
        return value.ToString();
    }
    private static void Run(string name, Action test) { test(); groups++; Console.WriteLine("  OK " + name); }
    private static void Assert(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
}
