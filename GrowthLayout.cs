using System;

namespace CornerTodo
{
    /// <summary>A screen-space rectangle in device-independent pixels.</summary>
    public struct PanelBounds
    {
        public PanelBounds(double left, double top, double width, double height) : this()
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Right { get { return Left + Width; } }
        public double Bottom { get { return Top + Height; } }
    }

    /// <summary>
    /// Pure geometry for growing a panel leftward and downward from its right/top
    /// anchor. Work-area coordinates and all returned values use screen DIPs.
    /// No desktop, WPF, persistence, or animation state is accessed here.
    /// </summary>
    public static class GrowthLayout
    {
        private const double WidthFraction = 1280.0 / 2560.0;
        private const double HeightFraction = 1100.0 / 1528.0;
        private const double Margin = 8;
        private const double MinimumWidth = 320;
        private const double MinimumHeight = 280;
        private const double CollapsedHeight = 88;
        private const double DefaultWidth = 430;
        private const double DefaultHeight = 475;

        // Real monitor coordinates are far smaller. Bound corrupted/extreme
        // values before adding origins and extents so arithmetic remains finite.
        private const double CoordinateLimit = 1000000000;

        /// <summary>
        /// progress is clamped to [0,1] (NaN means zero). The baseline is limited
        /// to 320x280 or the available maximum, whichever is smaller. Collapsing
        /// uses the normalized baseline width and an 88-DIP height, regardless of
        /// progress. Near the left/bottom edge, dimensions shrink before moving
        /// an otherwise valid anchor. An anchor with no positive available space
        /// is moved just far enough to leave one DIP, or less in a tiny work area.
        ///
        /// Work areas at most 16 DIPs wide/high cannot fit two 8-DIP margins and
        /// positive content, so that dimension uses quarter-size margins.
        /// Nonpositive/nonfinite work-area extents fall back to one DIP; finite
        /// extents below one DIP are also normalized to one DIP. NaN anchors
        /// default to the upper-right usable corner; infinite anchors clamp to
        /// the corresponding edge. These fallback rules ensure positive bounds.
        /// </summary>
        public static PanelBounds Target(PanelBounds workArea, double anchorRight, double anchorTop,
            double baseWidth, double baseHeight, double progress, bool collapsed)
        {
            double workLeft = NormalizeOrigin(workArea.Left);
            double workTop = NormalizeOrigin(workArea.Top);
            double workWidth = NormalizeExtent(workArea.Width);
            double workHeight = NormalizeExtent(workArea.Height);

            double horizontalMargin = workWidth > Margin * 2 ? Margin : workWidth / 4;
            double verticalMargin = workHeight > Margin * 2 ? Margin : workHeight / 4;
            double usableLeft = workLeft + horizontalMargin;
            double usableTop = workTop + verticalMargin;
            double usableRight = workLeft + workWidth - horizontalMargin;
            double usableBottom = workTop + workHeight - verticalMargin;
            double usableWidth = usableRight - usableLeft;
            double usableHeight = usableBottom - usableTop;
            double maxWidth = Math.Min(workWidth * WidthFraction, usableWidth);
            double maxHeight = Math.Min(workHeight * HeightFraction, usableHeight);

            double baselineWidth = Clamp(NaNOr(baseWidth, DefaultWidth),
                Math.Min(MinimumWidth, maxWidth), maxWidth);
            double baselineHeight = Clamp(NaNOr(baseHeight, DefaultHeight),
                Math.Min(MinimumHeight, maxHeight), maxHeight);
            double amount = Clamp(NaNOr(progress, 0), 0, 1);
            double desiredWidth = collapsed ? baselineWidth :
                baselineWidth + (maxWidth - baselineWidth) * amount;
            double desiredHeight = collapsed ? Math.Min(CollapsedHeight, maxHeight) :
                baselineHeight + (maxHeight - baselineHeight) * amount;

            double minimumAnchorWidth = Math.Min(1, usableWidth);
            double minimumAnchorHeight = Math.Min(1, usableHeight);
            double right = Clamp(NaNOr(anchorRight, usableRight),
                usableLeft + minimumAnchorWidth, usableRight);
            double top = Clamp(NaNOr(anchorTop, usableTop),
                usableTop, usableBottom - minimumAnchorHeight);

            double width = Math.Min(desiredWidth, right - usableLeft);
            double height = Math.Min(desiredHeight, usableBottom - top);
            return new PanelBounds(right - width, top, width, height);
        }

        private static double NormalizeOrigin(double value)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? 0 :
                Clamp(value, -CoordinateLimit, CoordinateLimit);
        }

        private static double NormalizeExtent(double value)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) || value <= 0 ? 1 :
                Clamp(value, 1, CoordinateLimit);
        }

        private static double NaNOr(double value, double fallback)
        {
            return Double.IsNaN(value) ? fallback : value;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }
    }
}
