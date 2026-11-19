using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using SonoHUD.Core;

namespace SonoHUD.App.Platform;

// Keep Windows caption hit testing, snap, system menu and resizing intact.
internal static class WindowChromeTheme
{
    internal static IReadOnlyDictionary<string,int> Apply(IntPtr hwnd, bool active, MusicTheme? theme=null)
    {
        if(hwnd==IntPtr.Zero)return new Dictionary<string,int>();
        bool highContrast=SystemParameters.HighContrast;
        int dark=highContrast || theme?.Light==true?0:1, corners=2;
        int caption=highContrast?-1:ColorRef(theme?.Raised??DesignTokens.Elevated);
        int text=highContrast?-1:ColorRef(active?theme?.Ink??DesignTokens.TextPrimary:theme?.Secondary??DesignTokens.TextSecondary);
        int border=highContrast?-1:ColorRef(theme?.Border??DesignTokens.Border);
        return new Dictionary<string,int>
        {
            ["dark"]=DwmSetWindowAttribute(hwnd,20,ref dark,4),
            ["corners"]=DwmSetWindowAttribute(hwnd,33,ref corners,4),
            ["border"]=DwmSetWindowAttribute(hwnd,34,ref border,4),
            ["caption"]=DwmSetWindowAttribute(hwnd,35,ref caption,4),
            ["text"]=DwmSetWindowAttribute(hwnd,36,ref text,4)
        };
    }
    static int ColorRef(string value){var c=(Color)ColorConverter.ConvertFromString(value);return c.R|(c.G<<8)|(c.B<<16);}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int bytes);
}
