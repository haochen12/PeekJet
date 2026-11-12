using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PeekJet
{
    // Developer-only preview scenes. All content is synthetic; no user editor screenshots
    // are used in the public documentation gallery.
    internal static class ThemeGallery
    {
        internal static int Run(string output)
        {
            var sources = new List<Form>();
            var images = new List<Bitmap>();
            IntPtr foreground = Native.GetForegroundWindow();
            try
            {
                Directory.CreateDirectory(output);
                string[] projects = { "orbital-web", "atlas-api", "notes-kit" };
                for (int i = 0; i < projects.Length; i++)
                {
                    var form = new ExampleEditor(projects[i], i);
                    sources.Add(form);
                    var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    images.Add(image);
                }
                using (var dock = new PreviewDock(null, true))
                {
                    dock.Show(); Application.DoEvents();
                    dock.SelectDisplay(Screen.PrimaryScreen.DeviceName);
                    dock.SelectPreviewSize(160);
                    dock.SelectEdge(DockEdge.Top);
                    dock.RefreshWindows(sources.Select(f => new CodeWindow { Handle = f.Handle, Title = f.Text }).ToList(), sources[0].Handle);
                    Application.DoEvents();
                    for (int i = 0; i < dock.Tiles.Count; i++) dock.Tiles[i].ExampleImage = images[i];
                    foreach (var theme in ThemeCatalog.All)
                    {
                        dock.SelectTheme(theme.Id);
                        var tile = dock.Tiles[1];
                        Point point = dock.PointToScreen(new Point(tile.Bounds.X + 15, tile.Bounds.Y + 15));
                        dock.UpdatePointer(new Point(-30000, -30000), false, 0);
                        dock.UpdatePointer(point, false, 0); dock.UpdatePointer(point, false, 250);
                        // Draw the actual dock renderer to a bitmap. Windows DWM surfaces cannot
                        // be captured while locked, so only this documentation path supplies
                        // rendered sample editor images instead of live user thumbnails.
                        using (var screenshot = new Bitmap(dock.Width, dock.Height))
                        {
                            dock.DrawToBitmap(screenshot, new Rectangle(Point.Empty, dock.Size));
                            using (var g = Graphics.FromImage(screenshot))
                            using (var shape = ThemeDrawing.Rounded(new Rectangle(Point.Empty, dock.Size), 15))
                            using (var corners = new Region(new Rectangle(Point.Empty, dock.Size)))
                            {
                                corners.Exclude(shape);
                                g.SetClip(corners, System.Drawing.Drawing2D.CombineMode.Replace);
                                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                                using (var transparent = new SolidBrush(Color.Transparent)) g.FillRectangle(transparent, new Rectangle(Point.Empty, dock.Size));
                            }
                            screenshot.Save(Path.Combine(output, theme.Id + ".png"));
                        }
                    }
                }
                Compose(output);
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "gallery-error.log"), error.ToString());
                return 1;
            }
            finally
            {
                foreach (var form in sources) form.Dispose();
                foreach (var image in images) image.Dispose();
                Windows.Activate(foreground);
            }
        }
        private static void Compose(string output)
        {
            var shots = ThemeCatalog.All.Select(t => new Bitmap(Path.Combine(output, t.Id + ".png"))).ToArray();
            try
            {
                int width = shots.Max(s => s.Width), height = shots.Max(s => s.Height);
                int pad = 30, labelHeight = 52, cellHeight = height + labelHeight;
                using (var gallery = new Bitmap(width * 2 + pad * 3, cellHeight * 2 + pad * 3))
                using (var g = Graphics.FromImage(gallery))
                using (var label = new Font("Microsoft YaHei UI", 12, FontStyle.Bold))
                using (var detail = new Font("Segoe UI", 9))
                {
                    g.Clear(Color.FromArgb(231, 235, 242));
                    for (int i = 0; i < shots.Length; i++)
                    {
                        int x = pad + (i % 2) * (width + pad), y = pad + (i / 2) * (cellHeight + pad);
                        g.DrawString(ThemeCatalog.All[i].Name, label, Brushes.DarkSlateGray, x, y);
                        g.DrawString(ThemeCatalog.All[i].Id.ToUpperInvariant(), detail, Brushes.SlateGray, x, y + 27);
                        g.DrawImageUnscaled(shots[i], x, y + labelHeight);
                    }
                    gallery.Save(Path.Combine(output, "gallery.png"));
                }
            }
            finally { foreach (var shot in shots) shot.Dispose(); }
        }

        private sealed class ExampleEditor : Form
        {
            private readonly string project;
            private readonly int sample;
            internal ExampleEditor(string name, int example)
            {
                project = name; sample = example;
                Text = "index.ts - " + name + " - Visual Studio Code";
                ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(Screen.PrimaryScreen.WorkingArea.Left + 80 + example * 24, Screen.PrimaryScreen.WorkingArea.Top + 250 + example * 20, 1000, 660);
                BackColor = Color.FromArgb(22, 27, 36); DoubleBuffered = true;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                using (var header = new SolidBrush(Color.FromArgb(32, 39, 50))) g.FillRectangle(header, 0, 0, Width, 44);
                using (var sidebar = new SolidBrush(Color.FromArgb(26, 33, 44))) g.FillRectangle(sidebar, 0, 44, 195, Height - 44);
                using (var font = new Font("Consolas", 14))
                using (var ui = new Font("Segoe UI", 11))
                using (var text = new SolidBrush(Color.FromArgb(184, 199, 218)))
                using (var accent = new SolidBrush(Color.FromArgb(129, 179, 236)))
                {
                    g.DrawString(project + "  /  index.ts", ui, text, 20, 12);
                    g.DrawString("EXPLORER\n\n" + project + "\n\n  src\n    index.ts\n    config.ts\n    utils.ts\n\n  tests\n  package.json\n  README.md", ui, text, 20, 72);
                    string[] samples = {
                        "import { createApp } from './app';\nimport { routes } from './routes';\n\nconst app = createApp({\n  name: 'Orbital',\n  theme: 'system',\n  routes,\n});\n\nexport async function start() {\n  await app.initialize();\n  return app.render();\n}\n\nstart();",
                        "import { router } from './router';\nimport { database } from './storage';\n\nrouter.get('/workspaces', async () => {\n  const items = await database.list();\n  return { items, total: items.length };\n});\n\nexport const config = {\n  port: 3000,\n  cache: true,\n  health: '/status',\n};",
                        "# Notes Kit\n\nA small place for useful ideas.\n\n## Getting started\n\n1. Open your workspace\n2. Make something useful\n3. Share what you learn\n\n## Tasks\n\n  [x] Keep the interface quiet\n  [x] Make the details count\n  [ ] Ship the next small improvement"
                    };
                    g.DrawString(samples[sample], font, accent, 225, 80);
                    using (var terminal = new SolidBrush(Color.FromArgb(16, 21, 29))) g.FillRectangle(terminal, 196, Height - 105, Width - 196, 105);
                    g.DrawString("TERMINAL    OUTPUT    PROBLEMS", ui, text, 215, Height - 95);
                    g.DrawString("$ npm run dev\n  ready in 218 ms  /  local workspace", font, text, 215, Height - 65);
                }
            }
        }
    }
}
