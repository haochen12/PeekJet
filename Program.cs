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
            if (args.Contains("--self-test-controls")) { Environment.Exit(SelfTest.RunControls()); return; }
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
        private string cachedTitle, cachedProject;
        internal string ProjectName
        {
            get
            {
                if (cachedProject == null || cachedTitle != Title)
                { cachedTitle = Title; cachedProject = WindowTitles.ProjectName(Title); }
                return cachedProject;
            }
        }
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
        [ThreadStatic] private static WindowScanner scanner;
        internal static List<CodeWindow> Find()
        {
            if (scanner == null) scanner = new WindowScanner();
            return scanner.Find();
        }
        private sealed class WindowScanner
        {
            private List<CodeWindow> found;
            // Cache process names for this enumeration, including processes with multiple top-level windows.
            private readonly Dictionary<uint, bool> names = new Dictionary<uint, bool>();
            private readonly StringBuilder className = new StringBuilder(256);
            private readonly StringBuilder title = new StringBuilder(2048);
            private readonly StringBuilder processPath = new StringBuilder(1024);
            private readonly Native.EnumWindowProc callback;
            internal WindowScanner() { callback = Visit; }
            internal List<CodeWindow> Find()
            {
                found = new List<CodeWindow>();
                names.Clear(); // Never trust a process ID from an earlier enumeration.
                Native.EnumWindows(callback, IntPtr.Zero);
                return found;
            }
            private bool Visit(IntPtr hwnd, IntPtr unused)
            {
                if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, 4) != IntPtr.Zero) return true;
                className.Clear();
                Native.GetClassName(hwnd, className, className.Capacity);
                const string prefix = "Chrome_WidgetWin_";
                if (className.Length < prefix.Length) return true;
                for (int i = 0; i < prefix.Length; i++) if (className[i] != prefix[i]) return true;
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                bool isCode;
                if (!names.TryGetValue(pid, out isCode))
                {
                    isCode = false;
                    // Query only this process image, rather than asking .NET Framework
                    // Process.ProcessName to collect a system-wide process snapshot.
                    IntPtr process = Native.OpenProcess(0x1000, false, pid);
                    if (process != IntPtr.Zero)
                    {
                        try
                        {
                            uint length = (uint)processPath.Capacity;
                            processPath.Clear();
                            bool queried = Native.QueryFullProcessImageName(process, 0, processPath, ref length);
                            if (!queried && System.Runtime.InteropServices.Marshal.GetLastWin32Error() == 122)
                            {
                                processPath.EnsureCapacity(32768);
                                length = (uint)processPath.Capacity;
                                queried = Native.QueryFullProcessImageName(process, 0, processPath, ref length);
                            }
                            if (queried)
                            {
                                string name = Path.GetFileName(processPath.ToString());
                                isCode = name.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase);
                            }
                        }
                        finally { Native.CloseHandle(process); }
                    }
                    names[pid] = isCode;
                }
                if (!isCode) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                title.Clear();
                Native.GetWindowText(hwnd, title, title.Capacity);
                if (title.Length > 0) found.Add(new CodeWindow { Handle = hwnd, Title = title.ToString(), Minimized = Native.IsIconic(hwnd) });
                return true;
            }
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
        internal Native.Size SourceSize;
        internal Rectangle LastDestination;
        internal byte LastOpacity;
        internal bool LastVisible, PropertiesSet;
        public void Dispose()
        {
            if (Thumbnail != IntPtr.Zero) Native.DwmUnregisterThumbnail(Thumbnail);
            Thumbnail = IntPtr.Zero;
            PropertiesSet = false;
            SourceSize = new Native.Size();
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
        private readonly Font brandFont = new Font("Segoe UI", 7f, FontStyle.Regular);
        private readonly Font iconFont = new Font("Segoe MDL2 Assets", 9f);
        private readonly Font menuFont = new Font("Microsoft YaHei UI", 9f);
        private string themeId = "midnight";
        private ThemePalette palette;
        private int hoveredControl = -1;
        private IntPtr active;
        private int hoverDelayMilliseconds = 150;
        internal int HoverDelayMilliseconds { get { return hoverDelayMilliseconds; } }
        private int DwellVisualDelayMilliseconds { get { return Math.Min(40, hoverDelayMilliseconds / 5); } }
        internal double DwellProgress
        {
            get { return hoverStarted < 0 || SelectionEnabled ? 0 : Math.Max(0, Math.Min(1,
                (double)(hoverElapsed - DwellVisualDelayMilliseconds) / (HoverDelayMilliseconds - DwellVisualDelayMilliseconds))); }
        }
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
        private bool nativeCorners;
        internal int CornerRadius { get { return S(nativeCorners ? 8 : 12); } }
        private Rectangle previousButton, nextButton, minimizeButton;
        private ToolTip tooltip = new ToolTip();
        private Icon appIcon, trayIcon;
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
            appIcon = CreateIcon(new Size(32, 32));
            trayIcon = CreateIcon(SystemInformation.SmallIconSize);
            Icon = appIcon;
            tray = new NotifyIcon { Icon = trayIcon, Text = "PeekJet · Ctrl+Alt+V", Visible = true, ContextMenuStrip = menu };
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
            pointerTimer.Interval = 50;
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
            // A GDI window region is a binary mask and cannot antialias its edge.
            // Uniform window opacity still allows DWM to composite smooth corners.
            int corners = 2; // DWMWCP_ROUND (Windows 11).
            nativeCorners = Native.DwmSetWindowAttribute(Handle, 33, ref corners, 4) == 0;
            UpdateCornerRegion();
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
                    int savedDelay = Convert.ToInt32(key.GetValue("HoverDelayMilliseconds", 150));
                    if (savedDelay >= 50 && savedDelay <= 2000) hoverDelayMilliseconds = savedDelay;
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
                    key.SetValue("HoverDelayMilliseconds", hoverDelayMilliseconds);
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
        internal void SelectHoverDelay(int milliseconds)
        {
            hoverDelayMilliseconds = Math.Max(50, Math.Min(2000, milliseconds));
            // Changing the threshold must not arm a partly completed dwell.
            ResetInteraction();
            if (!testing) SaveSettings();
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
            UpdateCornerRegion();
        }
        private void UpdateCornerRegion()
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            // Setting any region disables the compositor's antialiased corners.
            if (nativeCorners)
            {
                var previous = Region;
                Region = null;
                if (previous != null) previous.Dispose();
                return;
            }
            using (var path = ThemeDrawing.Rounded(ClientRectangle, CornerRadius))
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
            // Preserve ordering and unchanged descriptors without allocating dictionaries,
            // query iterators or a replacement list on every background scan.
            bool changed = false, repaint = active != foreground;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                CodeWindow next = null;
                for (int j = 0; j < found.Count; j++)
                    if (found[j].Handle == windows[i].Handle) { next = found[j]; break; }
                if (next == null) { windows.RemoveAt(i); changed = true; }
                else if (windows[i].Title != next.Title || windows[i].Minimized != next.Minimized)
                { windows[i] = next; repaint = true; }
            }
            for (int i = 0; i < found.Count; i++)
            {
                bool exists = false;
                for (int j = 0; j < windows.Count; j++) if (windows[j].Handle == found[i].Handle) { exists = true; break; }
                if (!exists) { windows.Add(found[i]); changed = true; }
            }
            repaint |= changed;
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
                if (repaint) Invalidate();
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
            var previousTiles = Tiles.ToDictionary(t => t.Window.Handle);
            Tiles.Clear();
            using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            layoutScreenDevice = target.DeviceName;
            layoutWorkArea = area;
            bool vertical = dockEdge == DockEdge.Left || dockEdge == DockEdge.Right;
            int margin = S(8), gap = S(6), width = S(preferredWidth);
            int headerHeight = S(12), footerHeight = S(10);
            int contentLeft = margin + S(18), contentRight = margin + S(34);
            int usable = Math.Max(S(160), area.Width - S(32));
            width = Math.Min(width, usable - contentLeft - contentRight);
            width = Math.Min(width, Math.Max(S(60), (int)((area.Height - S(20) - headerHeight - footerHeight - S(22)) * 16.0 / 9.0)));
            int previewHeight = (int)(width * 9.0 / 16.0);
            int tileHeight = previewHeight + S(22);
            capacity = vertical ? Math.Max(1, Math.Min(5, (area.Height - S(20) - headerHeight - footerHeight + gap) / (tileHeight + gap))) :
                Math.Max(1, Math.Min(5, (usable - contentLeft - contentRight + gap) / (width + gap)));
            int maxPage = Math.Max(0, (windows.Count - 1) / capacity);
            page = Math.Min(page, maxPage);
            int count = Math.Min(capacity, Math.Max(0, windows.Count - page * capacity));
            ClientSize = vertical ? new Size(contentLeft + width + contentRight, headerHeight + count * tileHeight + Math.Max(0, count - 1) * gap + footerHeight) :
                new Size(contentLeft + count * width + Math.Max(0, count - 1) * gap + contentRight, headerHeight + tileHeight + footerHeight);
            bool hasPages = windows.Count > capacity;
            int controlX = ClientSize.Width - S(34), controlY = (ClientSize.Height - S(26)) / 2;
            previousButton = !hasPages ? Rectangle.Empty : new Rectangle(controlX + S(2), controlY - S(22), S(22), S(20));
            nextButton = !hasPages ? Rectangle.Empty : new Rectangle(controlX + S(2), controlY + S(28), S(22), S(20));
            minimizeButton = new Rectangle(controlX, controlY, S(26), S(26));
            for (int i = 0; i < count; i++)
            {
                var window = windows[page * capacity + i];
                PreviewTile tile;
                if (previousTiles.TryGetValue(window.Handle, out tile)) previousTiles.Remove(window.Handle);
                else tile = new PreviewTile();
                tile.Window = window;
                tile.Bounds = new Rectangle(vertical ? contentLeft : contentLeft + i * (width + gap), vertical ? headerHeight + i * (tileHeight + gap) : headerHeight, width, tileHeight);
                tile.Preview = new Rectangle(tile.Bounds.X + S(3), tile.Bounds.Y + S(3), width - S(6), previewHeight - S(6));
                Tiles.Add(tile);
                UpdateThumbnail(tile);
            }
            foreach (var unused in previousTiles.Values) unused.Dispose();
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
        private void UpdateThumbnail(PreviewTile tile, bool querySource = true)
        {
            if (tile.Thumbnail == IntPtr.Zero)
            {
                IntPtr thumbnail;
                tile.LastError = Native.DwmRegisterThumbnail(Handle, tile.Window.Handle, out thumbnail);
                tile.Thumbnail = tile.LastError == 0 ? thumbnail : IntPtr.Zero;
                if (tile.Thumbnail == IntPtr.Zero) return;
            }
            Native.Size source = tile.SourceSize;
            Rectangle destination = tile.Preview;
            if (querySource && Native.DwmQueryThumbnailSourceSize(tile.Thumbnail, out source) == 0) tile.SourceSize = source;
            if (source.Width > 0 && source.Height > 0)
            {
                double ratio = Math.Min((double)destination.Width / source.Width, (double)destination.Height / source.Height);
                int w = Math.Max(1, (int)(source.Width * ratio)), h = Math.Max(1, (int)(source.Height * ratio));
                destination = new Rectangle(destination.X + (destination.Width - w) / 2, destination.Y + (destination.Height - h) / 2, w, h);
            }
            if (tile.PropertiesSet && tile.LastDestination == destination && tile.LastOpacity == previewOpacity && tile.LastVisible == Visible) return;
            var properties = new Native.ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = new Native.Rect(destination.X, destination.Y, destination.Width, destination.Height), Opacity = previewOpacity, Visible = Visible, ClientOnly = false };
            tile.LastError = Native.DwmUpdateThumbnailProperties(tile.Thumbnail, ref properties);
            if (tile.LastError != 0) tile.Dispose();
            else
            {
                tile.LastDestination = destination; tile.LastOpacity = previewOpacity;
                tile.LastVisible = Visible; tile.PropertiesSet = true;
            }
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
            pointerTimer.Interval = 16;
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
            if (!Visible) { pointerTimer.Interval = 250; return; }
            long milliseconds = MonotonicMilliseconds();
            if (typingVisibility < 1 || milliseconds - lastTypingAt < 1200)
                AdvanceTypingAvoidance(milliseconds, Native.GetForegroundWindow());
            if (dragPending)
            {
                if ((Native.GetAsyncKeyState(1) & 0x8000) == 0) FinishDrag(Cursor.Position);
                else MoveDrag(Cursor.Position);
                return;
            }
            bool buttonsDown = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0 || (Native.GetAsyncKeyState(4) & 0x8000) != 0;
            UpdatePointer(Cursor.Position, buttonsDown, milliseconds);
            pointerTimer.Interval = recoveryStarted >= 0 || (hoverStarted >= 0 && !SelectionEnabled) ? 16 : 50;
        }
        internal void UpdatePointer(Point point, bool buttonsDown, long milliseconds)
        {
            if (disposing) return;
            if (dragPending) return;
            if (TypingAvoidanceActive || typingVisibility < 1) { ResetDwell(); return; }
            if (menu.Visible) { ResetDwell(); return; }
            if (!Visible || !Bounds.Contains(point))
            {
                if (SelectionEnabled || hoverStarted >= 0 || hovered >= 0 || hoveredControl >= 0) ResetInteraction();
                return;
            }
            if (SelectionEnabled) return;
            // Do not capture an in-progress click or drag that started in the window below us.
            if (buttonsDown)
            {
                ResetDwell();
                ApplyHoverBrightness(0);
                return;
            }
            // A fixed dead zone tolerates hand tremor without letting the anchor
            // follow a continuously moving pointer across the panel.
            if (hoverStarted < 0 || Math.Abs(point.X - hoverAnchor.X) > S(12) || Math.Abs(point.Y - hoverAnchor.Y) > S(12))
            {
                hoverAnchor = point;
                hoverStarted = milliseconds;
                hoverElapsed = 0;
                ApplyHoverBrightness(0);
            }
            else
            {
                hoverElapsed = Math.Max(hoverElapsed, Math.Max(0, milliseconds - hoverStarted));
                ApplyHoverBrightness((double)hoverElapsed / HoverDelayMilliseconds);
                if (hoverElapsed >= HoverDelayMilliseconds)
                {
                    SetSelectionEnabled(true);
                    var local = PointToClient(point);
                    OnPointerMove(this, new MouseEventArgs(MouseButtons.None, 0, local.X, local.Y, 0));
                }
            }
            InvalidateDwellProgress();
        }
        private Rectangle DwellProgressBounds { get { return new Rectangle(0, Math.Max(0, Height - S(10)), Width, S(10)); } }
        private void InvalidateDwellProgress()
        {
            if (IsHandleCreated && !disposing) Invalidate(DwellProgressBounds);
        }
        private void ResetDwell()
        {
            bool hadProgress = hoverStarted >= 0;
            hoverStarted = -1; hoverElapsed = 0;
            // Opacity changes do not repaint the client bitmap. Explicitly erase
            // the old line even when selection was never enabled.
            if (hadProgress) InvalidateDwellProgress();
        }
        private void ApplyHoverBrightness(double progress)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            double eased = progress * progress * (3 - 2 * progress);
            double opacity = (0.58 + (0.97 - 0.58) * eased) * typingVisibility;
            if (Math.Abs(Opacity - opacity) > 0.0001) Opacity = opacity;
            byte thumbnailOpacity = (byte)Math.Round((180 + (255 - 180) * eased) * typingVisibility);
            if (previewOpacity == thumbnailOpacity) return;
            previewOpacity = thumbnailOpacity;
            foreach (var tile in Tiles) UpdateThumbnail(tile, false);
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
            return previousButton.Contains(point) || nextButton.Contains(point) || minimizeButton.Contains(point);
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
            int nextControl = Array.FindIndex(new[] { previousButton, nextButton, minimizeButton }, r => r.Contains(e.Location));
            Cursor = index >= 0 || control ? Cursors.Hand : Cursors.SizeAll;
            if (index == hovered && nextControl == hoveredControl) return;
            hoveredControl = nextControl;
            hovered = index;
            tooltip.Hide(this);
            if (!testing)
            {
                string detail = index >= 0 ? Tiles[index].Window.Title : nextControl == 0 ? "上一页" : nextControl == 1 ? "下一页" : nextControl == 2 ? "最小化全部 VS Code 窗口" : "";
                if (detail.Length > 0) tooltip.Show(detail, this, e.X, e.Y - S(34), 4000);
            }
            Invalidate();
        }
        private void OnPointerUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragPending && FinishDrag(Cursor.Position)) return;
            if (!SelectionEnabled) return;
            if (e.Button == MouseButtons.Right) { menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            if (minimizeButton.Contains(e.Location)) { MinimizeAllWindows(); return; }
            if (previousButton.Contains(e.Location)) { ChangePage(-1); return; }
            if (nextButton.Contains(e.Location)) { ChangePage(1); return; }
            var tile = Tiles.FirstOrDefault(t => t.Bounds.Contains(e.Location));
            if (tile == null) return;
            tooltip.Hide(this);
            if (!Windows.Activate(tile.Window.Handle))
            {
                if (!testing) tooltip.Show("暂时无法切换，请重试", this, e.X, e.Y - S(30), 3000);
            }
            RefreshWindows();
        }

        internal void MinimizeAllWindows()
        {
            tooltip.Hide(this);
            // Include every page, and restrict the action to the tracked editor windows.
            foreach (var window in windows.ToArray())
                if (Native.IsWindow(window.Handle) && !Native.IsIconic(window.Handle))
                    Native.ShowWindowAsync(window.Handle, 6); // SW_MINIMIZE, without activating another editor.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (palette == null) return;
            g.Clear(palette.Surface);
            ThemeDrawing.Stroke(g, ClientRectangle, CornerRadius, palette.Border, 1);
            if (e.ClipRectangle.Top >= DwellProgressBounds.Top)
            {
                DrawFooter(g);
                return;
            }
            var brandState = g.Save();
            g.TranslateTransform(S(13), Height / 2f);
            g.RotateTransform(90);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var brush = new SolidBrush(Blend(palette.Surface, palette.Muted, 0.78f)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("P E E K J E T", brandFont, brush, new RectangleF(-Height / 2f, -S(10), Height, S(20)), format);
            g.Restore(brandState);
            DrawMinimizeButton(g);
            if (SelectionEnabled)
            {
                DrawButton(g, previousButton, "\uE76B", page > 0, 0);
                DrawButton(g, nextButton, "\uE76C", (page + 1) * capacity < windows.Count, 1);
            }
            for (int i = 0; i < Tiles.Count; i++)
            {
                var tile = Tiles[i];
                bool selected = tile.Window.Handle == active;
                bool pointed = SelectionEnabled && i == hovered;
                Color edge = pointed ? Blend(palette.Border, palette.Accent, 0.5f) : Blend(palette.Surface, palette.Border, 0.6f);
                ThemeDrawing.Fill(g, tile.Bounds, S(8), pointed ? Blend(palette.Card, palette.Hover, 0.6f) : palette.Card);
                ThemeDrawing.Fill(g, tile.Preview, S(5), palette.Preview);
                ThemeDrawing.Stroke(g, tile.Bounds, S(8), edge, 1);
                if (tile.ExampleImage != null)
                {
                    double ratio = Math.Min((double)tile.Preview.Width / tile.ExampleImage.Width, (double)tile.Preview.Height / tile.ExampleImage.Height);
                    int imageWidth = (int)(tile.ExampleImage.Width * ratio), imageHeight = (int)(tile.ExampleImage.Height * ratio);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(tile.ExampleImage, new Rectangle(tile.Preview.X + (tile.Preview.Width - imageWidth) / 2, tile.Preview.Y + (tile.Preview.Height - imageHeight) / 2, imageWidth, imageHeight));
                }
                else if (tile.Thumbnail == IntPtr.Zero)
                    TextRenderer.DrawText(g, "\uE91B", iconFont, tile.Preview, palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                string title = tile.Window.ProjectName;
                if (tile.Window.Minimized)
                {
                    using (var pen = new Pen(palette.Muted)) g.DrawEllipse(pen, tile.Bounds.X + S(7), tile.Bounds.Bottom - S(12), S(4), S(4));
                }
                else using (var brush = new SolidBrush(selected ? palette.Accent : palette.Muted))
                    g.FillEllipse(brush, tile.Bounds.X + S(7), tile.Bounds.Bottom - S(12), S(4), S(4));
                if (selected)
                {
                    using (var pen = new Pen(palette.Accent, Math.Max(1, S(2))))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, tile.Bounds.Left + S(18), tile.Bounds.Bottom - S(2), tile.Bounds.Left + S(32), tile.Bounds.Bottom - S(2));
                    }
                }
                var label = new Rectangle(tile.Bounds.X + S(16), tile.Preview.Bottom + S(2), tile.Bounds.Width - S(22), S(20));
                TextRenderer.DrawText(g, title, titleFont, label, palette.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            DrawFooter(g);
        }
        private void DrawFooter(Graphics g)
        {
            int pages = (windows.Count + capacity - 1) / capacity;
            if (pages > 1)
            {
                int dots = Math.Min(7, pages), startPage = Math.Max(0, Math.Min(page - dots / 2, pages - dots));
                int startX = (Width - dots * S(7) + S(3)) / 2;
                int dotY = Height - S(5);
                for (int i = 0; i < dots; i++)
                    using (var brush = new SolidBrush(startPage + i == page ? palette.Accent : palette.Border))
                        g.FillEllipse(brush, startX + i * S(7), dotY, S(3), S(3));
            }
            if (DwellProgress > 0)
            {
                float length = (float)((Width - S(24)) * DwellProgress);
                using (var pen = new Pen(palette.Accent, Math.Max(1, S(2))))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, S(12), Height - S(2), S(12) + length, Height - S(2));
                }
            }
        }
        private void DrawButton(Graphics g, Rectangle rect, string text, bool enabled, int index)
        {
            if (rect.IsEmpty) return;
            if (SelectionEnabled && enabled && hoveredControl == index) ThemeDrawing.Fill(g, rect, S(5), palette.Hover);
            TextRenderer.DrawText(g, text, iconFont, rect, enabled ? palette.Muted : palette.Border, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private void DrawMinimizeButton(Graphics g)
        {
            if (minimizeButton.IsEmpty) return;
            bool pointed = SelectionEnabled && hoveredControl == 2;
            ThemeDrawing.Fill(g, minimizeButton, S(8), pointed ? palette.Hover : Blend(palette.Surface, palette.Card, 0.3f));
            ThemeDrawing.Stroke(g, minimizeButton, S(8), Blend(palette.Surface, pointed ? palette.Accent : palette.Border, pointed ? 0.6f : 0.25f), 1);
            TextRenderer.DrawText(g, "\uE921", iconFont, minimizeButton, pointed ? palette.Accent : Blend(palette.Surface, palette.Muted, 0.65f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb((int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
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
            var activation = new ToolStripMenuItem("悬停激活时间");
            int[] delays = { 100, 150, 200, 250, 350, 500, 1000 };
            foreach (int milliseconds in delays)
            {
                int value = milliseconds;
                var item = new ToolStripMenuItem(value + " ms" + (value == 150 ? "（默认）" : "")) { Checked = hoverDelayMilliseconds == value };
                item.Click += delegate { SelectHoverDelay(value); };
                activation.DropDownItems.Add(item);
            }
            activation.DropDownItems.Add(new ToolStripSeparator());
            var customDelay = new ToolStripMenuItem("自定义（50–2000 ms）") { Checked = !delays.Contains(hoverDelayMilliseconds) };
            var delayInput = new NumericUpDown {
                Minimum = 50, Maximum = 2000, Increment = 10, Value = hoverDelayMilliseconds,
                Width = S(112), Font = menuFont, BackColor = palette.Card, ForeColor = palette.Text
            };
            delayInput.ValueChanged += delegate { SelectHoverDelay((int)delayInput.Value); };
            customDelay.DropDownItems.Add(new ToolStripControlHost(delayInput) { Padding = new Padding(S(4)) });
            activation.DropDownItems.Add(customDelay);
            menu.Items.Add(activation);
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
                titleFont.Dispose(); brandFont.Dispose(); appIcon.Dispose(); trayIcon.Dispose();
                iconFont.Dispose(); menuFont.Dispose();
            }
            base.Dispose(managed);
        }
        private static Icon CreateIcon(Size size)
        {
            // One multi-resolution asset supplies the executable, window and tray.
            // Select the tray's native size so Windows need not shrink a 32px bitmap.
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PeekJet.AppIcon"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded PeekJet icon.");
                using (var icon = new Icon(stream, size)) return (Icon)icon.Clone();
            }
        }
    }

}
