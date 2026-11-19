using System.Windows;
using System.Windows.Media;
using SonoHUD.Core;

namespace SonoHUD.App.Rendering;

internal static class SpectrumDrawing
{
    internal static SpectrumStyle Style(SceneElement element, SpectrumStyle preference = SpectrumStyle.Preset) => preference != SpectrumStyle.Preset ? preference : Enum.TryParse<SpectrumStyle>(element.Style.Text, out var style) ? style : SpectrumStyle.Bars;
    internal static int Count(double width, SpectrumStyle style) => Math.Clamp((int)(width / (style == SpectrumStyle.Minimal ? 8 : 5)), 8, style == SpectrumStyle.Curve ? 64 : 48);
    // Renderer aggregation stays within frequency intervals, never a global level.
    internal static float Band(ReadOnlySpan<float> values, int index, int count)
    {
        if (values.IsEmpty) return 0;
        double start = (double)index * values.Length / count, end = (index + 1.0) * values.Length / count, sum = 0, weight = 0;
        for (int b = (int)start; b < Math.Ceiling(end) && b < values.Length; b++)
        { double part = Math.Max(0, Math.Min(end, b + 1) - Math.Max(start, b)); sum += values[b] * part; weight += part; }
        return (float)Math.Clamp(sum / Math.Max(.00001, weight), 0, 1);
    }
    internal static void Draw(DrawingContext dc, double width, double height, ReadOnlySpan<float> values, Brush ink, SpectrumStyle style, int requestedCount = 0,ReadOnlySpan<float> peaks=default)
    {
        if (values.IsEmpty || width <= 0 || height <= 0) return;
        int count = requestedCount > 0 ? requestedCount : Count(width, style);
        if (style == SpectrumStyle.Curve)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new(0, height - Band(values, 0, count) * height), false, false);
                for (int i = 1; i < count; i++)
                {
                    double x0 = (i - 1.0) * width / (count - 1), x = i * width / (count - 1);
                    double y0 = height - Band(values, i - 1, count) * height, y = height - Band(values, i, count) * height;
                    context.BezierTo(new((x + x0) / 2, y0), new((x + x0) / 2, y), new(x, y), true, false);
                }
            }
            geometry.Freeze(); dc.DrawGeometry(null, new Pen(ink, 1.5), geometry); return;
        }
        if (style == SpectrumStyle.Radial)
        {
            double radius = Math.Min(width, height) * .3, length = Math.Min(width, height) * .19; var pen = new Pen(ink, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int i = 0; i < count; i++)
            {
                double angle = i * Math.Tau / count - Math.PI / 2, value = Band(values, i, count), outer = radius + value * length;
                if (value < .001) continue;
                var spoke=ReferenceEquals(ink,PrismPaint.Spectrum)?PrismPaint.Spoke(i,count):pen;
                dc.DrawLine(spoke, new(width / 2 + radius * Math.Cos(angle), height / 2 + radius * Math.Sin(angle)), new(width / 2 + outer * Math.Cos(angle), height / 2 + outer * Math.Sin(angle)));
            }
            return;
        }
        double pitch = width / count, gap = Math.Min(3, pitch * .4);
        for (int i = 0; i < count; i++)
        {
            var bandInk=ReferenceEquals(ink,PrismPaint.Spectrum)?PrismPaint.Band(i,count):ink;
            double h = Band(values, i, count) * height;
            if(style is SpectrumStyle.Bars or SpectrumStyle.Preset && !peaks.IsEmpty)
            {
                double peak=Band(peaks,i,count)*height;
                if(peak>h+2 && peak>1){dc.PushOpacity(.4);dc.DrawRectangle(bandInk,null,new(i*pitch+gap/2,Math.Max(0,height-peak),Math.Max(.5,pitch-gap),1));dc.Pop();}
            }
            if (h < .1) continue;
            double y = style is SpectrumStyle.Mirror or SpectrumStyle.Minimal ? (height - h) / 2 : height - h;
            dc.DrawRoundedRectangle(bandInk, null, new(i * pitch + gap / 2, y, Math.Max(.5, pitch - gap), h), 1, 1);
        }
    }
}
