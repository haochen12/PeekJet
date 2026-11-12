$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class PeekJetIconAssets
{
    private static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    private static Bitmap Render(int size)
    {
        int sample = size <= 48 ? 8 : 4;
        using (var hi = new Bitmap(size * sample, size * sample, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(hi))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size * sample / 256f, size * sample / 256f);
                using (var tile = Round(8, 8, 240, 240, 56))
                using (var blue = new LinearGradientBrush(new Point(24, 24), new Point(232, 244), Color.FromArgb(59, 149, 244), Color.FromArgb(33, 77, 206)))
                {
                    g.FillPath(blue, tile);
                    using (var back = Round(88, 54, 120, 112, 18))
                    using (var pen = new Pen(Color.FromArgb(155, 238, 249, 255), size <= 24 ? 18 : 14))
                        g.DrawPath(pen, back);
                    using (var front = Round(52, 88, 144, 116, 18))
                    using (var pen = new Pen(Color.FromArgb(247, 252, 255), size <= 24 ? 18 : 16))
                    {
                        g.FillPath(blue, front); g.DrawPath(pen, front);
                    }
                    if (size >= 32)
                    {
                        using (var arrow = new GraphicsPath())
                        using (var white = new SolidBrush(Color.FromArgb(247, 252, 255)))
                        {
                            arrow.AddPolygon(new[] { new PointF(111, 119), new PointF(151, 146), new PointF(111, 173),
                                new PointF(111, 155), new PointF(125, 146), new PointF(111, 137) });
                            g.FillPath(white, arrow);
                        }
                    }
                }
            }
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(hi, new Rectangle(0, 0, size, size), 0, 0, hi.Width, hi.Height, GraphicsUnit.Pixel);
            }
            return result;
        }
    }
    public static void Generate(string root)
    {
        string branding = Path.Combine(root, "docs", "branding"); Directory.CreateDirectory(branding);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var frames = new List<byte[]>();
        foreach (int size in sizes)
            using (var bitmap = Render(size))
            using (var stream = new MemoryStream())
            { bitmap.Save(stream, ImageFormat.Png); frames.Add(stream.ToArray()); }
        using (var file = new BinaryWriter(File.Create(Path.Combine(root, "app.ico"))))
        {
            file.Write((ushort)0); file.Write((ushort)1); file.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                file.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); file.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                file.Write((byte)0); file.Write((byte)0); file.Write((ushort)1); file.Write((ushort)32);
                file.Write(frames[i].Length); file.Write(offset); offset += frames[i].Length;
            }
            foreach (var frame in frames) file.Write(frame);
        }
        using (var master = Render(256)) master.Save(Path.Combine(branding, "peekjet.png"), ImageFormat.Png);
        File.WriteAllText(Path.Combine(branding, "peekjet.svg"), @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 256 256"">
<defs><linearGradient id=""blue"" x1=""24"" y1=""24"" x2=""232"" y2=""244"" gradientUnits=""userSpaceOnUse""><stop stop-color=""#3B95F4""/><stop offset=""1"" stop-color=""#214DCE""/></linearGradient></defs>
<rect x=""8"" y=""8"" width=""240"" height=""240"" rx=""56"" fill=""url(#blue)""/>
<rect x=""88"" y=""54"" width=""120"" height=""112"" rx=""18"" fill=""none"" stroke=""#EEF9FF"" stroke-opacity="".608"" stroke-width=""14""/>
<rect x=""52"" y=""88"" width=""144"" height=""116"" rx=""18"" fill=""url(#blue)"" stroke=""#F7FCFF"" stroke-width=""16""/>
<path d=""M111 119 151 146 111 173V155L125 146 111 137Z"" fill=""#F7FCFF""/>
</svg>");
        using (var preview = new Bitmap(780, 340))
        using (var g = Graphics.FromImage(preview))
        using (var title = new Font("Segoe UI", 18, FontStyle.Bold))
        using (var text = new Font("Segoe UI", 10))
        {
            g.Clear(Color.FromArgb(242, 245, 250));
            g.FillRectangle(Brushes.White, 20, 78, 360, 240);
            using (var dark = new SolidBrush(Color.FromArgb(20, 25, 35))) g.FillRectangle(dark, 400, 78, 360, 240);
            g.DrawString("PeekJet", title, Brushes.MidnightBlue, 24, 20);
            g.DrawString("App icon / tray sizes", text, Brushes.SlateGray, 153, 29);
            foreach (int column in new[] { 0, 1 })
            {
                int left = column == 0 ? 20 : 400;
                using (var big = Render(128)) g.DrawImageUnscaled(big, left + 116, 96);
                int x = left + 40;
                foreach (int size in new[] { 16, 20, 24, 32, 48 })
                {
                    using (var icon = Render(size)) g.DrawImageUnscaled(icon, x + (48 - size) / 2, 250 + (48 - size) / 2);
                    g.DrawString(size.ToString(), text, column == 0 ? Brushes.SlateGray : Brushes.LightSlateGray, x + 13, 300);
                    x += 57;
                }
            }
            preview.Save(Path.Combine(branding, "icon-preview.png"), ImageFormat.Png);
        }
    }
}
'@
[PeekJetIconAssets]::Generate((Split-Path -Parent $PSScriptRoot))
Write-Host 'Updated app.ico and docs/branding assets.'
