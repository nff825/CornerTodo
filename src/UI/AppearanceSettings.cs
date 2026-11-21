using System;

namespace CornerTodo
{
    // Stored independently from WPF so loading data never depends on UI resources.
    public sealed class AppearanceSettings
    {
        public const double MinTextScale = 0.9;
        public const double MaxTextScale = 1.5;

        public AppearanceSettings()
        {
            BackgroundTransparency = 20;
            BackgroundColor = "#F4F9FB";
            PrimaryTextColor = "#193C50";
            SecondaryTextColor = "#537486";
            TextScale = 1.0;
        }

        // 0 means an opaque background; 100 means a fully transparent background.
        // Text and controls do not share this background-only transparency.
        public double BackgroundTransparency { get; set; }
        public string BackgroundColor { get; set; }
        public string PrimaryTextColor { get; set; }
        public string SecondaryTextColor { get; set; }
        public double TextScale { get; set; }

        public static AppearanceSettings CreateLight()
        {
            return new AppearanceSettings();
        }

        public static AppearanceSettings CreateDark()
        {
            return new AppearanceSettings {
                BackgroundTransparency = 15, BackgroundColor = "#182B38",
                PrimaryTextColor = "#F1F5F8", SecondaryTextColor = "#BAC8D2"
            };
        }

        public static AppearanceSettings CreateOriginal()
        {
            return new AppearanceSettings {
                BackgroundTransparency = 100.0 - 32.0 / 255.0 * 100.0,
                BackgroundColor = "#FFFFFF", PrimaryTextColor = "#193C50",
                SecondaryTextColor = "#537486"
            };
        }

        public AppearanceSettings Clone()
        {
            return new AppearanceSettings {
                BackgroundTransparency = BackgroundTransparency, BackgroundColor = BackgroundColor,
                PrimaryTextColor = PrimaryTextColor, SecondaryTextColor = SecondaryTextColor,
                TextScale = TextScale
            };
        }

        // Return a separate value. Repair optional appearance fields individually;
        // a bad color must never invalidate the user's task list.
        public static AppearanceSettings Normalize(AppearanceSettings value)
        {
            AppearanceSettings result = CreateLight();
            if (value == null) return result;
            if (!Double.IsNaN(value.BackgroundTransparency) && !Double.IsInfinity(value.BackgroundTransparency)
                && value.BackgroundTransparency >= 0 && value.BackgroundTransparency <= 100)
                result.BackgroundTransparency = value.BackgroundTransparency;
            result.BackgroundColor = ColorOrDefault(value.BackgroundColor, result.BackgroundColor);
            result.PrimaryTextColor = ColorOrDefault(value.PrimaryTextColor, result.PrimaryTextColor);
            result.SecondaryTextColor = ColorOrDefault(value.SecondaryTextColor, result.SecondaryTextColor);
            if (!Double.IsNaN(value.TextScale) && !Double.IsInfinity(value.TextScale)
                && value.TextScale >= MinTextScale && value.TextScale <= MaxTextScale)
                result.TextScale = value.TextScale;
            return result;
        }

        public bool Matches(AppearanceSettings other)
        {
            if (other == null) return false;
            AppearanceSettings left = Normalize(this);
            AppearanceSettings right = Normalize(other);
            return Math.Abs(left.BackgroundTransparency - right.BackgroundTransparency) < 0.000000001
                && left.BackgroundColor == right.BackgroundColor
                && left.PrimaryTextColor == right.PrimaryTextColor
                && left.SecondaryTextColor == right.SecondaryTextColor
                && Math.Abs(left.TextScale - right.TextScale) < 0.000000001;
        }

        private static string ColorOrDefault(string value, string fallback)
        {
            if (value == null || value.Length != 7 || value[0] != '#') return fallback;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return fallback;
            }
            return value.ToUpperInvariant();
        }
    }
}
