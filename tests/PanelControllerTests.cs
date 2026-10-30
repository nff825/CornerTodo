using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using CornerTodo;

internal static class PanelControllerTests
{
    private static int passed;
    private const double MaximumHeight = 1200 * (1100.0 / 1528.0);

    [STAThread]
    public static int Main()
    {
        Application application = null;
        try
        {
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Run("Construction does not access the host and initialization only calculates a baseline", Initialization);
            Run("Growth animates for three seconds with smoothstep and a fixed upper-right anchor", SmoothAnimation);
            Run("Repeating an unchanged target does not restart the animation", UnchangedTarget);
            Run("A changed target begins from the currently displayed bounds", Retarget);
            Run("External interaction pauses animation and permits an explicit immediate update", InteractionPause);
            Run("Window hover stops animation and leaving requests attention resumption", HoverEvents);
            Run("Movement updates the anchor and saves only the ordinary baseline size", Movement);
            Run("Header buttons never initiate movement", HeaderButton);
            Run("Actual Thumb routed events resize, update the baseline and request one save", ResizeEvents);
            Run("Manual resizing obeys minimums, work-area limits and disabled-grip state", ResizeLimits);
            Run("Collapsing stays at 88 DIP and turning growth off restores the baseline", CollapseAndGrowthOff);
            Run("Reset restores the default upper-right anchor without overwriting baseline dimensions", Reset);
            Run("A lost host receives no geometry calls", LostHost);
            Run("Disposal stops animation, removes event handlers and prevents further host access", Disposal);
            Run("Edge auto-hide is off by default and never changes ordinary movement or hover", EdgeDisabledByDefault);
            Run("Enabled startup remains expanded until a leave event and hides after 800 ms", EdgeDelayAndBaseline);
            Run("Hovering over the capsule never reveals it or changes its saved baseline", EdgeHoverOnly);
            Run("Deadline growth remains hidden and explicit reveal uses the latest urgency", EdgeGrowthAndReveal);
            Run("Menus and editors cancel the pending delay and receive a fresh delay when closed", EdgeExternalBlock);
            Run("Dragging away undocks and dragging back to the right re-enables auto-hide", EdgeDockMovement);
            Run("Losing mouse capture while dragging away preserves the new undocked position", EdgeLostCapture);
            Run("Disabling edge mode restores both full-panel and manual-collapse geometry", EdgeDisableRestores);
            Run("Reset and explicit reveal remain visible until another leave event", EdgeResetWaits);
            Run("A layout-induced leave after capsule reveal cannot hide it before real entry", EdgeRevealEntry);
            Run("A changed work area clamps the capsule without persisting its small dimensions", EdgeWorkAreaChange);
            Run("Keyboard text entry blocks hiding but ordinary button focus does not", EdgeKeyboardFocus);
            Run("Host loss stops the edge timer without geometry calls or state-change events", EdgeLostHost);
            Run("Disposal removes the edge timer and all reveal and auto-hide callbacks", EdgeDisposal);
            Console.WriteLine("PASS: " + passed + " panel-controller tests; all windows remained unshown and all geometry used fake hosts.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { if (application != null) application.Shutdown(); }
    }

    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("  OK " + name); }

    private static void Initialization()
    {
        using (Fixture fixture = new Fixture(false))
        {
            Assert(fixture.Host.Calls == 0 && !fixture.Controller.IsReady, "Construction accessed the host or became ready.");
            WidgetSettings settings = Settings();
            fixture.Controller.Initialize(settings);
            Assert(fixture.Controller.IsReady && fixture.Host.WorkAreaCalls == 1 && fixture.Host.SetCalls == 0,
                "Initialization should calculate bounds without moving/showing a window.");
            Bounds(new Rect(1546, 18, 430, 475), fixture.Controller.BaselineBounds, "Initial baseline");
            Assert(settings.Left == 1546 && settings.Width == 430, "Initialization mutated caller settings.");
            NoHandle(fixture.Window);
        }
    }

    private static void SmoothAnimation()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            Assert(fixture.Controller.IsAnimating && fixture.Host.SetCalls == 0, "An animated target jumped immediately.");
            fixture.Time = fixture.Time.AddSeconds(0.75); fixture.Controller.AdvanceAnimation();
            Near(430 + 570 * 0.15625, fixture.Host.Bounds.Width, "Quarter-time smoothstep width");
            Near(475 + (MaximumHeight - 475) * 0.15625, fixture.Host.Bounds.Height, "Quarter-time smoothstep height");
            Near(1976, fixture.Host.Bounds.Right, "Animated right anchor"); Near(18, fixture.Host.Bounds.Top, "Animated top anchor");
            fixture.Time = fixture.Time.AddSeconds(0.75); fixture.Controller.AdvanceAnimation();
            Near(715, fixture.Host.Bounds.Width, "Half-time width");
            fixture.Time = fixture.Time.AddSeconds(1.5); fixture.Controller.AdvanceAnimation();
            Near(1000, fixture.Host.Bounds.Width, "Final width"); Near(MaximumHeight, fixture.Host.Bounds.Height, "Final height");
            Assert(!fixture.Controller.IsAnimating, "Animation did not stop at three seconds.");
            Bounds(new Rect(1546, 18, 430, 475), fixture.Controller.BaselineBounds, "Growth overwrote baseline");
            Assert(fixture.Saves == 0, "Automatic growth requested a geometry save.");
            NoHandle(fixture.Window);
        }
    }

    private static void UnchangedTarget()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Time = fixture.Time.AddSeconds(1.5); fixture.Controller.AdvanceAnimation();
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Time = fixture.Time.AddSeconds(1.5); fixture.Controller.AdvanceAnimation();
            Near(1000, fixture.Host.Bounds.Width, "An unchanged target restarted the animation");
            Assert(!fixture.Controller.IsAnimating, "Unchanged target extended animation duration.");
        }
    }

    private static void Retarget()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Time = fixture.Time.AddSeconds(1.5); fixture.Controller.AdvanceAnimation();
            fixture.Controller.UpdateExpansion(0.25, false, false, false);
            fixture.Time = fixture.Time.AddSeconds(1.5); fixture.Controller.AdvanceAnimation();
            Near(643.75, fixture.Host.Bounds.Width, "Retargeting did not start at the current displayed width");
            Near(1976, fixture.Host.Bounds.Right, "Retargeted right anchor");
        }
    }

    private static void InteractionPause()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Time = fixture.Time.AddSeconds(0.75); fixture.Controller.AdvanceAnimation();
            Rect paused = fixture.Host.Bounds;
            fixture.Blocked = true; fixture.Host.ResetCalls();
            fixture.Time = fixture.Time.AddSeconds(1); fixture.Controller.AdvanceAnimation();
            fixture.Controller.UpdateExpansion(1, false, false, false);
            Assert(fixture.Controller.IsInteractionBlocked && !fixture.Controller.IsAnimating && fixture.Host.Calls == 0,
                "Blocked interaction changed geometry or left animation running.");
            Bounds(paused, fixture.Host.Bounds, "Paused bounds");
            fixture.Controller.UpdateExpansion(1, false, true, true);
            Near(1000, fixture.Host.Bounds.Width, "Explicit immediate update should be allowed during interaction");
            fixture.Blocked = false;
            fixture.Controller.UpdateExpansion(0, false, false, false);
            fixture.Time = fixture.Time.AddSeconds(3); fixture.Controller.AdvanceAnimation();
            Near(430, fixture.Host.Bounds.Width, "Animation did not resume after interaction ended");
        }
    }

    private static void HoverEvents()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Assert(!fixture.Controller.IsAnimating, "Mouse-enter event did not stop animation.");
            int before = fixture.Host.Calls;
            fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Assert(fixture.Resumes == 1 && fixture.Host.Calls == before, "Mouse-leave must request, rather than perform, urgency recomputation.");
            NoHandle(fixture.Window);
        }
    }

    private static void Movement()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, true, true);
            fixture.Controller.BeginMove(new Point(10, 10));
            Assert(fixture.Controller.IsInteractionBlocked, "Moving must block expansion.");
            fixture.Controller.MoveTo(new Point(-90, 40));
            Near(1876, fixture.Host.Bounds.Right, "Dragged right anchor"); Near(48, fixture.Host.Bounds.Top, "Dragged top anchor");
            Bounds(new Rect(1446, 48, 430, 475), fixture.Controller.BaselineBounds,
                "Dragging expanded geometry replaced baseline dimensions");
            fixture.Controller.MoveTo(new Point(-10000, -10000));
            Near(8, fixture.Host.Bounds.Left, "Drag left clamp"); Near(8, fixture.Host.Bounds.Top, "Drag top clamp");
            fixture.Controller.EndMove(); fixture.Controller.EndMove();
            Assert(!fixture.Controller.IsInteractionBlocked && fixture.Saves == 1 && fixture.Resumes == 1,
                "Ending a drag must request one save and one resumption.");
        }
    }

    private static void HeaderButton()
    {
        using (Fixture fixture = new Fixture())
        {
            Button button = new Button { Content = "More" }; fixture.Header.Children.Add(button);
            fixture.Header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = button
            });
            Assert(!fixture.Controller.IsInteractionBlocked && fixture.Host.Calls == 0,
                "A header button initiated a drag or accessed screen geometry.");
            NoHandle(fixture.Window);
        }
    }

    private static void ResizeEvents()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            Assert(fixture.Controller.IsInteractionBlocked, "Thumb drag-start did not block expansion.");
            fixture.Grip.RaiseEvent(new DragDeltaEventArgs(100, 90) { RoutedEvent = Thumb.DragDeltaEvent });
            Bounds(new Rect(1546, 18, 446, 565), fixture.Host.Bounds, "Actual Thumb drag geometry");
            Bounds(fixture.Host.Bounds, fixture.Controller.BaselineBounds, "Thumb resize did not become the baseline");
            fixture.Grip.RaiseEvent(new DragCompletedEventArgs(100, 90, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Assert(!fixture.Controller.IsInteractionBlocked && fixture.Saves == 1 && fixture.Resumes == 1,
                "Thumb drag-completed did not save/resume exactly once.");
            NoHandle(fixture.Window);
        }
    }

    private static void ResizeLimits()
    {
        using (Fixture fixture = new Fixture(false))
        {
            WidgetSettings settings = Settings(); settings.Left = 300; settings.Top = 100;
            fixture.Controller.Initialize(settings); fixture.Controller.UpdateExpansion(0, false, true, true);
            fixture.Controller.BeginResize(); fixture.Controller.ResizeBy(-10000, -10000);
            Near(320, fixture.Host.Bounds.Width, "Manual minimum width"); Near(280, fixture.Host.Bounds.Height, "Manual minimum height");
            fixture.Controller.ResizeBy(10000, 10000);
            Near(1000, fixture.Host.Bounds.Width, "Manual maximum width"); Near(MaximumHeight, fixture.Host.Bounds.Height, "Manual maximum height");
            fixture.Controller.EndResize();
            fixture.Grip.IsEnabled = false; fixture.Host.ResetCalls();
            fixture.Controller.BeginResize(); fixture.Controller.ResizeBy(100, 100); fixture.Controller.EndResize();
            Assert(fixture.Host.Calls == 0, "A disabled grip still changed baseline geometry.");
        }
    }

    private static void CollapseAndGrowthOff()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, true, false, false);
            Near(88, fixture.Host.Bounds.Height, "Collapsed height"); Near(430, fixture.Host.Bounds.Width, "Collapsed baseline width");
            Near(475, fixture.Controller.BaselineBounds.Height, "Collapse overwrote expanded baseline height");
            Assert(!fixture.Controller.IsAnimating, "Collapsed geometry should update immediately.");
            fixture.Host.ResetCalls(); fixture.Controller.BeginResize(); fixture.Controller.ResizeBy(20, 20);
            Assert(fixture.Host.Calls == 0, "A collapsed panel was manually resized.");
            fixture.Controller.UpdateExpansion(1, false, true, true);
            Near(1000, fixture.Host.Bounds.Width, "Expansion after uncollapsing");
            fixture.Controller.UpdateExpansion(0, false, true, true);
            Bounds(new Rect(1546, 18, 430, 475), fixture.Host.Bounds, "Turning growth off did not restore the baseline");
        }
    }

    private static void Reset()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.BeginMove(new Point(0, 0)); fixture.Controller.MoveTo(new Point(-1246, 62)); fixture.Controller.EndMove();
            fixture.Saves = 0;
            fixture.Controller.ResetPosition(0.5, false);
            Near(1976, fixture.Host.Bounds.Right, "Reset right anchor"); Near(18, fixture.Host.Bounds.Top, "Reset top anchor");
            Near(715, fixture.Host.Bounds.Width, "Reset retained the supplied expansion progress");
            Bounds(new Rect(1546, 18, 430, 475), fixture.Controller.BaselineBounds, "Reset changed baseline dimensions");
            Assert(fixture.Saves == 1 && !fixture.Controller.IsAnimating, "Reset should be immediate and request one save.");
        }
    }

    private static void LostHost()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Lost = true; fixture.Host.ResetCalls();
            fixture.Controller.AdvanceAnimation(); fixture.Controller.UpdateExpansion(0.5, false, true, true);
            fixture.Controller.ResetPosition(0, false); fixture.Controller.Initialize(Settings());
            fixture.Controller.BeginMove(new Point()); fixture.Controller.MoveTo(new Point(100, 100)); fixture.Controller.EndMove();
            fixture.Controller.BeginResize(); fixture.Controller.ResizeBy(10, 10); fixture.Controller.EndResize();
            Assert(fixture.Host.Calls == 0 && !fixture.Controller.IsAnimating && fixture.Saves == 0,
                "A lost host received geometry operations or persistence callbacks.");
        }
    }

    private static void Disposal()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.UpdateExpansion(1, false, false, false);
            fixture.Controller.Dispose(); fixture.Controller.Dispose(); fixture.Host.ResetCalls();
            fixture.Controller.Initialize(Settings()); fixture.Controller.UpdateExpansion(1, false, true, true);
            fixture.Controller.ResetPosition(0, false); fixture.Controller.AdvanceAnimation();
            fixture.Controller.BeginMove(new Point()); fixture.Controller.MoveTo(new Point(30, 30)); fixture.Controller.EndMove();
            fixture.Controller.BeginResize(); fixture.Controller.ResizeBy(30, 30); fixture.Controller.EndResize();
            fixture.Grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            fixture.Grip.RaiseEvent(new DragDeltaEventArgs(20, 20) { RoutedEvent = Thumb.DragDeltaEvent });
            fixture.Grip.RaiseEvent(new DragCompletedEventArgs(20, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
            fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Assert(fixture.Host.Calls == 0 && fixture.Saves == 0 && fixture.Resumes == 0,
                "Disposed controller still processed host operations or routed events.");
            Assert(!fixture.Controller.IsReady && !fixture.Controller.IsAnimating && !fixture.Controller.IsInteractionBlocked,
                "Disposed controller retained active interaction state.");
        }
    }

    private static void Leave(Fixture fixture)
    { fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent }); }
    private static void Enter(Fixture fixture)
    { fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent }); }
    private static void After(Fixture fixture, int milliseconds)
    { fixture.Time = fixture.Time.AddMilliseconds(milliseconds); fixture.Controller.PollEdgeAutoHide(); }
    private static void HideAtEdge(Fixture fixture)
    {
        fixture.Controller.SetEdgeAutoHide(true);
        Leave(fixture); After(fixture, 800);
        Assert(fixture.Controller.IsEdgeHidden, "The docked panel did not hide after leaving.");
    }

    private static void EdgeDisabledByDefault()
    {
        Assert(!new WidgetSettings().EdgeAutoHide, "Existing users were opted into edge auto-hide.");
        using (Fixture fixture = new Fixture())
        {
            Leave(fixture); After(fixture, 20000);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsEdgeTimerRunning,
                "Ordinary users were hidden or given a running edge timer.");
            Bounds(new Rect(1546, 18, 430, 475), fixture.Host.Bounds, "Default edge mode changed geometry");
        }
    }

    private static void EdgeDelayAndBaseline()
    {
        using (Fixture fixture = new Fixture(false))
        {
            WidgetSettings settings = Settings(); settings.EdgeAutoHide = true;
            fixture.Controller.Initialize(settings); fixture.Controller.UpdateExpansion(0, false, true, true);
            Rect baseline = fixture.Controller.BaselineBounds;
            Assert(fixture.Controller.IsEdgeTimerRunning && fixture.Controller.IsEdgeDocked, "Saved mode was not restored.");
            After(fixture, 10000);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsEdgeHidePending,
                "Startup hid without a leave event.");
            int notifications = 0; fixture.Controller.EdgeHiddenChanged += delegate(bool hidden) { if (hidden) notifications++; };
            Leave(fixture); After(fixture, 799);
            Assert(!fixture.Controller.IsEdgeHidden && fixture.Controller.IsEdgeHidePending, "Hide occurred before the full delay.");
            After(fixture, 1);
            Bounds(new Rect(1952, 18, 48, 152), fixture.Host.Bounds, "Capsule bounds");
            Bounds(baseline, fixture.Controller.BaselineBounds, "Hiding overwrote the expanded baseline");
            Assert(fixture.Controller.IsEdgeHidden && notifications == 1 && fixture.Saves == 0,
                "Hide must notify once without persisting capsule geometry.");
            After(fixture, 1000);
            Assert(notifications == 1, "Polling raised duplicate hidden notifications.");
        }
    }

    private static void EdgeHoverOnly()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.SetEdgeAutoHide(true);
            Leave(fixture); After(fixture, 400); Enter(fixture); After(fixture, 2000);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsEdgeHidePending, "Hover did not cancel pending hide.");
            Leave(fixture); After(fixture, 800);
            Rect baseline = fixture.Controller.BaselineBounds;
            Enter(fixture); After(fixture, 2000); Leave(fixture); After(fixture, 2000);
            Assert(fixture.Controller.IsEdgeHidden, "Hover over the capsule incorrectly expanded it.");
            Bounds(baseline, fixture.Controller.BaselineBounds, "Capsule hover overwrote baseline");
            Near(48, fixture.Host.Bounds.Width, "Capsule hover changed width");
        }
    }

    private static void EdgeGrowthAndReveal()
    {
        using (Fixture fixture = new Fixture())
        {
            HideAtEdge(fixture);
            Rect baseline = fixture.Controller.BaselineBounds;
            fixture.Controller.UpdateExpansion(1, false, true, true);
            After(fixture, 10000);
            Assert(fixture.Controller.IsEdgeHidden && !fixture.Controller.IsAnimating,
                "Urgency growth revealed the capsule or started a competing animation.");
            Near(48, fixture.Host.Bounds.Width, "Growth changed hidden geometry");
            Bounds(baseline, fixture.Controller.BaselineBounds, "Hidden growth changed baseline");
            fixture.Controller.RevealFromEdge(1, false);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsAnimating, "Explicit reveal was not immediate.");
            Near(1000, fixture.Host.Bounds.Width, "Reveal did not use current urgency");
            Near(MaximumHeight, fixture.Host.Bounds.Height, "Reveal did not use current urgency height");
            After(fixture, 10000);
            Assert(!fixture.Controller.IsEdgeHidden, "Explicit reveal immediately hid again without a leave event.");
        }
    }

    private static void EdgeExternalBlock()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.SetEdgeAutoHide(true); Leave(fixture); After(fixture, 600);
            fixture.Blocked = true; After(fixture, 5000);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsEdgeHidePending, "Editing/menu did not stop pending hide.");
            fixture.Blocked = false; fixture.Controller.PollEdgeAutoHide(); After(fixture, 799);
            Assert(!fixture.Controller.IsEdgeHidden, "The previous delay leaked past the interaction.");
            After(fixture, 1); Assert(fixture.Controller.IsEdgeHidden, "A full fresh delay did not hide after editor/menu closure.");
        }
    }

    private static void EdgeDockMovement()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.SetEdgeAutoHide(true);
            fixture.Controller.BeginMove(new Point()); fixture.Controller.MoveTo(new Point(-400, 50)); fixture.Controller.EndMove();
            Assert(!fixture.Controller.IsEdgeDocked && !fixture.Controller.IsEdgeTimerRunning, "Dragging away kept edge mode active.");
            Leave(fixture); After(fixture, 10000);
            Assert(!fixture.Controller.IsEdgeHidden, "A panel away from the right edge was hidden.");
            fixture.Controller.BeginMove(new Point()); fixture.Controller.MoveTo(new Point(1000, 0)); fixture.Controller.EndMove();
            Assert(fixture.Controller.IsEdgeDocked && fixture.Controller.IsEdgeTimerRunning, "Dragging back did not dock.");
            Near(1992, fixture.Controller.BaselineBounds.Right, "Docked expanded right anchor");
            Rect baseline = fixture.Controller.BaselineBounds;
            Leave(fixture); After(fixture, 800);
            Assert(fixture.Controller.IsEdgeHidden, "Docked panel did not hide.");
            Bounds(baseline, fixture.Controller.BaselineBounds, "Dock movement did not preserve expanded baseline");
        }
    }

    private static void EdgeDisableRestores()
    {
        using (Fixture fixture = new Fixture())
        {
            HideAtEdge(fixture); fixture.Controller.UpdateExpansion(1, false, true, true);
            fixture.Controller.SetEdgeAutoHide(false);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsEdgeTimerRunning, "Disabling edge mode left a capsule/timer.");
            Near(1000, fixture.Host.Bounds.Width, "Disable failed to restore current growth");
            Leave(fixture); After(fixture, 2000);
            Assert(!fixture.Controller.IsEdgeHidden, "Disabled mode hid after leaving.");
            fixture.Controller.UpdateExpansion(1, true, true, true);
            HideAtEdge(fixture); fixture.Controller.SetEdgeAutoHide(false);
            Near(88, fixture.Host.Bounds.Height, "Disable discarded the user's manual-collapse setting");
            Near(430, fixture.Host.Bounds.Width, "Disable overwrote collapsed baseline width");
            Near(475, fixture.Controller.BaselineBounds.Height, "Capsule saved its height as the normal baseline");
        }
    }

    private static void EdgeLostCapture()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.SetEdgeAutoHide(true); fixture.Saves = 0; fixture.Resumes = 0;
            fixture.Controller.BeginMove(new Point()); fixture.Controller.MoveTo(new Point(-400, 40));
            Rect moved = fixture.Host.Bounds;
            fixture.Header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
            Assert(!fixture.Controller.IsEdgeDocked && !fixture.Controller.IsInteractionBlocked && fixture.Saves == 1 && fixture.Resumes == 1,
                "Capture loss kept the old dock or failed to commit the move.");
            fixture.Controller.UpdateExpansion(0, false, true, true);
            Bounds(moved, fixture.Host.Bounds, "Urgency resumption pulled the interrupted drag back to the right");
            Leave(fixture); After(fixture, 2000);
            Assert(!fixture.Controller.IsEdgeHidden, "Interrupted drag away still auto-hid the panel.");
        }
    }

    private static void EdgeResetWaits()
    {
        using (Fixture fixture = new Fixture())
        {
            HideAtEdge(fixture); fixture.Controller.ResetPosition(0.5, false);
            Assert(!fixture.Controller.IsEdgeHidden, "Reset failed to reveal a hidden capsule.");
            Near(18, fixture.Host.Bounds.Top, "Reset top anchor"); Near(715, fixture.Host.Bounds.Width, "Reset progress");
            After(fixture, 20000);
            Assert(!fixture.Controller.IsEdgeHidden, "Reset disappeared before a fresh leave event.");
            Enter(fixture); Leave(fixture); After(fixture, 800); Assert(fixture.Controller.IsEdgeHidden, "Reset permanently disabled auto-hide.");
            fixture.Controller.RevealFromEdge(0, true); After(fixture, 20000);
            Assert(!fixture.Controller.IsEdgeHidden, "Manual reveal disappeared before a fresh leave event.");
            Near(88, fixture.Host.Bounds.Height, "Reveal ignored manual-collapse state");
        }
    }

    private static void EdgeRevealEntry()
    {
        using (Fixture fixture = new Fixture())
        {
            HideAtEdge(fixture); Enter(fixture);
            fixture.Controller.RevealFromEdge(0, false);
            // The capsule touches the edge but the normal panel has an 8-DIP
            // margin. Resizing alone can therefore generate this leave event.
            Leave(fixture); After(fixture, 10000);
            Assert(!fixture.Controller.IsEdgeHidden && !fixture.Controller.IsEdgeHidePending,
                "Layout-induced leave made the revealed panel disappear again.");
            Enter(fixture); Leave(fixture); After(fixture, 800);
            Assert(fixture.Controller.IsEdgeHidden, "Entering then leaving did not restore ordinary auto-hide.");
            fixture.Controller.RevealFromEdge(0, false);
            fixture.Window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            Leave(fixture); After(fixture, 800);
            Assert(fixture.Controller.IsEdgeHidden, "Moving within an already-entered expanded panel did not allow auto-hide.");
        }
    }

    private static void EdgeWorkAreaChange()
    {
        using (Fixture fixture = new Fixture())
        {
            HideAtEdge(fixture);
            Rect baseline = fixture.Controller.BaselineBounds; int saves = fixture.Saves;
            fixture.Host.Area = new Rect(100, 50, 500, 400); fixture.Controller.PollEdgeAutoHide();
            Bounds(new Rect(552, 50, 48, 152), fixture.Host.Bounds, "Capsule was not kept on the available screen");
            Bounds(baseline, fixture.Controller.BaselineBounds, "Work-area clamp persisted capsule geometry");
            Assert(fixture.Saves == saves, "Work-area capsule reposition saved tiny geometry.");
            fixture.Host.Area = new Rect(100, 50, 30, 70); fixture.Controller.PollEdgeAutoHide();
            Bounds(fixture.Host.Area, fixture.Host.Bounds, "Capsule did not fit a tiny positive work area");
            fixture.Controller.RevealFromEdge(1, false);
            Assert(fixture.Host.Bounds.Left >= fixture.Host.Area.Left && fixture.Host.Bounds.Top >= fixture.Host.Area.Top &&
                fixture.Host.Bounds.Right <= fixture.Host.Area.Right && fixture.Host.Bounds.Bottom <= fixture.Host.Area.Bottom,
                "Reveal after a work-area change left the available screen.");
        }
    }

    private static void EdgeKeyboardFocus()
    {
        Assert(PanelController.BlocksEdgeHideForKeyboard(new TextBox()), "Text entry did not block auto-hide.");
        Assert(PanelController.BlocksEdgeHideForKeyboard(new ComboBox { IsEditable = true }), "Editable combo entry did not block auto-hide.");
        Assert(PanelController.BlocksEdgeHideForKeyboard(new DatePicker()), "Date entry did not block auto-hide.");
        Assert(!PanelController.BlocksEdgeHideForKeyboard(new Button()) && !PanelController.BlocksEdgeHideForKeyboard(null),
            "Ordinary button focus permanently prevented auto-hide.");
        using (Fixture fixture = new Fixture())
        {
            Button button = new Button(); fixture.Header.Children.Add(button);
            FocusManager.SetFocusedElement(fixture.Window, button);
            HideAtEdge(fixture);
            Assert(fixture.Controller.IsEdgeHidden, "An ordinary focused button prevented hide.");
        }
    }

    private static void EdgeLostHost()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.SetEdgeAutoHide(true); Leave(fixture);
            int events = 0; fixture.Controller.EdgeHiddenChanged += delegate { events++; };
            fixture.Lost = true; fixture.Host.ResetCalls();
            After(fixture, 2000); fixture.Controller.RevealFromEdge(1, false); fixture.Controller.SetEdgeAutoHide(false);
            Assert(fixture.Host.Calls == 0 && events == 0 && !fixture.Controller.IsEdgeTimerRunning,
                "Host loss left callbacks running or accessed invalid geometry.");
        }
    }

    private static void EdgeDisposal()
    {
        using (Fixture fixture = new Fixture())
        {
            fixture.Controller.SetEdgeAutoHide(true); Leave(fixture);
            int events = 0; fixture.Controller.EdgeHiddenChanged += delegate { events++; };
            fixture.Controller.Dispose(); fixture.Host.ResetCalls();
            After(fixture, 2000); Enter(fixture); Leave(fixture); fixture.Controller.RevealFromEdge(1, false);
            fixture.Controller.SetEdgeAutoHide(false);
            Assert(fixture.Host.Calls == 0 && events == 0 && !fixture.Controller.IsEdgeTimerRunning && !fixture.Controller.IsEdgeHidden,
                "Disposed edge mode still changed state or accessed the host.");
        }
    }

    private static WidgetSettings Settings()
    { return new WidgetSettings { Width = 430, Height = 475, Left = 1546, Top = 18 }; }
    private static void NoHandle(Window window)
    { Assert(new WindowInteropHelper(window).Handle == IntPtr.Zero, "A test showed a window or created a native window handle."); }
    private static void Bounds(Rect expected, Rect actual, string message)
    {
        Near(expected.Left, actual.Left, message + " left"); Near(expected.Top, actual.Top, message + " top");
        Near(expected.Width, actual.Width, message + " width"); Near(expected.Height, actual.Height, message + " height");
    }
    private static void Near(double expected, double actual, string message)
    { Assert(Math.Abs(expected - actual) < 0.00001, message + ": expected " + expected + ", got " + actual); }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FakeHost : IPanelHost
    {
        public Rect Bounds = new Rect(1546, 18, 430, 475);
        public Rect Area = new Rect(0, 0, 2000, 1200);
        public int GetCalls, SetCalls, WorkAreaCalls;
        public int Calls { get { return GetCalls + SetCalls + WorkAreaCalls; } }
        public Rect GetBounds() { GetCalls++; return Bounds; }
        public void SetBounds(Rect bounds) { SetCalls++; Bounds = bounds; }
        public Rect WorkArea() { WorkAreaCalls++; return Area; }
        public void ResetCalls() { GetCalls = SetCalls = WorkAreaCalls = 0; }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Window Window;
        public readonly Grid Header;
        public readonly Thumb Grip;
        public readonly FakeHost Host;
        public readonly PanelController Controller;
        public DateTime Time = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        public bool Blocked, Lost;
        public int Saves, Resumes;
        public Fixture() : this(true) { }
        public Fixture(bool prepare)
        {
            Window = new Window(); Header = new Grid(); Grip = new Thumb(); Host = new FakeHost();
            Grid content = new Grid(); content.Children.Add(Header); content.Children.Add(Grip); Window.Content = content;
            Controller = new PanelController(Window, Header, Grip, Host, delegate { return Blocked; }, delegate { return Lost; },
                delegate { Saves++; }, delegate { Resumes++; }, delegate { return Time; });
            if (prepare) { Controller.Initialize(Settings()); Controller.UpdateExpansion(0, false, true, true); Host.ResetCalls(); }
        }
        public void Dispose() { Controller.Dispose(); NoHandle(Window); Window.Close(); }
    }
}
