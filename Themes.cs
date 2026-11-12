using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeekJet
{
    internal sealed class ThemePalette
    {
        internal readonly string Id, Name;
        internal readonly Color Surface, Card, Border, Text, Muted, Accent, AccentText, Preview, Hover;
        internal readonly bool Light;
        internal ThemePalette(string id, string name, bool light, string surface, string card, string border, string text, string muted, string accent, string accentText, string preview, string hover)
        {
            Id = id; Name = name; Light = light;
            Surface = Hex(surface); Card = Hex(card); Border = Hex(border); Text = Hex(text);
            Muted = Hex(muted); Accent = Hex(accent); AccentText = Hex(accentText); Preview = Hex(preview); Hover = Hex(hover);
        }
        private static Color Hex(string value) { return ColorTranslator.FromHtml(value); }
    }

    internal static class ThemeCatalog
    {
        internal static readonly ThemePalette[] All = {
            new ThemePalette("midnight", "午夜深色", false, "#141923", "#1D2633", "#303C4E", "#E8EFF8", "#9AAAC1", "#88B5FF", "#B8D2FF", "#0D1119", "#293B55"),
            new ThemePalette("paper", "云白浅色", true, "#F5F7FC", "#FFFFFF", "#D4DCE8", "#233047", "#596A82", "#356BE8", "#2858BF", "#E8EDF5", "#E4ECFD"),
            new ThemePalette("ocean", "深海青蓝", false, "#10252D", "#19333D", "#2C505B", "#E0F4F3", "#93B8BF", "#63D9CC", "#9FECDD", "#0B1B23", "#244C55"),
            new ThemePalette("ember", "暖砂琥珀", false, "#29231F", "#362F29", "#53463A", "#F8EDE0", "#C0AA95", "#EFB479", "#FFD4A5", "#1C1916", "#4B3C2D")
        };
        internal static ThemePalette Resolve(string id)
        {
            if (id == "system") return SystemLight() ? All[1] : All[0];
            foreach (var theme in All) if (theme.Id == id) return theme;
            return All[0];
        }
        internal static bool Valid(string id)
        {
            if (id == "system") return true;
            foreach (var theme in All) if (theme.Id == id) return true;
            return false;
        }
        private static bool SystemLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                    return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 0)) != 0;
            }
            catch { return false; }
        }
    }

    internal static class ThemeDrawing
    {
        internal static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            return Rounded((RectangleF)bounds, radius);
        }
        internal static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        internal static void Fill(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            using (var path = Rounded(bounds, radius))
            using (var brush = new SolidBrush(color)) graphics.FillPath(brush, path);
        }
        internal static void Stroke(Graphics graphics, Rectangle bounds, int radius, Color color, float width)
        {
            // Center the stroke on pixel centers and keep its full coverage inside
            // the card. Inset pen alignment is unreliable on antialiased curves.
            var inset = new RectangleF(bounds.X + width / 2, bounds.Y + width / 2,
                bounds.Width - width, bounds.Height - width);
            using (var path = Rounded(inset, Math.Max(1, radius - width / 2)))
            using (var pen = new Pen(color, width))
            {
                graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class ThemeMenuColors : ProfessionalColorTable
    {
        private readonly ThemePalette palette;
        internal ThemeMenuColors(ThemePalette theme) { palette = theme; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return palette.Surface; } }
        public override Color ImageMarginGradientBegin { get { return palette.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return palette.Surface; } }
        public override Color ImageMarginGradientEnd { get { return palette.Surface; } }
        public override Color MenuBorder { get { return palette.Border; } }
        public override Color MenuItemBorder { get { return palette.Hover; } }
        public override Color MenuItemSelected { get { return palette.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return palette.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return palette.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return palette.Hover; } }
        public override Color MenuItemPressedGradientMiddle { get { return palette.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return palette.Hover; } }
        public override Color CheckBackground { get { return palette.Hover; } }
        public override Color CheckSelectedBackground { get { return palette.Hover; } }
        public override Color CheckPressedBackground { get { return palette.Hover; } }
        public override Color SeparatorDark { get { return palette.Border; } }
        public override Color SeparatorLight { get { return palette.Surface; } }
    }

    internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemePalette palette;
        internal ThemeMenuRenderer(ThemePalette theme) : base(new ThemeMenuColors(theme)) { palette = theme; RoundedEdges = true; }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = palette.Muted;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? palette.Text : palette.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (var font = new Font("Segoe MDL2 Assets", 9f))
                TextRenderer.DrawText(e.Graphics, "\uE73E", font, e.ImageRectangle, palette.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
