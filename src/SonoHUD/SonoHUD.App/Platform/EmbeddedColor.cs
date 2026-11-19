// SPDX-License-Identifier: GPL-3.0-or-later
// ABI field layout from the pinned MIT-licensed Windows.winmd (Windows.UI.Color).
namespace Windows.UI;
[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
[WinRT.WindowsRuntimeType]
internal struct Color
{
    public byte A, R, G, B;
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new() { A=a, R=r, G=g, B=b };
}
