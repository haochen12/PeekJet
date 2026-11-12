using System;
using System.Text;
using System.Runtime.InteropServices;

namespace PeekJet
{
    internal static class Native
    {
        internal delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);
        internal delegate IntPtr KeyboardHookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rect(int x, int y, int width, int height) { Left = x; Top = y; Right = x + width; Bottom = y + height; }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)] internal struct ThumbnailProperties
        {
            public uint Flags;
            public Rect Destination, Source;
            public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
        }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int length);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int hook, KeyboardHookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLong64(IntPtr hwnd, int index, IntPtr value);
        internal static long ExtendedStyle(IntPtr hwnd)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);
        }
        internal static void SetClickThrough(IntPtr hwnd, bool enabled)
        {
            long style = ExtendedStyle(hwnd);
            long updated = enabled ? style | 0x20 : style & ~0x20;
            if (style == updated) return;
            if (IntPtr.Size == 8) SetWindowLong64(hwnd, -20, new IntPtr(updated));
            else SetWindowLong32(hwnd, -20, (int)updated);
        }
        [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
        [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
        [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out Size size);
        [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
