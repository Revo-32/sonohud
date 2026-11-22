using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SonoHUD.App.Deployment;

internal sealed class LoginTray : IDisposable
{
    const uint Callback = 0x8000 + 51;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly StudioWindow window;
    readonly HwndSource source;
    Data data;
    IntPtr ownedIcon;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Data
    {
        public int size; public IntPtr window; public uint id, flags, callback; public IntPtr icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string tip;
        public uint state, stateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string info;
        public uint timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string infoTitle;
        public uint infoFlags; public Guid guid; public IntPtr balloonIcon;
    }
    [DllImport("shell32", CharSet=CharSet.Unicode, EntryPoint="Shell_NotifyIconW")] static extern bool Notify(uint action, ref Data data);
    [DllImport("shell32", CharSet=CharSet.Unicode, EntryPoint="ExtractIconExW")] static extern uint Extract(string file, int index, IntPtr large, out IntPtr small, uint count);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string value);
    [DllImport("user32")] static extern bool DestroyIcon(IntPtr icon);
    internal bool Registered { get; private set; }
    internal LoginTray(StudioWindow window)
    {
        this.window = window;
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        Extract(Environment.ProcessPath!, 0, IntPtr.Zero, out ownedIcon, 1);
        data = new() { size=Marshal.SizeOf<Data>(), window=handle, id=1, flags=7, callback=Callback,
            icon=ownedIcon, tip="SonoHUD", info="", infoTitle="" };
        source.AddHook(Hook);
        Registered = Notify(0, ref data);
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == taskbarCreated) Registered = Notify(0, ref data);
        if (msg == Callback)
        {
            if (lParam == 0x202) window.Dispatcher.BeginInvoke(window.OpenFromTray);
            if (lParam == 0x205) window.Dispatcher.BeginInvoke(() =>
            {
                var menu = new ContextMenu();
                var open = new MenuItem { Header="SonoHUD 열기" }; open.Click += (_,_) => window.OpenFromTray();
                var exit = new MenuItem { Header="SonoHUD 종료" }; exit.Click += async (_,_) => await window.ExitApplicationAsync(ExitReason.TrayExit);
                menu.Items.Add(open); menu.Items.Add(exit); menu.IsOpen = true;
            });
            handled=true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Notify(2, ref data);
        Registered = false;
        if (!source.IsDisposed) source.RemoveHook(Hook);
        if (ownedIcon != IntPtr.Zero) { DestroyIcon(ownedIcon); ownedIcon = IntPtr.Zero; }
    }
}
