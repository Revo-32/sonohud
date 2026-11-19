using System.Runtime.InteropServices;

namespace SonoHUD.App.Platform;

internal static class DisplayRefresh
{
    internal static int Read(string device)
    {
        nint dc = CreateDCW("DISPLAY", device, null, 0);
        if (dc == 0) return 60;
        try { int value = GetDeviceCaps(dc, 116); return value is >= 30 and <= 1000 ? value : 60; }
        finally { DeleteDC(dc); }
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern nint CreateDCW(string driver, string device, string? output, nint init);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(nint dc, int index);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
}
