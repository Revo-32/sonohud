using System.Windows;
using System.Windows.Media;
using SonoHUD.App.Rendering;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

// One DrawingVisual: changing samples never invalidates parent measure/arrange.
public sealed class SpectrumView : FrameworkElement
{
    readonly DrawingVisual drawing = new();
    readonly float[] snapshot = new float[96];
    readonly float[] peakSnapshot = new float[96];
    int length,peakLength;
    Brush ink = ElementDrawing.Brush(MusicTheme.Gallery[0].Spectrum);
    internal SpectrumStyle SpectrumStyle { get; set; } = SpectrumStyle.Bars;
    internal int BandCount { get; set; }
    internal long DrawCount { get; private set; }
    public SpectrumView() { AddVisualChild(drawing); ClipToBounds = true; Focusable = false; IsHitTestVisible = false; ink.Freeze(); SizeChanged += (_, _) => Paint(); }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? drawing : throw new ArgumentOutOfRangeException(nameof(index));
    internal void SetSpectrum(ReadOnlySpan<float> values, SpectrumStyle style, Brush? brush = null,ReadOnlySpan<float> peaks=default)
    {
        length = Math.Min(values.Length, snapshot.Length); values[..length].CopyTo(snapshot); SpectrumStyle = style;
        peakLength=Math.Min(peaks.Length,peakSnapshot.Length);peaks[..peakLength].CopyTo(peakSnapshot);
        if (brush is not null) ink = brush; Paint();
    }
    void Paint()
    {
        using var dc = drawing.RenderOpen(); SpectrumDrawing.Draw(dc, ActualWidth, ActualHeight, snapshot.AsSpan(0, length), ink, SpectrumStyle, BandCount,peakSnapshot.AsSpan(0,peakLength)); DrawCount++;
    }
}
