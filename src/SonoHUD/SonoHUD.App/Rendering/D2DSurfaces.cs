using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using Windows.UI.Composition;
using WinRT;
using SonoHUD.App.Platform;
namespace SonoHUD.App.Rendering;
// Adapted from independently authored M3.2 interop; no FluentFlyout source.
// Direct2D ABI slots verified against Windows SDK d2d1.h.
internal sealed unsafe class D2DSurfaces : IDisposable
{
    IntPtr d3d, context, d2d; readonly CompositionGraphicsDevice graphics;
    public D2DSurfaces(Compositor compositor)
    {
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3d, out _, out context));
            var dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in dxgiId, out var dxgi));
            try { Marshal.ThrowExceptionForHR(D2D1CreateDevice(dxgi, IntPtr.Zero, out d2d)); } finally { Marshal.Release(dxgi); }
            compositor.As<ICompositorInterop>().CreateGraphicsDevice(d2d, out var abi);
            try { graphics = MarshalInterface<CompositionGraphicsDevice>.FromAbi(abi); } finally { Marshal.Release(abi); }
        }
        catch { ReleaseDevices(); throw; }
    }
    public CompositionDrawingSurface Upload(byte[] pixels, int width, int height)
    {
        var surface = graphics.CreateDrawingSurface(new Windows.Foundation.Size(width, height), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        try
        {
            var drawing = surface.As<IDrawingSurfaceInterop>(); var iid = new Guid("e8f7fe7a-191c-466d-ad95-975678bda998");
            drawing.BeginDraw(IntPtr.Zero, ref iid, out var dc, out var offset);
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                var table = *(IntPtr**)dc;
                var properties = new BitmapProperties { Format = 87, Alpha = 1, DpiX = 96, DpiY = 96 }; var size = new SizeU { Width = (uint)width, Height = (uint)height };
                fixed (byte* data = pixels) { Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, SizeU, byte*, uint, BitmapProperties*, IntPtr*, int>)table[4])(dc, size, data, (uint)(width * 4), &properties, &bitmap)); }
                var clear = new ColorF(); ((delegate* unmanaged[Stdcall]<IntPtr, ColorF*, void>)table[47])(dc, &clear);
                var matrix = new Matrix3x2 { M11 = 1, M22 = 1, DX = offset.X, DY = offset.Y }; ((delegate* unmanaged[Stdcall]<IntPtr, Matrix3x2*, void>)table[30])(dc, &matrix);
                var rect = new RectF { Right = width, Bottom = height }; ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, RectF*, float, uint, IntPtr, void>)table[26])(dc, bitmap, &rect, 1, 1, IntPtr.Zero);
            }
            finally { if (bitmap != IntPtr.Zero) Marshal.Release(bitmap); Marshal.Release(dc); drawing.EndDraw(); }
            return surface;
        }
        catch { surface.Dispose(); throw; }
    }
    void ReleaseDevices() { if (d2d != IntPtr.Zero) Marshal.Release(d2d); if (context != IntPtr.Zero) Marshal.Release(context); if (d3d != IntPtr.Zero) Marshal.Release(d3d); d2d = context = d3d = IntPtr.Zero; }
    public void Dispose() { graphics.Dispose(); ReleaseDevices(); }
    [StructLayout(LayoutKind.Sequential)] struct SizeU { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapProperties { public uint Format, Alpha; public float DpiX, DpiY; }
    [StructLayout(LayoutKind.Sequential)] struct ColorF { public float R, G, B, A; }
    [StructLayout(LayoutKind.Sequential)] struct Matrix3x2 { public float M11, M12, M21, M22, DX, DY; }
    [StructLayout(LayoutKind.Sequential)] struct RectF { public float Left, Top, Right, Bottom; }
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, uint driver, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out uint selected, out IntPtr context);
    [DllImport("d2d1.dll")] static extern int D2D1CreateDevice(IntPtr dxgi, IntPtr properties, out IntPtr device);
    [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorInterop
    {
        void CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr surface); void CreateCompositionSurfaceForSwapChain(IntPtr chain, out IntPtr surface); void CreateGraphicsDevice(IntPtr device, out IntPtr graphics);
    }
    [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDrawingSurfaceInterop
    {
        void BeginDraw(IntPtr update, ref Guid iid, out IntPtr obj, out Native.Point offset); void EndDraw(); void Resize(Native.Point size); void Scroll(IntPtr scroll, IntPtr clip, int x, int y); void ResumeDraw(); void SuspendDraw();
    }
}
