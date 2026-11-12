using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PeekJet
{
    internal static class SelfTest
    {
        internal static int RunControls()
        {
            var results = new List<string>();
            var sources = new List<Form>();
            try
            {
                results.Add("INFO: Process architecture " + (IntPtr.Size * 8) + "-bit");
                TestEdges(results);
                for (int i = 0; i < 7; i++)
                {
                    var source = new Form { Text = "Minimize test " + i, ShowInTaskbar = false };
                    sources.Add(source); source.Show();
                }
                using (var unrelated = new Form { Text = "Unrelated app", ShowInTaskbar = false })
                using (var dock = new PreviewDock(null, true))
                {
                    unrelated.Show(); dock.Show(); Application.DoEvents();
                    var windows = sources.Select(f => new CodeWindow { Handle = f.Handle, Title = f.Text }).ToList();
                    dock.RefreshWindows(windows, sources[0].Handle);
                    Check(dock.Tiles.Count < sources.Count, "Minimize test includes windows outside the current page", results);
                    var thumbnailHandles = dock.Tiles.ToDictionary(t => t.Window.Handle, t => t.Thumbnail);
                    dock.SelectPreviewSize(80);
                    Check(dock.Tiles.All(t => thumbnailHandles.ContainsKey(t.Window.Handle) && thumbnailHandles[t.Window.Handle] == t.Thumbnail),
                        "Resizing reuses existing DWM thumbnail handles", results);
                    dock.Update();
                    int invalidations = 0;
                    dock.Invalidated += delegate { invalidations++; };
                    dock.RefreshWindows(windows.Select(w => new CodeWindow { Handle = w.Handle, Title = w.Title }).ToList(), sources[0].Handle);
                    Check(invalidations == 0, "Unchanged scan does not repaint the dock", results);
                    var changedWindows = windows.Select(w => new CodeWindow { Handle = w.Handle, Title = w.Title }).ToList();
                    changedWindows[0].Title = "new.cs - changed-project - Visual Studio Code";
                    dock.RefreshWindows(changedWindows, sources[0].Handle);
                    Check(invalidations > 0 && dock.Tiles[0].Window.ProjectName == "changed-project", "Changed titles still update project labels", results);
                    dock.MinimizeAllWindows(); Pump();
                    Check(sources.All(f => Native.IsIconic(f.Handle)), "One action minimizes all tracked windows across pages", results);
                    Check(!Native.IsIconic(unrelated.Handle), "Minimize action leaves unrelated windows alone", results);
                    foreach (var window in windows) window.Minimized = Native.IsIconic(window.Handle);
                    dock.RefreshWindows(windows, IntPtr.Zero);
                    Check(dock.Visible && dock.Tiles.Count > 0, "Dock remains visible after minimizing editors", results);
                    dock.MinimizeAllWindows(); Pump();
                    Check(sources.All(f => Native.IsIconic(f.Handle)), "Repeated minimize preserves minimized windows", results);
                    Native.ShowWindowAsync(sources[0].Handle, 9); Pump();
                    Check(!Native.IsIconic(sources[0].Handle), "Minimized editors can be restored", results);
                }
                var actualWindows = Windows.Find();
                if (actualWindows.Count >= 2)
                {
                    using (var actualDock = new PreviewDock(null, true))
                    {
                        actualDock.Show();
                        actualDock.RefreshWindows(actualWindows, Native.GetForegroundWindow());
                        Check(actualDock.Tiles.Count > 0 && actualDock.Tiles.All(t => t.Thumbnail != IntPtr.Zero && t.LastError == 0 && t.SourceSize.Width > 0 && t.SourceSize.Height > 0),
                            "Real VS Code discovery and DWM interop: " + actualWindows.Count + " windows", results);
                    }
                }
                else results.Add("SKIP: real VS Code DWM interop requires at least two editor windows");
                TestDwellProgress(results);
                return 0;
            }
            catch (Exception error) { results.Add("FAIL: " + error); return 1; }
            finally
            {
                foreach (var source in sources) source.Dispose();
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controls-test.log"), string.Join(Environment.NewLine, results), Encoding.UTF8);
            }
        }

        private static void TestDwellProgress(List<string> results)
        {
            using (var first = new Form { Text = "Dwell A", ShowInTaskbar = false })
            using (var second = new Form { Text = "Dwell B", ShowInTaskbar = false })
            using (var dock = new PreviewDock(null, true))
            {
                first.Show(); second.Show(); dock.Show();
                Check(dock.HoverDelayMilliseconds == 150, "Hover activation defaults to 150 ms", results);
                dock.SelectHoverDelay(250);
                dock.SelectDisplay(Screen.PrimaryScreen.DeviceName);
                dock.RefreshWindows(new List<CodeWindow> {
                    new CodeWindow { Handle = first.Handle, Title = first.Text },
                    new CodeWindow { Handle = second.Handle, Title = second.Text }
                }, first.Handle);
                var tile = dock.Tiles[0];
                var point = dock.PointToScreen(new Point(tile.Preview.Left + tile.Preview.Width / 2, tile.Preview.Top + tile.Preview.Height / 2));
                var outside = new Point(-30000, -30000);
                int jitter, movement;
                using (var graphics = dock.CreateGraphics())
                { jitter = (int)Math.Round(9 * graphics.DpiX / 96f); movement = (int)Math.Round(20 * graphics.DpiX / 96f); }
                int invalidations = 0;
                Rectangle lastInvalid = Rectangle.Empty;
                dock.Invalidated += delegate(object sender, InvalidateEventArgs e) { invalidations++; lastInvalid = e.InvalidRect; };
                dock.UpdatePointer(point, false, 0);
                dock.UpdatePointer(point, false, 30);
                Check(dock.DwellProgress == 0 && !dock.SelectionEnabled, "Short pointer crossings do not flash a progress line", results);
                dock.UpdatePointer(point, false, 60);
                double previousProgress = dock.DwellProgress, previousOpacity = dock.Opacity;
                dock.UpdatePointer(new Point(point.X + jitter, point.Y), false, 125);
                Check(dock.DwellProgress > previousProgress && dock.Opacity > previousOpacity,
                    "Small pointer jitter does not roll progress or brightness back", results);
                Check(!lastInvalid.IsEmpty && lastInvalid.Top > dock.Height / 2,
                    "Dwell animation invalidates the footer instead of every preview card", results);
                dock.UpdatePointer(point, false, 249);
                Check(!dock.SelectionEnabled, "Progress changes preserve the 250 ms selection threshold", results);
                dock.UpdatePointer(point, false, 250);
                Check(dock.SelectionEnabled && dock.DwellProgress == 0, "Completion enables selection and clears the progress line", results);

                dock.UpdatePointer(outside, false, 1000);
                dock.UpdatePointer(point, false, 2000);
                dock.UpdatePointer(point, false, 2125);
                int beforeCancel = invalidations;
                dock.UpdatePointer(outside, false, 2130);
                Check(dock.DwellProgress == 0 && invalidations > beforeCancel && Math.Abs(dock.Opacity - 0.58) < 0.01,
                    "Leaving midway invalidates and erases the old line immediately", results);

                dock.UpdatePointer(point, false, 3000);
                dock.UpdatePointer(point, false, 3125);
                beforeCancel = invalidations;
                dock.UpdatePointer(point, true, 3130);
                Check(dock.DwellProgress == 0 && invalidations > beforeCancel && !dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) != 0,
                    "Held-button cancellation erases progress and keeps click-through", results);

                dock.UpdatePointer(point, false, 4000);
                dock.UpdatePointer(point, false, 4125);
                var moved = new Point(point.X + movement, point.Y);
                dock.UpdatePointer(moved, false, 4130);
                dock.UpdatePointer(moved, false, 4160);
                Check(dock.DwellProgress == 0 && !dock.SelectionEnabled, "Significant movement resets dwell without a short progress flash", results);
                dock.UpdatePointer(moved, false, 4379);
                Check(!dock.SelectionEnabled, "Movement starts a fresh full dwell interval", results);
                dock.UpdatePointer(moved, false, 4380);
                Check(dock.SelectionEnabled, "Selection recovers after settling at the new position", results);
                long started = 10000;
                foreach (int delay in new[] { 50, 100, 150, 175, 250, 500, 1000, 2000 })
                {
                    dock.SelectHoverDelay(delay);
                    dock.UpdatePointer(point, false, started);
                    dock.UpdatePointer(point, false, started + delay / 2);
                    Check(dock.DwellProgress > 0 && dock.DwellProgress < 1 && dock.Opacity > 0.58 && dock.Opacity < 0.97,
                        "Configured delay keeps progressive brightness and progress: " + delay + " ms", results);
                    dock.UpdatePointer(point, false, started + delay - 1);
                    Check(!dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) != 0,
                        "Configured delay stays click-through until due: " + delay + " ms", results);
                    dock.UpdatePointer(point, false, started + delay);
                    Check(dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) == 0,
                        "Configured delay enables selection when due: " + delay + " ms", results);
                    started += 3000;
                }
                dock.SelectHoverDelay(500);
                dock.UpdatePointer(point, false, started);
                dock.UpdatePointer(point, false, started + 300);
                dock.SelectHoverDelay(100);
                Check(!dock.SelectionEnabled && dock.DwellProgress == 0 && Math.Abs(dock.Opacity - 0.58) < 0.01,
                    "Changing delay clears an in-progress dwell instead of immediately arming", results);
            }
        }

        internal static int RunThemes()
        {
            var results = new List<string>();
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme-test.log");
            try
            {
                using (var dock = new PreviewDock(null, true))
                {
                    dock.ClientSize = new Size(320, 150);
                    foreach (var theme in ThemeCatalog.All)
                    {
                        dock.SelectTheme(theme.Id);
                        using (var bitmap = new Bitmap(320, 150))
                        {
                            dock.DrawToBitmap(bitmap, new Rectangle(0, 0, 320, 150));
                            Check(bitmap.GetPixel(180, 40).ToArgb() == theme.Surface.ToArgb(), "Rendered surface switches to " + theme.Id, results);
                        }
                        Check(Contrast(theme.Text, theme.Card) >= 4.5 && Contrast(theme.Muted, theme.Surface) >= 4.5, "Readable text contrast in " + theme.Id, results);
                        Check(dock.ClientSize == new Size(320, 150) && Math.Abs(dock.Opacity - 0.58) < 0.01, "Theme preserves size and translucency in " + theme.Id, results);
                    }
                    dock.SelectTheme("system");
                    Check(dock.BackColor == ThemeCatalog.Resolve("system").Surface, "Follow-system resolves to current Windows appearance", results);
                    dock.SelectTheme("invalid-theme");
                    Check(dock.BackColor == ThemeCatalog.All[0].Surface, "Unknown theme safely falls back to midnight", results);
                }
                File.WriteAllText(log, string.Join(Environment.NewLine, results), Encoding.UTF8);
                return 0;
            }
            catch (Exception error)
            {
                results.Add("FAIL: " + error);
                File.WriteAllText(log, string.Join(Environment.NewLine, results), Encoding.UTF8);
                return 1;
            }
        }
        private static double Contrast(Color a, Color b)
        {
            double first = Luminance(a), second = Luminance(b);
            return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        }
        private static double Luminance(Color color)
        {
            double[] channels = { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
            for (int i = 0; i < channels.Length; i++) channels[i] = channels[i] <= 0.04045 ? channels[i] / 12.92 : Math.Pow((channels[i] + 0.055) / 1.055, 2.4);
            return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
        }
        internal static int Run(bool avoidanceStateOnly = false)
        {
            var results = new List<string>();
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.log");
            IntPtr originalWindow = Native.GetForegroundWindow();
            Point originalCursor = Cursor.Position;
            try
            {
                if (avoidanceStateOnly)
                {
                    TestAvoidance(results, false);
                    File.WriteAllText(log, string.Join(Environment.NewLine, results), Encoding.UTF8);
                    return 0;
                }
                using (var source = new Form { Text = "Preview integration test", Size = new Size(800, 500), BackColor = Color.DarkSlateBlue, ShowInTaskbar = false, TopMost = true })
                using (var destination = new Form { Size = new Size(400, 250), ShowInTaskbar = false, TopMost = true })
                {
                    source.Show(); destination.Show(); Application.DoEvents();
                    IntPtr thumbnail;
                    Check(Native.DwmRegisterThumbnail(destination.Handle, source.Handle, out thumbnail) == 0, "DWM register", results);
                    try
                    {
                        Native.Size size;
                        Check(Native.DwmQueryThumbnailSourceSize(thumbnail, out size) == 0 && size.Width > 0 && size.Height > 0, "DWM source dimensions", results);
                        var properties = new Native.ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = new Native.Rect(0, 0, 320, 180), Opacity = 255, Visible = true };
                        Check(Native.DwmUpdateThumbnailProperties(thumbnail, ref properties) == 0, "DWM live thumbnail", results);
                        Pump();
                        Color before = Sample(destination.PointToScreen(new Point(150, 100)));
                        source.BackColor = Color.OrangeRed;
                        source.Refresh();
                        Pump();
                        Color after = Sample(destination.PointToScreen(new Point(150, 100)));
                        Check(before.ToArgb() != after.ToArgb() && after.R > 180 && after.B < 100, "Live pixels update while source is covered", results);
                    }
                    finally { Native.DwmUnregisterThumbnail(thumbnail); }
                    // Windows permits foreground switching in response to real user input.
                    // Reproduce a click instead of asserting an unrelated background process can steal focus.
                    bool switched = false;
                    source.TopMost = true;
                    source.BringToFront();
                    source.MouseUp += delegate { switched = Windows.Activate(destination.Handle); };
                    Click(source.PointToScreen(new Point(65, 65)));
                    Check(switched, "Click activation API", results);
                    Check(Native.GetForegroundWindow() == destination.Handle, "Foreground window switched", results);
                    source.WindowState = FormWindowState.Minimized;
                    Pump();
                    Check(Native.IsIconic(source.Handle), "Source minimized", results);
                    destination.MouseUp += delegate { Windows.Activate(source.Handle); };
                    Click(destination.PointToScreen(new Point(65, 65)));
                    Check(!Native.IsIconic(source.Handle) && Native.GetForegroundWindow() == source.Handle, "Click restores and activates minimized window", results);
                }
                using (var dock = new PreviewDock(null, true))
                {
                    dock.Show(); Application.DoEvents(); dock.RefreshWindows();
                    var area = Screen.FromControl(dock).WorkingArea;
                    Check(area.Contains(dock.Bounds), "Dock inside work area above taskbar", results);
                    Check(Math.Abs(dock.Left + dock.Width / 2 - (area.Left + area.Width / 2)) <= 1, "Dock horizontally centered", results);
                    Check(dock.Tiles.All(t => dock.ClientRectangle.Contains(t.Bounds)), "Preview cards inside dock", results);
                    Check(dock.Tiles.All(t => t.Thumbnail != IntPtr.Zero && t.LastError == 0), "Real VS Code thumbnails registered: " + dock.Tiles.Count, results);
                    if (dock.Tiles.Count > 0)
                    {
                        var tile = dock.Tiles[0];
                        var clickPoint = dock.PointToScreen(new Point(tile.Bounds.X + 20, tile.Bounds.Y + 20));
                        dock.UpdatePointer(clickPoint, false, 0);
                        dock.UpdatePointer(clickPoint, false, 250);
                        Check(dock.SelectionEnabled, "Real preview arms after 250 ms hover", results);
                        Click(clickPoint);
                        Check(Native.GetForegroundWindow() == tile.Window.Handle, "Real preview click activates VS Code", results);
                        Check(dock.Visible, "Clicking VS Code preview keeps dock visible", results);
                    }
                }
                TestInteraction(results);
                TestVisibility(results);
                TestMonitors(results);
                TestEdges(results);
                TestDrag(results);
                TestAvoidance(results);
                File.WriteAllText(log, string.Join(Environment.NewLine, results), Encoding.UTF8);
                return 0;
            }
            catch (Exception e) { results.Add("FAIL: " + e); File.WriteAllText(log, string.Join(Environment.NewLine, results), Encoding.UTF8); return 1; }
            finally { Native.SetCursorPos(originalCursor.X, originalCursor.Y); Windows.Activate(originalWindow); }
        }
        private static void TestInteraction(List<string> results)
        {
            var screen = Screen.PrimaryScreen;
            var outside = new Point(screen.WorkingArea.Left + 5, screen.WorkingArea.Top + 5);
            Native.SetCursorPos(outside.X, outside.Y);
            using (var target = new Form { Text = "Hover selection target", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(screen.WorkingArea.Left + 30, screen.WorkingArea.Top + 30, 500, 350), BackColor = Color.DarkSlateBlue, ShowInTaskbar = false })
            using (var below = new Form { Text = "Click-through receiver", StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.None, BackColor = Color.White, ShowInTaskbar = false, TopMost = true })
            using (var dock = new PreviewDock(null, true))
            {
                dock.SelectHoverDelay(250);
                target.Show(); below.Show(); dock.Show(); Application.DoEvents();
                dock.SelectDisplay(screen.DeviceName);
                var sources = new List<CodeWindow> {
                    new CodeWindow { Handle = target.Handle, Title = target.Text },
                    new CodeWindow { Handle = below.Handle, Title = below.Text }
                };
                dock.RefreshWindows(sources, target.Handle);
                below.Bounds = new Rectangle(dock.Left - 20, dock.Top - 20, dock.Width + 40, dock.Height + 40);
                below.BringToFront(); dock.BringToFront();
                Pump();
                var tile = dock.Tiles[0];
                Point point = dock.PointToScreen(new Point(tile.Preview.Left + tile.Preview.Width / 2, tile.Preview.Top + tile.Preview.Height / 2));
                int underlyingClicks = 0;
                below.MouseUp += delegate { underlyingClicks++; };
                using (var graphics = dock.CreateGraphics())
                    Check(tile.Bounds.Width <= (int)(100 * graphics.DpiX / 96f) + 1, "Compact preview width defaults to 100 logical pixels", results);
                Check(!dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x80020) == 0x80020, "Idle window is layered and click-through", results);
                Check(Native.WindowFromPoint(point) == below.Handle, "Native hit-test routes to underlying receiver", results);
                Color idle = Sample(point);
                Click(point);
                Check(underlyingClicks == 1 && !dock.SelectionEnabled, "Immediate click passes to underlying window", results);
                dock.BringToFront();
                dock.UpdatePointer(point, false, 0);
                dock.UpdatePointer(point, false, 60);
                double earlyOpacity = dock.Opacity;
                dock.UpdatePointer(point, false, 125);
                Check(earlyOpacity > 0.58 && dock.Opacity > earlyOpacity && dock.Opacity < 0.97 && !dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) != 0, "Dwell gradually brightens while retaining click-through", results);
                Pump();
                Color halfway = Sample(point);
                dock.UpdatePointer(point, false, 249);
                Check(!dock.SelectionEnabled, "Preview remains click-through before 250 ms", results);
                dock.UpdatePointer(point, false, 250);
                Pump();
                Check(dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) == 0, "250 ms hover enables selection", results);
                Color highlighted = Sample(point);
                Check(idle.ToArgb() != halfway.ToArgb() && halfway.ToArgb() != highlighted.ToArgb(), "Live thumbnail pixels show intermediate fade brightness", results);
                Check(idle.ToArgb() != highlighted.ToArgb() && highlighted.B > highlighted.R + 20, "Layered DWM preview remains visible and brightens on hover", results);
                target.BackColor = Color.OrangeRed; target.Refresh(); Pump();
                Color updated = Sample(point);
                Check(updated.R > 160 && updated.B < 100, "Live preview pixels update on translucent dock", results);
                Click(point);
                Check(Native.GetForegroundWindow() == target.Handle && underlyingClicks == 1, "Armed preview click selects window without clicking underneath", results);
                dock.UpdatePointer(outside, false, 2100);
                Check(!dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) != 0, "Leaving restores transparency and click-through", results);
                dock.RefreshWindows(sources, target.Handle);
                point = dock.PointToScreen(new Point(dock.Tiles[0].Bounds.Left + 20, dock.Tiles[0].Bounds.Top + 20));
                dock.UpdatePointer(point, false, 3000);
                dock.UpdatePointer(point, false, 3125);
                dock.UpdatePointer(outside, false, 3900);
                Check(Math.Abs(dock.Opacity - 0.58) < 0.01, "Cancelled dwell resets partial fade to idle opacity", results);
                dock.UpdatePointer(point, false, 4000);
                dock.UpdatePointer(point, false, 4200);
                Check(!dock.SelectionEnabled, "Leaving and re-entering resets the dwell countdown", results);
                dock.UpdatePointer(point, true, 5500);
                dock.UpdatePointer(point, true, 8500);
                Check(!dock.SelectionEnabled, "Held mouse button prevents arming during drag", results);
                Native.SetCursorPos(outside.X, outside.Y); Pump();
            }
        }
        private static void TestVisibility(List<string> results)
        {
            using (var first = new Form { Text = "Visibility test A", ShowInTaskbar = false })
            using (var second = new Form { Text = "Visibility test B", ShowInTaskbar = false })
            using (var dock = new PreviewDock(null, true))
            {
                first.Show(); second.Show(); dock.Show(); Application.DoEvents();
                var sources = new List<CodeWindow> {
                    new CodeWindow { Handle = first.Handle, Title = first.Text },
                    new CodeWindow { Handle = second.Handle, Title = second.Text }
                };
                dock.RefreshWindows(sources, first.Handle);
                Check(dock.Visible && dock.Tiles.Count == 2, "Two VS Code windows show the dock", results);
                dock.RefreshWindows(sources.Take(1).ToList(), first.Handle); Application.DoEvents();
                Check(!dock.Visible && dock.Tiles.All(t => t.Thumbnail == IntPtr.Zero), "One remaining VS Code window hides dock and releases previews", results);
                dock.RefreshWindows(sources.Take(1).ToList(), first.Handle);
                Check(!dock.Visible, "Hidden dock stays hidden with only one window", results);
                dock.RefreshWindows(sources, second.Handle); Application.DoEvents();
                Check(dock.Visible && dock.Tiles.Count == 2 && dock.Tiles.All(t => t.Thumbnail != IntPtr.Zero), "A second VS Code window automatically restores live previews", results);
                dock.RefreshWindows(new List<CodeWindow>(), IntPtr.Zero); Application.DoEvents();
                Check(!dock.Visible && dock.Tiles.All(t => t.Thumbnail == IntPtr.Zero), "Closing all VS Code windows hides dock", results);
                dock.RefreshWindows(sources, first.Handle); Application.DoEvents();
                Check(dock.Visible, "Dock reappears after reopening multiple windows", results);
                dock.Toggle(); dock.RefreshWindows(sources, first.Handle);
                Check(!dock.Visible, "Explicit manual hide is respected during background scans", results);
            }
        }
        private static void TestMonitors(List<string> results)
        {
            var screens = Screen.AllScreens;
            Native.SetCursorPos(screens[0].WorkingArea.Left + 5, screens[0].WorkingArea.Top + 5);
            if (screens.Length < 2) { results.Add("SKIP: two-monitor integration requires two screens"); return; }
            using (var first = new Form { Text = "Monitor test A", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual })
            using (var second = new Form { Text = "Monitor test B", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual })
            using (var dock = new PreviewDock(null, true))
            {
                first.Bounds = new Rectangle(screens[0].WorkingArea.Left + 50, screens[0].WorkingArea.Top + 50, 600, 400);
                second.Bounds = new Rectangle(screens[1].WorkingArea.Left + 50, screens[1].WorkingArea.Top + 50, 600, 400);
                first.Show(); second.Show(); dock.Show(); Application.DoEvents();
                var sources = new List<CodeWindow> {
                    new CodeWindow { Handle = first.Handle, Title = first.Text },
                    new CodeWindow { Handle = second.Handle, Title = second.Text }
                };
                dock.RefreshWindows(sources, first.Handle); Pump();
                CheckMonitor(dock, screens[0], "Follows active VS Code on screen 1", results);
                dock.RefreshWindows(sources, second.Handle); Pump();
                CheckMonitor(dock, screens[1], "Follows active VS Code on screen 2", results);
                Check(dock.Tiles.Count == 2 && dock.Tiles.All(t => t.Thumbnail != IntPtr.Zero && t.LastError == 0), "Both previews survive a monitor and DPI change", results);
                dock.RefreshWindows(sources, IntPtr.Zero); Pump();
                CheckMonitor(dock, screens[1], "Other applications retain last VS Code screen", results);
                second.Location = new Point(screens[0].WorkingArea.Left + 100, screens[0].WorkingArea.Top + 100);
                dock.RefreshWindows(sources, second.Handle); Pump();
                CheckMonitor(dock, screens[0], "Dragging VS Code across monitors moves dock", results);
                // Exercise the actual menu actions without changing the user's saved preferences.
                dock.SelectDisplay(screens[1].DeviceName);
                dock.RefreshWindows(sources, first.Handle); Pump();
                CheckMonitor(dock, screens[1], "Fixed monitor overrides automatic following", results);
                dock.SelectDisplay("disconnected-monitor");
                dock.RefreshWindows(sources, first.Handle); Pump();
                CheckMonitor(dock, screens[0], "Disconnected fixed monitor falls back safely", results);
            }
        }
        private static void TestEdges(List<string> results)
        {
            var forms = new List<Form>();
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var form = new Form { Text = "Edge test " + i, ShowInTaskbar = false, Size = new Size(400, 300) };
                    forms.Add(form); form.Show();
                }
                using (var dock = new PreviewDock(null, true))
                {
                    dock.Show(); Application.DoEvents();
                    var sources = forms.Select(f => new CodeWindow { Handle = f.Handle, Title = f.Text }).ToList();
                    foreach (var screen in Screen.AllScreens)
                    {
                        dock.SelectDisplay(screen.DeviceName);
                        dock.RefreshWindows(sources, forms[0].Handle);
                        foreach (DockEdge edge in Enum.GetValues(typeof(DockEdge)))
                        {
                            dock.SelectEdge(edge); Application.DoEvents();
                            var area = screen.WorkingArea;
                            int inset;
                            using (var graphics = dock.CreateGraphics()) inset = (int)Math.Round(6 * graphics.DpiX / 96f);
                            bool vertical = edge == DockEdge.Left || edge == DockEdge.Right;
                            bool aligned = edge == DockEdge.Top ? dock.Top == area.Top + inset : edge == DockEdge.Bottom ? dock.Bottom == area.Bottom - inset : edge == DockEdge.Left ? dock.Left == area.Left + inset : dock.Right == area.Right - inset;
                            bool centered = vertical ? Math.Abs(dock.Top + dock.Height / 2 - (area.Top + area.Height / 2)) <= 1 : Math.Abs(dock.Left + dock.Width / 2 - (area.Left + area.Width / 2)) <= 1;
                            bool ordered = true;
                            for (int i = 1; i < dock.Tiles.Count; i++)
                                ordered &= vertical ? dock.Tiles[i - 1].Bounds.Bottom <= dock.Tiles[i].Bounds.Top && dock.Tiles[i - 1].Bounds.X == dock.Tiles[i].Bounds.X : dock.Tiles[i - 1].Bounds.Right <= dock.Tiles[i].Bounds.Left && dock.Tiles[i - 1].Bounds.Y == dock.Tiles[i].Bounds.Y;
                            Check(aligned && centered && area.Contains(dock.Bounds) && ordered && dock.Tiles.All(t => dock.ClientRectangle.Contains(t.Bounds) && t.Thumbnail != IntPtr.Zero), "Edge layout " + edge + " on " + screen.DeviceName, results);
                            dock.ChangePage(1); Application.DoEvents();
                            Check(dock.Tiles.Count == 1 && dock.Tiles[0].Window.Handle == forms[5].Handle && area.Contains(dock.Bounds) && dock.Tiles[0].Thumbnail != IntPtr.Zero, "Edge pagination " + edge + " on " + screen.DeviceName, results);
                        }
                    }
                }
            }
            finally { foreach (var form in forms) form.Dispose(); }
        }
        private static void TestDrag(List<string> results)
        {
            var screen = Screen.PrimaryScreen;
            var area = screen.WorkingArea;
            using (var first = new Form { Text = "Drag target A", ShowInTaskbar = false })
            using (var second = new Form { Text = "Drag target B", ShowInTaskbar = false })
            using (var dock = new PreviewDock(null, true))
            {
                first.Show(); second.Show(); dock.Show(); Application.DoEvents();
                var sources = new List<CodeWindow> {
                    new CodeWindow { Handle = first.Handle, Title = first.Text },
                    new CodeWindow { Handle = second.Handle, Title = second.Text }
                };
                dock.SelectDisplay(screen.DeviceName);
                dock.RefreshWindows(sources, first.Handle);
                Point local = new Point(18, 15);
                Point start = dock.PointToScreen(local);
                dock.UpdatePointer(start, false, 0); dock.UpdatePointer(start, false, 250);
                dock.BeginDrag(start, local);
                Point drop = new Point(area.Right - 15, area.Top + area.Height / 2);
                dock.MoveDrag(drop);
                Point moved = dock.Location;
                dock.RefreshWindows(sources, first.Handle);
                dock.UpdatePointer(new Point(area.Left + 5, area.Top + area.Height / 2), true, 600);
                Check(dock.Capture && dock.SelectionEnabled && dock.Location == moved, "Dragging keeps capture and pauses automatic repositioning", results);
                Check(dock.FinishDrag(drop), "Drag release is consumed instead of activating a preview", results);
                Application.DoEvents();
                Check(!dock.Capture && area.Contains(dock.Bounds) && Math.Abs(area.Right - dock.Right) <= 12 && dock.Tiles[0].Bounds.X == dock.Tiles[1].Bounds.X, "Drop snaps to nearest edge and changes to vertical layout", results);

                dock.SelectEdge(DockEdge.Top);
                start = dock.PointToScreen(local);
                dock.UpdatePointer(start, false, 1000); dock.UpdatePointer(start, false, 1250);
                dock.BeginDrag(start, local); dock.MoveDrag(new Point(start.X + 1, start.Y + 1));
                Check(!dock.FinishDrag(start) && !dock.Capture && dock.SelectionEnabled, "Small pointer movement retains normal click behavior", results);
                dock.BeginDrag(start, local); dock.MoveDrag(drop);
                dock.Capture = false; Application.DoEvents();
                Check(area.Contains(dock.Bounds) && Math.Abs(dock.Top - area.Top) <= 12, "Lost mouse capture cancels drag and restores docking", results);

                var other = Screen.AllScreens.FirstOrDefault(s => s.DeviceName != screen.DeviceName);
                if (other != null)
                {
                    start = dock.PointToScreen(local);
                    dock.UpdatePointer(start, false, 2000); dock.UpdatePointer(start, false, 2250);
                    dock.BeginDrag(start, local);
                    drop = new Point(other.WorkingArea.Left + other.WorkingArea.Width / 2, other.WorkingArea.Bottom - 15);
                    dock.MoveDrag(drop); dock.FinishDrag(drop); Application.DoEvents();
                    dock.RefreshWindows(sources, first.Handle);
                    Check(Screen.FromControl(dock).DeviceName == other.DeviceName && other.WorkingArea.Contains(dock.Bounds) && Math.Abs(other.WorkingArea.Bottom - dock.Bottom) <= 12, "Cross-monitor drag pins destination instead of jumping back", results);
                }

                // Exercise the real MouseDown/Move/Up route, including WinForms capture ordering.
                dock.SelectDisplay(screen.DeviceName); dock.SelectEdge(DockEdge.Top);
                Application.DoEvents();
                start = dock.PointToScreen(local);
                dock.UpdatePointer(start, false, 3000); dock.UpdatePointer(start, false, 3250);
                Native.SetCursorPos(start.X, start.Y);
                Native.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Pump(60);
                Check(dock.Capture, "Native mouse down captures dock for dragging", results);
                drop = new Point(area.Left + area.Width / 2, area.Bottom - 15);
                Native.SetCursorPos(drop.X, drop.Y); Pump(80);
                Native.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); Pump();
                Check(area.Contains(dock.Bounds) && Math.Abs(area.Bottom - dock.Bottom) <= 12 && !dock.Capture, "Native mouse drag moves and docks without a selection click (bounds=" + dock.Bounds + ", cursor=" + Cursor.Position + ")", results);
            }
        }
        private static void TestAvoidance(List<string> results, bool realKeyboardInput = true)
        {
            var screen = Screen.PrimaryScreen;
            var area = screen.WorkingArea;
            using (var editor = new Form { Text = "Typing avoidance editor", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = area, KeyPreview = true })
            using (var other = new Form { Text = "Typing avoidance other", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(area.Right - 260, area.Top + 100, 220, 120) })
            using (var dock = new PreviewDock(null, true))
            {
                editor.Show(); other.Show(); dock.Show(); Application.DoEvents();
                var sources = new List<CodeWindow> {
                    new CodeWindow { Handle = editor.Handle, Title = editor.Text },
                    new CodeWindow { Handle = other.Handle, Title = other.Text }
                };
                dock.SelectDisplay(screen.DeviceName); dock.SelectEdge(DockEdge.Bottom);
                dock.RefreshWindows(sources, editor.Handle); Application.DoEvents();
                dock.AdvanceTypingAvoidance(0, editor.Handle);
                Check(Math.Abs(area.Bottom - dock.Bottom) <= 12, "Bottom docking stays selected while VS Code is in foreground", results);
                dock.AdvanceTypingAvoidance(1, IntPtr.Zero);
                Check(Math.Abs(area.Bottom - dock.Bottom) <= 12, "Switching applications preserves configured bottom docking", results);
                dock.RecordKeyboardActivity(editor.Handle, 100);
                dock.AdvanceTypingAvoidance(100, editor.Handle);
                Check(dock.TypingAvoidanceActive && dock.Opacity == 0 && !dock.SelectionEnabled && (Native.ExtendedStyle(dock.Handle) & 0x20) != 0, "Typing makes dock fully transparent and click-through", results);
                Check(Math.Abs(area.Bottom - dock.Bottom) <= 12, "Typing avoidance keeps dock at user's chosen bottom edge", results);
                Point point = dock.PointToScreen(new Point(20, 45));
                dock.UpdatePointer(point, false, 200); dock.UpdatePointer(point, false, 600);
                Check(!dock.SelectionEnabled && dock.Opacity == 0, "Stationary mouse cannot activate a preview while typing", results);
                dock.RecordKeyboardActivity(editor.Handle, 1000);
                dock.AdvanceTypingAvoidance(2199, editor.Handle);
                Check(dock.TypingAvoidanceActive, "Repeated typing extends avoidance without flicker", results);
                dock.AdvanceTypingAvoidance(2200, editor.Handle);
                dock.AdvanceTypingAvoidance(2290, editor.Handle);
                Check(!dock.TypingAvoidanceActive && dock.Opacity > 0 && dock.Opacity < 0.58, "After 1.2 seconds idle dock gradually recovers", results);
                dock.AdvanceTypingAvoidance(2380, editor.Handle);
                Check(Math.Abs(dock.Opacity - 0.58) < 0.01 && Math.Abs(area.Bottom - dock.Bottom) <= 12, "After typing dock recovers in place at bottom edge", results);
                dock.RecordKeyboardActivity(editor.Handle, 3000); dock.AdvanceTypingAvoidance(3000, editor.Handle);
                dock.Toggle();
                Check(dock.Visible && !dock.TypingAvoidanceActive && dock.Opacity > 0, "Tray or hotkey can immediately recall an avoided dock", results);
                Check(dock.StartTypingMonitor(), "Native keyboard activity monitor installs successfully", results);
                if (!realKeyboardInput)
                {
                    results.Add("SKIP: real keyboard input and pixel checks require an unlocked desktop");
                    return;
                }

                dock.RefreshWindows(sources, editor.Handle);
                int received = 0;
                editor.KeyDown += delegate { received++; };
                Click(editor.PointToScreen(new Point(30, 150)));
                Check(Native.GetForegroundWindow() == editor.Handle, "Keyboard integration test editor receives focus", results);
                Native.keybd_event(0x87, 0, 0, UIntPtr.Zero); // F24: no text or terminal command is entered.
                Native.keybd_event(0x87, 0, 2, UIntPtr.Zero);
                Pump(60);
                long now = (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                dock.AdvanceTypingAvoidance(now, editor.Handle);
                Check(received > 0 && dock.TypingAvoidanceActive, "Real key event triggers avoidance and still reaches focused application", results);
            }
        }
        private static void CheckMonitor(PreviewDock dock, Screen screen, string message, List<string> results)
        {
            var area = screen.WorkingArea;
            Check(Screen.FromControl(dock).DeviceName == screen.DeviceName && area.Contains(dock.Bounds) &&
                Math.Abs(dock.Left + dock.Width / 2 - (area.Left + area.Width / 2)) <= 1 &&
                dock.Tiles.All(t => dock.ClientRectangle.Contains(t.Bounds)), message, results);
        }
        private static void Click(Point point)
        {
            Application.DoEvents();
            Native.SetCursorPos(point.X, point.Y);
            Native.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Native.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Pump();
        }
        private static void Pump(int milliseconds = 300)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            Native.DwmFlush();
        }
        private static Color Sample(Point point)
        {
            using (var bitmap = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(point, Point.Empty, new Size(1, 1));
                return bitmap.GetPixel(0, 0);
            }
        }
        private static void Check(bool condition, string message, List<string> results)
        {
            if (!condition) throw new InvalidOperationException(message);
            results.Add("PASS: " + message);
        }
    }
}
