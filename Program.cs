using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeekJet
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--self-test")) { Environment.Exit(SelfTest.Run()); return; }
            if (args.Contains("--self-test-avoidance")) { Environment.Exit(SelfTest.Run(true)); return; }
            if (args.Contains("--self-test-themes")) { Environment.Exit(SelfTest.RunThemes()); return; }
            if (args.Contains("--render-themes")) { Environment.Exit(ThemeGallery.Run(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "docs", "themes")))); return; }
            bool created;
            using (var mutex = new Mutex(true, "Local\\PeekJet.Singleton", out created))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PeekJet.Show"))
            {
                if (!created) { wake.Set(); return; }
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception); };
                try { Application.Run(new PreviewDock(wake)); }
                catch (Exception e) { Log(e); MessageBox.Show("启动失败：" + e.Message, "VS Code 预览"); }
            }
        }
        internal static void Log(Exception error)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeekJet");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now.ToString("s") + " " + error + Environment.NewLine);
            }
            catch { }
        }
    }

    internal sealed class CodeWindow
    {
        internal IntPtr Handle;
        internal string Title;
        internal bool Minimized;
        internal string ProjectName { get { return WindowTitles.ProjectName(Title); } }
    }

    internal static class WindowTitles
    {
        internal static string ProjectName(string title)
        {
            string name = (title ?? "").Trim();
            if (name.Length == 0 || name == "Visual Studio Code" || name == "Visual Studio Code - Insiders") return "未打开项目";
            string[] suffixes = { " - Visual Studio Code - Insiders", " - Visual Studio Code Insiders", " - Visual Studio Code" };
            foreach (string suffix in suffixes)
            {
                if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                name = name.Substring(0, name.Length - suffix.Length).Trim();
                break;
            }
            // Standard VS Code title: active editor - root/workspace name - application.
            // Remove the editor prefix while preserving spaces and hyphens within the project name.
            int separator = name.IndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0) name = name.Substring(separator + 3).Trim();
            int remote = name.LastIndexOf(" [", StringComparison.Ordinal);
            if (remote >= 0 && name.EndsWith("]", StringComparison.Ordinal))
            {
                string context = name.Substring(remote + 2);
                string[] remoteKinds = { "SSH:", "WSL:", "Dev Container:", "Container:", "Codespaces:", "Tunnel:", "Remote:" };
                if (remoteKinds.Any(kind => context.StartsWith(kind, StringComparison.OrdinalIgnoreCase))) name = name.Substring(0, remote).Trim();
            }
            name = name.TrimStart('●', '•', ' ');
            if (name.Length == 0 || name == "Visual Studio Code" || name == "Visual Studio Code - Insiders") return "未打开项目";
            return name;
        }
    }

    internal static class Windows
    {
        internal static List<CodeWindow> Find()
        {
            var found = new List<CodeWindow>();
            // Cache process names for this enumeration, including processes with multiple top-level windows.
            var names = new Dictionary<uint, bool>();
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, 4) != IntPtr.Zero) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                var title = new StringBuilder(2048);
                Native.GetWindowText(hwnd, title, title.Capacity);
                if (title.Length == 0) return true;
                var className = new StringBuilder(256);
                Native.GetClassName(hwnd, className, className.Capacity);
                if (!className.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)) return true;
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                bool isCode;
                if (!names.TryGetValue(pid, out isCode))
                {
                    try
                    {
                        using (var process = Process.GetProcessById((int)pid))
                            isCode = process.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
                                process.ProcessName.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { isCode = false; }
                    names[pid] = isCode;
                }
                if (isCode) found.Add(new CodeWindow { Handle = hwnd, Title = title.ToString(), Minimized = Native.IsIconic(hwnd) });
                return true;
            }, IntPtr.Zero);
            return found;
        }
        internal static bool Activate(IntPtr hwnd)
        {
            if (!Native.IsWindow(hwnd)) return false;
            if (Native.IsIconic(hwnd)) Native.ShowWindowAsync(hwnd, 9);
            return Native.SetForegroundWindow(hwnd);
        }
    }

    internal sealed class PreviewTile : IDisposable
    {
        internal CodeWindow Window;
        internal IntPtr Thumbnail;
        internal Rectangle Bounds, Preview;
        internal int LastError;
        internal Image ExampleImage;
        public void Dispose()
        {
            if (Thumbnail != IntPtr.Zero) Native.DwmUnregisterThumbnail(Thumbnail);
            Thumbnail = IntPtr.Zero;
        }
    }

    internal enum DockEdge { Top, Bottom, Left, Right }

    internal sealed class PreviewDock : Form
    {
        private readonly EventWaitHandle wake;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer pointerTimer = new System.Windows.Forms.Timer();
        private long hoverStarted = -1;
        private long hoverElapsed;
        private byte previewOpacity = 180;
        private bool avoidWhileTyping = true;
        private long lastTypingAt = -1000000, recoveryStarted = -1;
        private IntPtr typingWindow, keyboardHook;
        private Native.KeyboardHookProc keyboardCallback;
        private double typingVisibility = 1;
        internal bool TypingAvoidanceActive { get; private set; }
        private Point hoverAnchor;
        private bool dragPending, dragging;
        private Point dragStartCursor, dragOffset;
        internal bool SelectionEnabled { get; private set; }
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        internal readonly List<PreviewTile> Tiles = new List<PreviewTile>();
        private List<CodeWindow> windows = new List<CodeWindow>();
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 8f);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 8f);
        private readonly Font brandFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly Font iconFont = new Font("Segoe MDL2 Assets", 9f);
        private readonly Font menuFont = new Font("Microsoft YaHei UI", 9f);
        private string themeId = "midnight";
        private ThemePalette palette;
        private int hoveredControl = -1;
        private IntPtr active;
        private const int HoverDelayMilliseconds = 250;
        private int hovered = -1, page, capacity = 4, preferredWidth = 100;
        private DockEdge dockEdge = DockEdge.Top;
        private bool manuallyHidden, hotkeyRegistered, disposing, arranging, arrangeAgain;
        private bool followActive = true;
        private string screenDevice = "";
        private string automaticScreenDevice = "";
        private string layoutScreenDevice = "";
        private Rectangle layoutWorkArea;
        private IntPtr followedWindow;
        private float scale = 1f;
        private Rectangle previousButton, nextButton, settingsButton, hideButton;
        private ToolTip tooltip = new ToolTip();
        private string status = "";
        private DateTime statusUntil;
        private Icon appIcon;
        private readonly bool testing;

        internal PreviewDock(EventWaitHandle wakeEvent, bool testMode = false)
        {
            wake = wakeEvent;
            testing = testMode;
            Text = "PeekJet · 闪览";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 25, 34);
            Opacity = 0.58;
            DoubleBuffered = true;
            if (!testing) ReadSettings();
            ApplyTheme();
            appIcon = CreateIcon();
            Icon = appIcon;
            tray = new NotifyIcon { Icon = appIcon, Text = "PeekJet · Ctrl+Alt+V", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Toggle(); };
            menu.Opening += delegate { ClearTypingAvoidance(); SetSelectionEnabled(true); BuildMenu(); };
            MouseMove += OnPointerMove;
            MouseDown += OnPointerDown;
            MouseCaptureChanged += delegate
            {
                // WinForms may release capture before raising MouseUp. Let that click/drop
                // finish first; only cancel if capture was actually stolen by another window.
                if (!Capture && dragPending && !disposing)
                    BeginInvoke(new Action(delegate { if (!disposing && dragPending && !Capture) CancelDrag(); }));
            };
            MouseLeave += delegate { hovered = hoveredControl = -1; tooltip.Hide(this); Invalidate(); };
            MouseUp += OnPointerUp;
            MouseWheel += delegate(object sender, MouseEventArgs e) { if (SelectionEnabled) ChangePage(e.Delta < 0 ? 1 : -1); };
            timer.Interval = 650;
            timer.Tick += delegate { if (wake != null && wake.WaitOne(0)) ShowDock(); RefreshWindows(); };
            // Transparent windows receive no mouse events; inspect the global cursor independently.
            pointerTimer.Interval = 16;
            pointerTimer.Tick += delegate { PollPointer(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += PreferencesChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x80000; if (!SelectionEnabled) cp.ExStyle |= 0x20; return cp; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!testing) hotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.V);
            int corners = 2;
            Native.DwmSetWindowAttribute(Handle, 33, ref corners, 4);
            int dark = palette == null || !palette.Light ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            Native.SetClickThrough(Handle, !SelectionEnabled);
            if (!testing) StartTypingMonitor();
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshWindows();
            if (!testing) timer.Start();
            if (!testing) pointerTimer.Start();
            if (!testing && !hotkeyRegistered) tray.ShowBalloonTip(4000, "快捷键已被占用", "仍可点击托盘图标显示或隐藏预览。", ToolTipIcon.Info);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE; retain click delivery.
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { Toggle(); return; }
            base.WndProc(ref m);
            if (m.Msg == 0x02E0 && IsHandleCreated && !disposing) Arrange();
        }

        private void ReadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\PeekJet") ?? Registry.CurrentUser.OpenSubKey("Software\\VSCodePreview"))
                {
                    if (key == null) return;
                    preferredWidth = Math.Max(80, Math.Min(420, Convert.ToInt32(key.GetValue("PreviewWidth", 100))));
                    // Adopt the requested smaller appearance once, then preserve subsequent size choices.
                    if (Convert.ToInt32(key.GetValue("AppearanceVersion", 0)) < 4) preferredWidth = Math.Min(100, preferredWidth);
                    DockEdge savedEdge;
                    if (Enum.TryParse(Convert.ToString(key.GetValue("DockEdge", "Top")), out savedEdge) && Enum.IsDefined(typeof(DockEdge), savedEdge)) dockEdge = savedEdge;
                    screenDevice = Convert.ToString(key.GetValue("Screen", ""));
                    followActive = Convert.ToInt32(key.GetValue("FollowActive", 1)) != 0;
                    avoidWhileTyping = Convert.ToInt32(key.GetValue("AvoidWhileTyping", 1)) != 0;
                    string savedTheme = Convert.ToString(key.GetValue("Theme", "midnight"));
                    if (ThemeCatalog.Valid(savedTheme)) themeId = savedTheme;
                }
            }
            catch (Exception e) { Program.Log(e); }
        }
        private void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey("Software\\PeekJet"))
                {
                    key.SetValue("PreviewWidth", preferredWidth);
                    key.DeleteValue("HideAfterSwitch", false);
                    key.SetValue("Screen", screenDevice);
                    key.SetValue("FollowActive", followActive ? 1 : 0);
                    key.SetValue("AppearanceVersion", 4);
                    key.SetValue("DockEdge", dockEdge.ToString());
                    key.SetValue("AvoidWhileTyping", avoidWhileTyping ? 1 : 0);
                    key.SetValue("Theme", themeId);
                }
            }
            catch (Exception e) { Program.Log(e); }
        }
        private Screen TargetScreen()
        {
            var screens = Screen.AllScreens;
            if (!followActive)
            {
                var pinned = screens.FirstOrDefault(s => s.DeviceName == screenDevice);
                if (pinned != null) return pinned;
            }
            return screens.FirstOrDefault(s => s.DeviceName == automaticScreenDevice) ?? Screen.FromPoint(Cursor.Position);
        }
        internal void SelectDisplay(string fixedDevice)
        {
            followActive = string.IsNullOrEmpty(fixedDevice);
            if (followActive)
            {
                active = Native.GetForegroundWindow();
                UpdateFollowingScreen(Windows.Find());
            }
            else screenDevice = fixedDevice;
            if (!testing) SaveSettings();
            Arrange();
        }
        internal void SelectTheme(string id)
        {
            themeId = ThemeCatalog.Valid(id) ? id : "midnight";
            ApplyTheme();
            if (!testing) SaveSettings();
        }
        internal void SelectPreviewSize(int width)
        {
            preferredWidth = Math.Max(80, Math.Min(420, width));
            if (!testing) SaveSettings();
            Arrange();
        }
        private void ApplyTheme()
        {
            palette = ThemeCatalog.Resolve(themeId);
            BackColor = palette.Surface;
            menu.Font = menuFont;
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.Text;
            menu.Renderer = new ThemeMenuRenderer(palette);
            menu.ShowImageMargin = true;
            StyleMenuItems(menu.Items);
            tooltip.BackColor = palette.Card;
            tooltip.ForeColor = palette.Text;
            if (IsHandleCreated)
            {
                int dark = palette.Light ? 0 : 1;
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            Invalidate();
        }
        private void StyleMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = palette.Text;
                var parent = item as ToolStripMenuItem;
                if (parent != null)
                {
                    parent.DropDown.BackColor = palette.Surface;
                    parent.DropDown.ForeColor = palette.Text;
                    parent.DropDown.Renderer = menu.Renderer;
                    StyleMenuItems(parent.DropDownItems);
                }
            }
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using (var path = ThemeDrawing.Rounded(ClientRectangle, (int)Math.Round(12 * Math.Max(1, scale))))
            {
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }
        internal void SelectEdge(DockEdge edge)
        {
            dockEdge = edge;
            page = 0;
            if (!testing) SaveSettings();
            Arrange();
        }
        private void UpdateFollowingScreen(List<CodeWindow> found)
        {
            // Keep the last VS Code screen when another application or the tray is active.
            // Remember the window handle so dragging that window to another screen also follows it.
            var foregroundCode = found.FirstOrDefault(w => w.Handle == active && !w.Minimized);
            if (foregroundCode != null) followedWindow = foregroundCode.Handle;
            else if (!found.Any(w => w.Handle == followedWindow && !w.Minimized))
            {
                var candidate = found.FirstOrDefault(w => !w.Minimized) ?? found.FirstOrDefault();
                followedWindow = candidate == null ? IntPtr.Zero : candidate.Handle;
            }
            if (followedWindow != IntPtr.Zero)
                automaticScreenDevice = Screen.FromHandle(followedWindow).DeviceName;
            else if (automaticScreenDevice.Length == 0)
                automaticScreenDevice = Screen.FromPoint(Cursor.Position).DeviceName;
        }
        private int S(int value) { return (int)Math.Round(value * scale); }

        internal void RefreshWindows()
        {
            RefreshWindows(Windows.Find(), Native.GetForegroundWindow());
        }
        internal void RefreshWindows(List<CodeWindow> found, IntPtr foreground)
        {
            if (dragPending) return;
            // Keep cards stable when activation changes Windows' Z order.
            var ordered = windows.Where(w => found.Any(f => f.Handle == w.Handle)).Select(w => found.First(f => f.Handle == w.Handle)).ToList();
            ordered.AddRange(found.Where(f => !ordered.Any(w => w.Handle == f.Handle)));
            bool changed = !ordered.Select(w => w.Handle).SequenceEqual(windows.Select(w => w.Handle));
            windows = ordered;
            active = foreground;
            // Do not move the panel while its settings menu is being used.
            if (!menu.Visible && !SelectionEnabled) UpdateFollowingScreen(found);
            if (windows.Count < 2)
            {
                if (Visible) HideDock(false);
                return;
            }
            if (manuallyHidden) return;
            bool showing = !Visible;
            if (showing) Show();
            var target = TargetScreen();
            if (showing || changed || Tiles.Count == 0 || target.DeviceName != layoutScreenDevice || target.WorkingArea != layoutWorkArea) Arrange();
            else
            {
                foreach (var tile in Tiles)
                {
                    tile.Window = windows.First(w => w.Handle == tile.Window.Handle);
                    UpdateThumbnail(tile);
                }
                // Re-evaluate work area to follow taskbar or display scaling changes.
                PositionDock();
                Invalidate();
            }
        }
        private void Arrange()
        {
            if (disposing || dragPending || windows.Count < 2 || manuallyHidden) return;
            if (arranging) { arrangeAgain = true; return; }
            arranging = true;
            try { ArrangeCore(); }
            finally
            {
                arranging = false;
                if (arrangeAgain)
                {
                    arrangeAgain = false;
                    QueueArrange();
                }
            }
        }
        private void ArrangeCore()
        {
            ResetInteraction();
            // Move before measuring: the destination monitor can have a different DPI.
            // WM_DPICHANGED may arrive synchronously; Arrange queues a second pass safely.
            var target = TargetScreen();
            var area = target.WorkingArea;
            if (Screen.FromControl(this).DeviceName != target.DeviceName)
            {
                tooltip.Hide(this);
                hovered = -1;
                Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, area.Height - Height - 10));
            }
            foreach (var tile in Tiles) tile.Dispose();
            Tiles.Clear();
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            layoutScreenDevice = target.DeviceName;
            layoutWorkArea = area;
            bool vertical = dockEdge == DockEdge.Left || dockEdge == DockEdge.Right;
            int margin = S(8), gap = S(6), width = S(preferredWidth);
            int headerHeight = S(30), footerHeight = S(22);
            int usable = Math.Max(S(160), area.Width - S(32));
            width = Math.Min(width, usable - 2 * margin);
            width = Math.Min(width, Math.Max(S(60), (int)((area.Height - S(20) - headerHeight - footerHeight - S(24)) * 16.0 / 9.0)));
            int previewHeight = (int)(width * 9.0 / 16.0);
            int tileHeight = previewHeight + S(24);
            capacity = vertical ? Math.Max(1, Math.Min(5, (area.Height - S(20) - headerHeight - footerHeight + gap) / (tileHeight + gap))) :
                Math.Max(1, Math.Min(5, (usable - margin * 2 + gap) / (width + gap)));
            int maxPage = Math.Max(0, (windows.Count - 1) / capacity);
            page = Math.Min(page, maxPage);
            int count = Math.Min(capacity, Math.Max(0, windows.Count - page * capacity));
            ClientSize = vertical ? new Size(margin * 2 + width, headerHeight + count * tileHeight + Math.Max(0, count - 1) * gap + footerHeight) :
                new Size(margin * 2 + count * width + Math.Max(0, count - 1) * gap, headerHeight + tileHeight + footerHeight);
            bool hasPages = windows.Count > capacity;
            previousButton = !hasPages ? Rectangle.Empty : vertical ? new Rectangle(S(5), Height - footerHeight, S(24), S(22)) : new Rectangle(ClientSize.Width - S(116), S(1), S(24), S(27));
            nextButton = !hasPages ? Rectangle.Empty : vertical ? new Rectangle(Width - S(29), Height - footerHeight, S(24), S(22)) : new Rectangle(ClientSize.Width - S(88), S(1), S(24), S(27));
            settingsButton = new Rectangle(ClientSize.Width - S(60), S(1), S(24), S(27));
            hideButton = new Rectangle(ClientSize.Width - S(32), S(1), S(24), S(27));
            for (int i = 0; i < count; i++)
            {
                var tile = new PreviewTile { Window = windows[page * capacity + i] };
                tile.Bounds = new Rectangle(vertical ? margin : margin + i * (width + gap), vertical ? headerHeight + i * (tileHeight + gap) : headerHeight, width, tileHeight);
                tile.Preview = new Rectangle(tile.Bounds.X + S(3), tile.Bounds.Y + S(3), width - S(6), previewHeight - S(6));
                Tiles.Add(tile);
                UpdateThumbnail(tile);
            }
            PositionDock();
            Invalidate();
        }
        private void PositionDock()
        {
            if (dragPending) return;
            var area = TargetScreen().WorkingArea;
            int inset = S(6);
            int centerX = area.Left + (area.Width - Width) / 2, centerY = area.Top + (area.Height - Height) / 2;
            Point point;
            switch (dockEdge)
            {
                case DockEdge.Bottom: point = new Point(centerX, area.Bottom - Height - inset); break;
                case DockEdge.Left: point = new Point(area.Left + inset, centerY); break;
                case DockEdge.Right: point = new Point(area.Right - Width - inset, centerY); break;
                default: point = new Point(centerX, area.Top + inset); break;
            }
            if (Location != point) Location = point;
        }
        private void UpdateThumbnail(PreviewTile tile)
        {
            if (tile.Thumbnail == IntPtr.Zero)
            {
                IntPtr thumbnail;
                tile.LastError = Native.DwmRegisterThumbnail(Handle, tile.Window.Handle, out thumbnail);
                tile.Thumbnail = tile.LastError == 0 ? thumbnail : IntPtr.Zero;
                if (tile.Thumbnail == IntPtr.Zero) return;
            }
            Native.Size source;
            Rectangle destination = tile.Preview;
            if (Native.DwmQueryThumbnailSourceSize(tile.Thumbnail, out source) == 0 && source.Width > 0 && source.Height > 0)
            {
                double ratio = Math.Min((double)destination.Width / source.Width, (double)destination.Height / source.Height);
                int w = Math.Max(1, (int)(source.Width * ratio)), h = Math.Max(1, (int)(source.Height * ratio));
                destination = new Rectangle(destination.X + (destination.Width - w) / 2, destination.Y + (destination.Height - h) / 2, w, h);
            }
            var properties = new Native.ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = new Native.Rect(destination.X, destination.Y, destination.Width, destination.Height), Opacity = previewOpacity, Visible = Visible, ClientOnly = false };
            tile.LastError = Native.DwmUpdateThumbnailProperties(tile.Thumbnail, ref properties);
            if (tile.LastError != 0) tile.Dispose();
        }
        internal void ChangePage(int direction)
        {
            int next = Math.Max(0, Math.Min(Math.Max(0, (windows.Count - 1) / capacity), page + direction));
            if (next != page) { page = next; hovered = -1; tooltip.Hide(this); Arrange(); }
        }
        internal void Toggle()
        {
            if (TypingAvoidanceActive || typingVisibility < 1) { ShowDock(); return; }
            if (Visible) HideDock(); else ShowDock();
        }
        private void HideDock(bool manual = true)
        {
            if (dragPending) CancelDrag();
            if (manual) manuallyHidden = true;
            ResetInteraction();
            Hide();
            foreach (var tile in Tiles) tile.Dispose();
        }
        private void ShowDock() { ClearTypingAvoidance(); manuallyHidden = false; RefreshWindows(); }

        private static long MonotonicMilliseconds() { return (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency); }
        internal bool StartTypingMonitor()
        {
            if (keyboardHook != IntPtr.Zero) return true;
            keyboardCallback = delegate(int code, IntPtr message, IntPtr data)
            {
                // Only retain the event time and target window. Never inspect key codes or text,
                // and always forward input unchanged. All rendering happens on the timer.
                if (code >= 0 && (message.ToInt64() == 0x100 || message.ToInt64() == 0x104))
                    RecordKeyboardActivity(Native.GetForegroundWindow(), MonotonicMilliseconds());
                return Native.CallNextHookEx(keyboardHook, code, message, data);
            };
            keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
            if (keyboardHook == IntPtr.Zero) Program.Log(new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Keyboard activity monitor could not start"));
            return keyboardHook != IntPtr.Zero;
        }
        internal void RecordKeyboardActivity(IntPtr foreground, long milliseconds)
        {
            if (!avoidWhileTyping || !Visible || menu.Visible || !windows.Any(w => w.Handle == foreground)) return;
            typingWindow = foreground;
            lastTypingAt = milliseconds;
        }
        private bool OverlapsWindow(IntPtr window)
        {
            Native.Rect rect;
            return Native.GetWindowRect(window, out rect) && Bounds.IntersectsWith(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
        }
        internal void AdvanceTypingAvoidance(long milliseconds, IntPtr foreground)
        {
            long elapsed = milliseconds - lastTypingAt;
            bool avoiding = avoidWhileTyping && Visible && !menu.Visible && !dragPending && elapsed >= 0 && elapsed < 1200 && foreground == typingWindow &&
                windows.Any(w => w.Handle == foreground) && OverlapsWindow(foreground);
            if (avoiding != TypingAvoidanceActive)
            {
                TypingAvoidanceActive = avoiding;
                if (avoiding) { typingVisibility = 0; recoveryStarted = -1; }
                else recoveryStarted = milliseconds;
                ResetInteraction();
            }
            if (!TypingAvoidanceActive && recoveryStarted >= 0)
            {
                double progress = Math.Max(0, Math.Min(1, (milliseconds - recoveryStarted) / 180.0));
                typingVisibility = progress * progress * (3 - 2 * progress);
                if (progress >= 1) recoveryStarted = -1;
                ApplyHoverBrightness(0);
            }
        }
        internal void ClearTypingAvoidance()
        {
            lastTypingAt = -1000000;
            typingWindow = IntPtr.Zero;
            TypingAvoidanceActive = false;
            recoveryStarted = -1;
            typingVisibility = 1;
            ResetInteraction();
        }

        internal void PollPointer()
        {
            long milliseconds = MonotonicMilliseconds();
            AdvanceTypingAvoidance(milliseconds, Native.GetForegroundWindow());
            if (dragPending)
            {
                if ((Native.GetAsyncKeyState(1) & 0x8000) == 0) FinishDrag(Cursor.Position);
                else MoveDrag(Cursor.Position);
                return;
            }
            bool buttonsDown = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0 || (Native.GetAsyncKeyState(4) & 0x8000) != 0;
            UpdatePointer(Cursor.Position, buttonsDown, milliseconds);
        }
        internal void UpdatePointer(Point point, bool buttonsDown, long milliseconds)
        {
            if (disposing) return;
            if (dragPending) return;
            if (TypingAvoidanceActive || typingVisibility < 1) { ResetDwell(); return; }
            if (menu.Visible) { ResetDwell(); return; }
            if (!Visible || !Bounds.Contains(point)) { ResetInteraction(); return; }
            if (SelectionEnabled) return;
            // Do not capture an in-progress click or drag that started in the window below us.
            if (buttonsDown)
            {
                ResetDwell();
                ApplyHoverBrightness(0);
                return;
            }
            if (hoverStarted < 0 || Math.Abs(point.X - hoverAnchor.X) > S(8) || Math.Abs(point.Y - hoverAnchor.Y) > S(8))
            {
                hoverAnchor = point;
                hoverStarted = milliseconds;
                hoverElapsed = 0;
                ApplyHoverBrightness(0);
            }
            else
            {
                hoverElapsed = Math.Max(0, milliseconds - hoverStarted);
                ApplyHoverBrightness((double)hoverElapsed / HoverDelayMilliseconds);
                if (hoverElapsed >= HoverDelayMilliseconds)
                {
                    SetSelectionEnabled(true);
                    var local = PointToClient(point);
                    OnPointerMove(this, new MouseEventArgs(MouseButtons.None, 0, local.X, local.Y, 0));
                }
            }
            Invalidate();
        }
        private void ResetDwell() { hoverStarted = -1; hoverElapsed = 0; }
        private void ApplyHoverBrightness(double progress)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            double eased = progress * progress * (3 - 2 * progress);
            double opacity = (0.58 + (0.97 - 0.58) * eased) * typingVisibility;
            if (Math.Abs(Opacity - opacity) > 0.0001) Opacity = opacity;
            byte thumbnailOpacity = (byte)Math.Round((180 + (255 - 180) * eased) * typingVisibility);
            if (previewOpacity == thumbnailOpacity) return;
            previewOpacity = thumbnailOpacity;
            foreach (var tile in Tiles) UpdateThumbnail(tile);
        }
        private void SetSelectionEnabled(bool enabled)
        {
            // A cancelled dwell must also reset a partially completed fade.
            ApplyHoverBrightness(enabled ? 1 : 0);
            if (SelectionEnabled == enabled) return;
            SelectionEnabled = enabled;
            ResetDwell();
            if (IsHandleCreated) Native.SetClickThrough(Handle, !enabled);
            Invalidate();
        }
        private void ResetInteraction()
        {
            ResetDwell();
            hoveredControl = -1;
            if (hovered != -1) { hovered = -1; tooltip.Hide(this); Invalidate(); }
            Cursor = Cursors.Default;
            SetSelectionEnabled(false);
        }

        private bool IsControlPoint(Point point)
        {
            return previousButton.Contains(point) || nextButton.Contains(point) || settingsButton.Contains(point) || hideButton.Contains(point);
        }
        private void OnPointerDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SelectionEnabled && !IsControlPoint(e.Location)) BeginDrag(Cursor.Position, e.Location);
        }
        internal void BeginDrag(Point screenPoint, Point localPoint)
        {
            if (!SelectionEnabled || !Visible || IsControlPoint(localPoint)) return;
            dragPending = true;
            dragging = false;
            dragStartCursor = screenPoint;
            dragOffset = localPoint;
            tooltip.Hide(this);
            Capture = true;
        }
        internal void MoveDrag(Point screenPoint)
        {
            if (!dragPending) return;
            Size threshold = SystemInformation.DragSize;
            if (!dragging && Math.Abs(screenPoint.X - dragStartCursor.X) < threshold.Width / 2 && Math.Abs(screenPoint.Y - dragStartCursor.Y) < threshold.Height / 2) return;
            dragging = true;
            Cursor = Cursors.SizeAll;
            Location = new Point(screenPoint.X - dragOffset.X, screenPoint.Y - dragOffset.Y);
        }
        internal bool FinishDrag(Point screenPoint)
        {
            if (!dragPending) return false;
            bool moved = dragging;
            dragPending = dragging = false;
            Capture = false;
            if (!moved) return false;
            var screen = Screen.FromPoint(screenPoint);
            if (screen.DeviceName != TargetScreen().DeviceName)
            {
                // A deliberate cross-monitor drop pins the destination so automatic following
                // does not immediately undo the user's move.
                followActive = false;
                screenDevice = screen.DeviceName;
            }
            dockEdge = NearestEdge(screen.WorkingArea, screenPoint);
            page = 0;
            if (!testing) SaveSettings();
            Arrange();
            return true;
        }
        internal void CancelDrag()
        {
            dragPending = dragging = false;
            Capture = false;
            Arrange();
        }
        internal static DockEdge NearestEdge(Rectangle area, Point point)
        {
            DockEdge edge = DockEdge.Top;
            int distance = Math.Abs(point.Y - area.Top);
            if (Math.Abs(point.Y - area.Bottom) < distance) { distance = Math.Abs(point.Y - area.Bottom); edge = DockEdge.Bottom; }
            if (Math.Abs(point.X - area.Left) < distance) { distance = Math.Abs(point.X - area.Left); edge = DockEdge.Left; }
            if (Math.Abs(point.X - area.Right) < distance) edge = DockEdge.Right;
            return edge;
        }
        private void OnPointerMove(object sender, MouseEventArgs e)
        {
            if (dragPending) { MoveDrag(Cursor.Position); return; }
            if (!SelectionEnabled) return;
            int index = Tiles.FindIndex(t => t.Bounds.Contains(e.Location));
            bool control = IsControlPoint(e.Location);
            int nextControl = Array.FindIndex(new[] { previousButton, nextButton, settingsButton, hideButton }, r => r.Contains(e.Location));
            Cursor = index >= 0 || control ? Cursors.Hand : Cursors.SizeAll;
            if (index == hovered && nextControl == hoveredControl) return;
            hoveredControl = nextControl;
            hovered = index;
            tooltip.Hide(this);
            if (index >= 0 && !testing) tooltip.Show(Tiles[index].Window.Title + "\n点击切换到此窗口", this, e.X, e.Y - S(46), 4000);
            Invalidate();
        }
        private void OnPointerUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragPending && FinishDrag(Cursor.Position)) return;
            if (!SelectionEnabled) return;
            if (e.Button == MouseButtons.Right) { menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            if (hideButton.Contains(e.Location)) { HideDock(); return; }
            if (settingsButton.Contains(e.Location)) { menu.Show(this, new Point(settingsButton.Left, settingsButton.Bottom)); return; }
            if (previousButton.Contains(e.Location)) { ChangePage(-1); return; }
            if (nextButton.Contains(e.Location)) { ChangePage(1); return; }
            var tile = Tiles.FirstOrDefault(t => t.Bounds.Contains(e.Location));
            if (tile == null) return;
            tooltip.Hide(this);
            if (!Windows.Activate(tile.Window.Handle))
            {
                status = "切换未成功，请重试；管理员窗口可能需要相同权限";
                statusUntil = DateTime.Now.AddSeconds(5);
            }
            RefreshWindows();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (palette == null) return;
            ThemeDrawing.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), S(12), palette.Surface);
            ThemeDrawing.Stroke(g, new Rectangle(0, 0, Width - 1, Height - 1), S(12), palette.Border, 1);
            bool crowdedHeader = windows.Count > capacity && dockEdge != DockEdge.Left && dockEdge != DockEdge.Right && Width < S(270);
            if (!crowdedHeader)
            {
                ThemeDrawing.Fill(g, new Rectangle(S(9), S(10), S(3), S(11)), S(2), SelectionEnabled ? palette.Accent : palette.Muted);
                if (Width >= S(110)) TextRenderer.DrawText(g, Width < S(270) ? "PJ" : "PEEKJET", brandFont, new Rectangle(S(19), S(1), S(55), S(27)), palette.Text, TextFormatFlags.VerticalCenter);
            }
            // Leave room for controls even on a narrow, single-card dock.
            if (Width >= S(320)) TextRenderer.DrawText(g, windows.Count + " 个工作区", smallFont, new Rectangle(S(80), S(2), S(90), S(25)), palette.Muted, TextFormatFlags.VerticalCenter);
            DrawButton(g, previousButton, "\uE76B", page > 0, 0);
            DrawButton(g, nextButton, "\uE76C", (page + 1) * capacity < windows.Count, 1);
            DrawButton(g, settingsButton, "\uE713", true, 2);
            DrawButton(g, hideButton, "\uE921", true, 3);
            if (windows.Count == 0)
            {
                TextRenderer.DrawText(g, "等待 VS Code 窗口", titleFont, new Rectangle(S(20), S(59), Width - S(40), S(25)), palette.Text, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, "打开多个工作区以开始预览", smallFont, new Rectangle(S(14), S(93), Width - S(28), S(24)), palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            for (int i = 0; i < Tiles.Count; i++)
            {
                var tile = Tiles[i];
                bool selected = tile.Window.Handle == active;
                bool focused = SelectionEnabled && (i == hovered || selected);
                Color edge = focused ? palette.Accent : palette.Border;
                ThemeDrawing.Fill(g, tile.Bounds, S(8), SelectionEnabled && i == hovered ? palette.Hover : palette.Card);
                ThemeDrawing.Fill(g, tile.Preview, S(5), palette.Preview);
                ThemeDrawing.Stroke(g, tile.Bounds, S(8), edge, focused ? Math.Max(1, S(1)) : 1);
                if (tile.ExampleImage != null)
                {
                    double ratio = Math.Min((double)tile.Preview.Width / tile.ExampleImage.Width, (double)tile.Preview.Height / tile.ExampleImage.Height);
                    int imageWidth = (int)(tile.ExampleImage.Width * ratio), imageHeight = (int)(tile.ExampleImage.Height * ratio);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(tile.ExampleImage, new Rectangle(tile.Preview.X + (tile.Preview.Width - imageWidth) / 2, tile.Preview.Y + (tile.Preview.Height - imageHeight) / 2, imageWidth, imageHeight));
                }
                else if (tile.Thumbnail == IntPtr.Zero)
                    TextRenderer.DrawText(g, "暂时无法预览", smallFont, tile.Preview, palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                string title = tile.Window.ProjectName;
                using (var brush = new SolidBrush(focused ? palette.Accent : palette.Muted))
                    g.FillEllipse(brush, tile.Bounds.X + S(7), tile.Bounds.Bottom - S(13), S(4), S(4));
                var label = new Rectangle(tile.Bounds.X + S(16), tile.Preview.Bottom + S(2), tile.Bounds.Width - S(22), S(20));
                if (tile.Window.Minimized) { title = "[已最小化] " + title; }
                TextRenderer.DrawText(g, title, titleFont, label, focused ? palette.AccentText : palette.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            bool vertical = dockEdge == DockEdge.Left || dockEdge == DockEdge.Right;
            string hint = Width < S(200) ? (SelectionEnabled ? "点击 / 拖动" : "停稳 250ms") : (SelectionEnabled ? "点击选择 · 拖动移位" : "停稳 0.25 秒可选择");
            string footer = DateTime.Now < statusUntil ? status : Width < S(400) ? hint : string.Format("{0} 个窗口 · {1}/{2} 页   |   {3}", windows.Count, page + 1, Math.Max(1, (windows.Count + capacity - 1) / capacity), hint);
            if (vertical && windows.Count > capacity) footer = string.Format("{0}/{1}", page + 1, (windows.Count + capacity - 1) / capacity);
            TextRenderer.DrawText(g, footer, smallFont, new Rectangle(vertical && windows.Count > capacity ? S(30) : S(10), Height - S(22), Width - (vertical && windows.Count > capacity ? S(60) : S(20)), S(20)), palette.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | (vertical ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Default));
            if (!SelectionEnabled && hoverStarted >= 0)
            {
                int progress = (int)((Width - S(24)) * Math.Min(1.0, (double)hoverElapsed / HoverDelayMilliseconds));
                using (var brush = new SolidBrush(palette.Accent)) g.FillRectangle(brush, S(12), Height - S(3), progress, S(2));
            }
        }
        private void DrawButton(Graphics g, Rectangle rect, string text, bool enabled, int index)
        {
            if (rect.IsEmpty) return;
            if (SelectionEnabled && enabled && hoveredControl == index) ThemeDrawing.Fill(g, rect, S(5), palette.Hover);
            TextRenderer.DrawText(g, text, iconFont, rect, enabled ? palette.Muted : palette.Border, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private void BuildMenu()
        {
            // Dispose removed menu items and their event handlers before rebuilding.
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            menu.Items.Add(Visible ? "隐藏预览 (Ctrl+Alt+V)" : "显示预览 (Ctrl+Alt+V)", null, delegate { Toggle(); });
            menu.Items.Add(new ToolStripSeparator());
            var themes = new ToolStripMenuItem("外观主题");
            var systemTheme = new ToolStripMenuItem("跟随系统") { Checked = themeId == "system" };
            systemTheme.Click += delegate { SelectTheme("system"); };
            themes.DropDownItems.Add(systemTheme);
            themes.DropDownItems.Add(new ToolStripSeparator());
            foreach (var entry in ThemeCatalog.All)
            {
                string id = entry.Id;
                var item = new ToolStripMenuItem(entry.Name) { Checked = themeId == id };
                item.Click += delegate { SelectTheme(id); };
                themes.DropDownItems.Add(item);
            }
            menu.Items.Add(themes);
            var sizes = new ToolStripMenuItem("预览大小");
            int[] values = { 80, 100, 120, 160 };
            string[] labels = { "极小", "迷你（默认）", "小", "中" };
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                var item = new ToolStripMenuItem(labels[i]) { Checked = preferredWidth == value };
                item.Click += delegate { SelectPreviewSize(value); };
                sizes.DropDownItems.Add(item);
            }
            menu.Items.Add(sizes);
            var edges = new ToolStripMenuItem("停靠边缘");
            DockEdge[] edgeValues = { DockEdge.Top, DockEdge.Bottom, DockEdge.Left, DockEdge.Right };
            string[] edgeLabels = { "顶部（默认）", "底部", "左侧", "右侧" };
            for (int i = 0; i < edgeValues.Length; i++)
            {
                DockEdge edge = edgeValues[i];
                var item = new ToolStripMenuItem(edgeLabels[i]) { Checked = dockEdge == edge };
                item.Click += delegate { SelectEdge(edge); };
                edges.DropDownItems.Add(item);
            }
            menu.Items.Add(edges);
            var displays = new ToolStripMenuItem("显示位置");
            var automatic = new ToolStripMenuItem("自动跟随 VS Code（推荐）") { Checked = followActive };
            automatic.Click += delegate { SelectDisplay(null); };
            displays.DropDownItems.Add(automatic);
            displays.DropDownItems.Add(new ToolStripSeparator());
            foreach (var screen in Screen.AllScreens)
            {
                string device = screen.DeviceName;
                var item = new ToolStripMenuItem("固定到 " + device + (screen.Primary ? "（主屏）" : "")) { Checked = !followActive && screenDevice == device };
                item.Click += delegate { SelectDisplay(device); };
                displays.DropDownItems.Add(item);
            }
            menu.Items.Add(displays);
            var avoidance = new ToolStripMenuItem("输入时自动避让（推荐）") { Checked = avoidWhileTyping };
            avoidance.Click += delegate { avoidWhileTyping = !avoidWhileTyping; ClearTypingAvoidance(); if (!testing) SaveSettings(); };
            menu.Items.Add(avoidance);
            menu.Items.Add("刷新窗口", null, delegate { RefreshWindows(); Arrange(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            StyleMenuItems(menu.Items);
        }
        private void DisplayChanged(object sender, EventArgs e) { QueueArrange(); }
        private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (IsHandleCreated && !disposing)
                try { BeginInvoke(new Action(delegate { if (themeId == "system") ApplyTheme(); Arrange(); })); } catch (InvalidOperationException) { }
        }
        private void QueueArrange()
        {
            if (IsHandleCreated && !disposing)
            {
                try { BeginInvoke(new Action(Arrange)); } catch (InvalidOperationException) { }
            }
        }
        protected override void Dispose(bool managed)
        {
            if (managed && !disposing)
            {
                disposing = true;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged;
                timer.Stop(); timer.Dispose();
                pointerTimer.Stop(); pointerTimer.Dispose();
                if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
                if (IsHandleCreated && hotkeyRegistered) Native.UnregisterHotKey(Handle, 1);
                foreach (var tile in Tiles) tile.Dispose();
                tray.Visible = false; tray.Dispose(); menu.Dispose(); tooltip.Dispose();
                titleFont.Dispose(); smallFont.Dispose(); brandFont.Dispose(); appIcon.Dispose();
                iconFont.Dispose(); menuFont.Dispose();
            }
            base.Dispose(managed);
        }
        private static Icon CreateIcon()
        {
            // A small, original window-stack icon, stored entirely in the executable.
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(Color.FromArgb(65, 169, 255), 3))
            {
                g.Clear(Color.FromArgb(20, 25, 34));
                g.DrawRectangle(pen, 4, 9, 20, 17);
                g.DrawLine(pen, 10, 4, 28, 4); g.DrawLine(pen, 28, 4, 28, 21);
                g.DrawLine(pen, 5, 15, 23, 15);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }

}
