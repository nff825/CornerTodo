using System;
using CornerTodo;

internal static class GrowthLayoutTests
{
    private static int passed;
    private const double Tolerance = 0.000001;
    private static readonly PanelBounds ReferenceArea = new PanelBounds(0, 0, 2560, 1528);

    public static int Main()
    {
        try
        {
            Run("The reference work area grows to the requested 1280x1100 limit", ReferenceMaximum);
            Run("Progress endpoints and interpolation use the normalized baseline", ProgressInterpolation);
            Run("The upper-right anchor stays fixed throughout growth", AnchorDoesNotDrift);
            Run("Growth is monotonic and always fits within the work area", MonotonicGrowth);
            Run("Equivalent physical work areas have the same cap at different DPI scales", DpiLimits);
            Run("All four corner anchors produce positive visible bounds", FourCorners);
            Run("Left and bottom constraints shrink dimensions before moving the anchor", ShrinkBeforeMoving);
            Run("Anchors outside the work area clamp to the nearest usable edge", OutsideAnchors);
            Run("Baseline dimensions obey both minimum and maximum bounds", BaselineBounds);
            Run("Collapsed panels keep baseline width and 88-DIP height", Collapsed);
            Run("Small work areas relax minimum size and, when necessary, margins", SmallAreas);
            Run("Negative monitor origins retain screen-space anchors", NegativeOrigins);
            Run("NaN and infinite values have deterministic finite results", NonfiniteValues);
            Run("Degenerate and extreme work areas cannot produce invalid rectangles", DegenerateAreas);
            Run("A wide range of geometries obey containment and cap invariants", GeometrySweep);
            Console.WriteLine("PASS: " + passed + " growth-layout tests");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static void Run(string name, Action test)
    {
        test(); passed++;
        Console.WriteLine("  OK " + name);
    }

    private static PanelBounds Reference(double progress, bool collapsed)
    {
        return GrowthLayout.Target(ReferenceArea, 2536, 18, 430, 475, progress, collapsed);
    }

    private static void ReferenceMaximum()
    {
        PanelBounds result = Reference(1, false);
        Near(1280, result.Width, "Maximum width");
        Near(1100, result.Height, "Maximum height");
        Near(2536, result.Right, "Right anchor");
        Near(18, result.Top, "Top anchor");
        Inside(result, ReferenceArea, 8, 8);
    }

    private static void ProgressInterpolation()
    {
        PanelBounds start = Reference(-4, false);
        Near(430, start.Width, "Negative progress should use the baseline width");
        Near(475, start.Height, "Negative progress should use the baseline height");
        PanelBounds middle = Reference(0.5, false);
        Near(855, middle.Width, "Halfway width");
        Near(787.5, middle.Height, "Halfway height");
        Same(Reference(1, false), Reference(4, false), "Progress above one");
    }

    private static void AnchorDoesNotDrift()
    {
        for (int i = 0; i <= 100; i++)
        {
            PanelBounds result = Reference(i / 100.0, false);
            Near(2536, result.Right, "Right anchor at step " + i);
            Near(18, result.Top, "Top anchor at step " + i);
        }
    }

    private static void MonotonicGrowth()
    {
        double previousWidth = 0, previousHeight = 0;
        for (int i = 0; i <= 200; i++)
        {
            PanelBounds result = Reference(i / 200.0, false);
            Assert(result.Width + Tolerance >= previousWidth, "Width decreased.");
            Assert(result.Height + Tolerance >= previousHeight, "Height decreased.");
            Inside(result, ReferenceArea, 8, 8);
            previousWidth = result.Width; previousHeight = result.Height;
        }
        // Constrained growth may stop along one dimension; it must never jump.
        PanelBounds constrainedStart = GrowthLayout.Target(ReferenceArea, 40, 1480, 430, 475, 0, false);
        PanelBounds constrainedEnd = GrowthLayout.Target(ReferenceArea, 40, 1480, 430, 475, 1, false);
        Same(constrainedStart, constrainedEnd, "An edge-limited panel should remain stable");
    }

    private static void DpiLimits()
    {
        double[] scales = { 1, 1.25, 1.5, 1.75, 2 };
        foreach (double scale in scales)
        {
            PanelBounds area = new PanelBounds(0, 0, 2560 / scale, 1528 / scale);
            PanelBounds result = GrowthLayout.Target(area, area.Right - 24 / scale, 18 / scale,
                430 / scale, 475 / scale, 1, false);
            Near(1280, result.Width * scale, "Physical width at DPI scale " + scale);
            Near(1100, result.Height * scale, "Physical height at DPI scale " + scale);
            Near(2536, result.Right * scale, "Physical right anchor at DPI scale " + scale);
        }
    }

    private static void FourCorners()
    {
        PanelBounds area = new PanelBounds(50, 80, 1200, 900);
        PanelBounds topRight = GrowthLayout.Target(area, area.Right, area.Top, 430, 475, 1, false);
        Near(area.Right - 8, topRight.Right, "Top-right right edge");
        Near(area.Top + 8, topRight.Top, "Top-right top edge");
        PanelBounds topLeft = GrowthLayout.Target(area, area.Left, area.Top, 430, 475, 1, false);
        Near(1, topLeft.Width, "Top-left width");
        Near(area.Left + 9, topLeft.Right, "Top-left minimally moved right anchor");
        PanelBounds bottomRight = GrowthLayout.Target(area, area.Right, area.Bottom, 430, 475, 1, false);
        Near(1, bottomRight.Height, "Bottom-right height");
        Near(area.Bottom - 9, bottomRight.Top, "Bottom-right minimally moved top anchor");
        PanelBounds bottomLeft = GrowthLayout.Target(area, area.Left, area.Bottom, 430, 475, 1, false);
        Near(1, bottomLeft.Width, "Bottom-left width");
        Near(1, bottomLeft.Height, "Bottom-left height");
        foreach (PanelBounds result in new[] { topRight, topLeft, bottomRight, bottomLeft })
            Inside(result, area, 8, 8);
    }

    private static void ShrinkBeforeMoving()
    {
        PanelBounds result = GrowthLayout.Target(ReferenceArea, 32, 1490, 430, 475, 1, false);
        Near(32, result.Right, "A valid right anchor should not move");
        Near(1490, result.Top, "A valid top anchor should not move");
        Near(24, result.Width, "Available leftward space");
        Near(30, result.Height, "Available downward space");
        Inside(result, ReferenceArea, 8, 8);
    }

    private static void OutsideAnchors()
    {
        PanelBounds farOutside = GrowthLayout.Target(ReferenceArea, 99999, -99999, 430, 475, 1, false);
        Near(2552, farOutside.Right, "Right outside anchor");
        Near(8, farOutside.Top, "Top outside anchor");
        Near(1280, farOutside.Width, "Unrestricted width after anchor clamp");
        Near(1100, farOutside.Height, "Unrestricted height after anchor clamp");
        PanelBounds opposite = GrowthLayout.Target(ReferenceArea, -99999, 99999, 430, 475, 1, false);
        Near(9, opposite.Right, "Left outside anchor");
        Near(1519, opposite.Top, "Bottom outside anchor");
        Inside(opposite, ReferenceArea, 8, 8);
    }

    private static void BaselineBounds()
    {
        PanelBounds minimum = GrowthLayout.Target(ReferenceArea, 2536, 18, -10, 0, 0, false);
        Near(320, minimum.Width, "Minimum baseline width");
        Near(280, minimum.Height, "Minimum baseline height");
        PanelBounds maximum = GrowthLayout.Target(ReferenceArea, 2536, 18, 99999, 99999, 0, false);
        Near(1280, maximum.Width, "Oversized baseline width");
        Near(1100, maximum.Height, "Oversized baseline height");
        PanelBounds small = GrowthLayout.Target(new PanelBounds(0, 0, 600, 300), 592, 8, 430, 475, 0, false);
        Near(300, small.Width, "A maximum below the nominal minimum wins");
        Assert(small.Height < 280, "Small work-area height must take precedence over 280.");
    }

    private static void Collapsed()
    {
        PanelBounds start = Reference(0, true), end = Reference(1, true);
        Same(start, end, "Collapsed geometry must ignore progress");
        Near(430, end.Width, "Collapsed baseline width");
        Near(88, end.Height, "Collapsed height");
        Near(2536, end.Right, "Collapsed right anchor");
        Near(18, end.Top, "Collapsed top anchor");
        PanelBounds limited = GrowthLayout.Target(ReferenceArea, 32, 1490, 430, 475, 1, true);
        Near(24, limited.Width, "Collapsed width limited by the left edge");
        Near(30, limited.Height, "Collapsed height limited by the bottom edge");
    }

    private static void SmallAreas()
    {
        foreach (double size in new[] { 1.0, 2, 8, 16, 17, 24, 32, 40, 120 })
        {
            PanelBounds area = new PanelBounds(0, 0, size, size);
            PanelBounds result = GrowthLayout.Target(area, size, 0, 430, 475, 1, false);
            double margin = size > 16 ? 8 : size / 4;
            Inside(result, area, margin, margin);
            Assert(result.Width <= size / 2 + Tolerance, "Tiny-area width cap was exceeded.");
            Assert(result.Height <= size * 1100 / 1528 + Tolerance, "Tiny-area height cap was exceeded.");
        }
    }

    private static void NegativeOrigins()
    {
        PanelBounds area = new PanelBounds(-1920, -1080, 1920, 1080);
        PanelBounds result = GrowthLayout.Target(area, -24, -1062, 430, 475, 1, false);
        Near(-24, result.Right, "Right anchor on a negative-origin monitor");
        Near(-1062, result.Top, "Top anchor on a negative-origin monitor");
        Near(960, result.Width, "Negative-origin monitor maximum width");
        Inside(result, area, 8, 8);
    }

    private static void NonfiniteValues()
    {
        PanelBounds nan = GrowthLayout.Target(ReferenceArea, Double.NaN, Double.NaN,
            Double.NaN, Double.NaN, Double.NaN, false);
        Near(430, nan.Width, "NaN baseline width default");
        Near(475, nan.Height, "NaN baseline height default");
        Near(2552, nan.Right, "NaN right anchor default");
        Near(8, nan.Top, "NaN top anchor default");
        Same(Reference(1, false), Reference(Double.PositiveInfinity, false), "Positive infinite progress");
        Same(Reference(0, false), Reference(Double.NegativeInfinity, false), "Negative infinite progress");
        PanelBounds infinity = GrowthLayout.Target(ReferenceArea, Double.PositiveInfinity, Double.NegativeInfinity,
            Double.PositiveInfinity, Double.PositiveInfinity, 0, false);
        Near(1280, infinity.Width, "Infinite baseline width clamps to maximum");
        Near(1100, infinity.Height, "Infinite baseline height clamps to maximum");
        Inside(infinity, ReferenceArea, 8, 8);
    }

    private static void DegenerateAreas()
    {
        PanelBounds[] invalid = {
            new PanelBounds(0, 0, 0, 0), new PanelBounds(0, 0, -500, -300),
            new PanelBounds(Double.NaN, Double.PositiveInfinity, Double.NaN, Double.NegativeInfinity),
            new PanelBounds(Double.MaxValue, -Double.MaxValue, Double.MaxValue, Double.Epsilon)
        };
        foreach (PanelBounds area in invalid)
        {
            foreach (bool collapsed in new[] { false, true })
                FinitePositive(GrowthLayout.Target(area, Double.NaN, Double.NaN,
                    Double.PositiveInfinity, Double.NaN, Double.PositiveInfinity, collapsed));
        }
    }

    private static void GeometrySweep()
    {
        Random random = new Random(1729);
        for (int i = 0; i < 500; i++)
        {
            PanelBounds area = new PanelBounds(random.Next(-4000, 4000), random.Next(-3000, 3000),
                random.Next(1, 5000), random.Next(1, 4000));
            double right = area.Left + random.NextDouble() * area.Width;
            double top = area.Top + random.NextDouble() * area.Height;
            PanelBounds result = GrowthLayout.Target(area, right, top,
                random.Next(-100, 2000), random.Next(-100, 2000), random.NextDouble(), i % 2 == 0);
            Inside(result, area, area.Width > 16 ? 8 : area.Width / 4,
                area.Height > 16 ? 8 : area.Height / 4);
            Assert(result.Width <= area.Width / 2 + Tolerance, "Sweep width cap exceeded.");
            Assert(result.Height <= area.Height * 1100 / 1528 + Tolerance, "Sweep height cap exceeded.");
        }
    }

    private static void FinitePositive(PanelBounds b)
    {
        foreach (double value in new[] { b.Left, b.Top, b.Width, b.Height, b.Right, b.Bottom })
            Assert(!Double.IsNaN(value) && !Double.IsInfinity(value), "A bound is nonfinite.");
        Assert(b.Width > 0 && b.Height > 0, "A dimension is nonpositive.");
    }

    private static void Inside(PanelBounds b, PanelBounds area, double horizontalMargin, double verticalMargin)
    {
        FinitePositive(b);
        Assert(b.Left >= area.Left + horizontalMargin - Tolerance, "Panel crossed the left margin.");
        Assert(b.Top >= area.Top + verticalMargin - Tolerance, "Panel crossed the top margin.");
        Assert(b.Right <= area.Right - horizontalMargin + Tolerance, "Panel crossed the right margin.");
        Assert(b.Bottom <= area.Bottom - verticalMargin + Tolerance, "Panel crossed the bottom margin.");
    }

    private static void Same(PanelBounds expected, PanelBounds actual, string context)
    {
        Near(expected.Left, actual.Left, context + " left"); Near(expected.Top, actual.Top, context + " top");
        Near(expected.Width, actual.Width, context + " width"); Near(expected.Height, actual.Height, context + " height");
    }

    private static void Near(double expected, double actual, string message)
    {
        Assert(Math.Abs(expected - actual) <= Tolerance, message + ": expected " + expected + ", got " + actual);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
