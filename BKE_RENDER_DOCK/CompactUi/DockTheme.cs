using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;

namespace BKE_MediaTools.CompactUi
{
    /// <summary>
    /// Compact dock palette: dark navy surfaces, understated borders, the BKE
    /// cyan→blue accent, and yellow reserved for the active drag state only.
    /// </summary>
    internal static class DockTheme
    {
        public static readonly Color Window = Color.FromArgb(10, 15, 26);
        public static readonly Color HeaderFill = Color.FromArgb(13, 19, 32);
        public static readonly Color Divider = Color.FromArgb(28, 38, 58);
        public static readonly Color Surface = Color.FromArgb(15, 22, 37);
        public static readonly Color Card = Color.FromArgb(19, 28, 45);
        public static readonly Color CardBorder = Color.FromArgb(33, 45, 68);
        public static readonly Color DashedBorder = Color.FromArgb(56, 74, 104);
        public static readonly Color Track = Color.FromArgb(30, 41, 62);

        public static readonly Color Text = Color.FromArgb(232, 237, 245);
        public static readonly Color Muted = Color.FromArgb(146, 158, 180);
        public static readonly Color Subtle = Color.FromArgb(104, 117, 140);

        public static readonly Color AccentStart = Color.FromArgb(34, 211, 238);
        public static readonly Color AccentEnd = Color.FromArgb(59, 130, 246);

        // Drag-over emphasis (yellow) — used only for the active drop target.
        public static readonly Color DragYellow = Color.FromArgb(250, 204, 21);
        public static readonly Color DragYellowDeep = Color.FromArgb(234, 160, 8);
        public static readonly Color DragFill = Color.FromArgb(22, 24, 30);

        public static readonly Color Success = Color.FromArgb(52, 211, 153);
        public static readonly Color Failure = Color.FromArgb(248, 113, 113);

        private static readonly string[] PreferredFamilies =
        {
            "Segoe UI Variable Text", "Segoe UI", "Inter", "Helvetica Neue", "Arial", "DejaVu Sans",
        };

        private static string? _family;

        public static string FontFamilyName => _family ??= ResolveFamily();

        /// <summary>
        /// Creates a font sized in device pixels for the given scale (DPI / 96), so
        /// text scales with the layout regardless of the Graphics object's DPI.
        /// </summary>
        public static Font Font(float points, float scale, FontStyle style = FontStyle.Regular) =>
            new(FontFamilyName, points * 96f / 72f * scale, style, GraphicsUnit.Pixel);

        private static string ResolveFamily()
        {
            try
            {
                using var installed = new InstalledFontCollection();
                var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in PreferredFamilies)
                {
                    if (names.Contains(candidate)) return candidate;
                }
            }
            catch
            {
                // Fall through to the generic sans-serif family.
            }

            return FontFamily.GenericSansSerif.Name;
        }
    }
}
