using System.Runtime.InteropServices;
namespace SonoHUD.App.Platform;

internal static class Native
{
    internal const uint NoActivate = 0x08000000, ToolWindow = 0x80, Transparent = 0x20, Layered = 0x80000, Topmost = 8, NoRedirection = 0x00200000;
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    internal sealed record Monitor(string Device, bool Primary, Rect Bounds, Rect Work, double Scale)
    {
        public string Label => $"{Device.Replace("\\\\.\\", "")} · {Bounds.Width} × {Bounds.Height}" + (Primary ? " · 주 화면" : "");
        public string FriendlyLabel => $"{(Primary ? "주 모니터" : "보조 모니터")} · {Bounds.Width} × {Bounds.Height}";
    }
    internal static SonoHUD.Core.DesktopMonitor[] DesktopMonitors() => Monitors().Select(m => new SonoHUD.Core.DesktopMonitor(m.Device, new(m.Bounds.Left, m.Bounds.Top, m.Bounds.Width, m.Bounds.Height), new(m.Work.Left, m.Work.Top, m.Work.Width, m.Work.Height), m.Scale)).ToArray();
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    internal delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass { public uint Size, Style; public WndProc Proc; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string? Menu; public string Name; public IntPtr SmallIcon; }
    internal static Monitor[] Monitors()
    {
        var result = new List<Monitor>(); EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) => { var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" }; if (!GetMonitorInfoW(h, ref info)) return true; double scale = 1; try { if (GetDpiForMonitor(h, 0, out uint x, out _) == 0) scale = x / 96.0; } catch (EntryPointNotFoundException) { } result.Add(new(info.Device, (info.Flags & 1) != 0, info.Monitor, info.Work, scale)); return true; }, IntPtr.Zero); return result.ToArray();
    }
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr h, ref MonitorInfo info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr h, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtrW(IntPtr h, int index);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowLongPtrW(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] internal static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(IntPtr h, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandleW(string? name);
}
