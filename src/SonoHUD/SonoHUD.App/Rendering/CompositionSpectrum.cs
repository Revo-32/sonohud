using System.Numerics;
using Windows.UI.Composition;
using SonoHUD.Core;

namespace SonoHUD.App.Rendering;

// Retained GPU primitives. No bitmap upload or child-tree rebuild on a sample update.
internal sealed class CompositionSpectrum : IDisposable
{
    readonly SpriteVisual owner;
    readonly ContainerVisual? transitionLayer;
    public double SlotY { get; }
    public double SlotHeight => height;
    readonly SpriteVisual[] bars;
    readonly SpriteVisual[] peaks;
    readonly CompositionColorBrush[] inks;
    readonly float width, height;
    readonly SpectrumStyle style;
    internal CompositionSpectrum(Compositor compositor, SpriteVisual owner, SceneElement element, (double Y,double Height)? previousSlot=null,bool prism=false)
    {
        this.owner = owner; width = (float)element.Width; height = (float)element.Height; style = SpectrumDrawing.Style(element);
        SlotY=element.Y;
        if(previousSlot is {} previous && Math.Abs(previous.Height-height)>1 && height>0)
        {
            transitionLayer=compositor.CreateContainerVisual();
            owner.Children.InsertAtTop(transitionLayer);
            using var scale=compositor.CreateScalarKeyFrameAnimation();
            scale.Duration=TimeSpan.FromMilliseconds(240);
            scale.InsertKeyFrame(0,(float)(previous.Height/height));scale.InsertKeyFrame(1,1);
            using var translation=compositor.CreateScalarKeyFrameAnimation();
            translation.Duration=scale.Duration;
            translation.InsertKeyFrame(0,(float)(previous.Y-element.Y));translation.InsertKeyFrame(1,0);
            transitionLayer.StartAnimation("Scale.Y",scale);
            transitionLayer.StartAnimation("Offset.Y",translation);
        }
        var layer=transitionLayer??owner;
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(element.Style.Foreground);

        int count = SpectrumDrawing.Count(width, style); bars = new SpriteVisual[style == SpectrumStyle.Curve ? count - 1 : count];
        inks=new CompositionColorBrush[prism ? bars.Length : 1];
        for(int i=0;i<inks.Length;i++){
            var c=prism?PrismPaint.Band(i,inks.Length).Color:color;
            inks[i]=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(c.A,c.R,c.G,c.B));
        }
        for (int i = 0; i < bars.Length; i++) { var visual = compositor.CreateSpriteVisual(); visual.Brush = inks[prism?i:0]; layer.Children.InsertAtTop(visual); bars[i] = visual; }
        peaks=style==SpectrumStyle.Bars?new SpriteVisual[count]:[];
        for(int i=0;i<peaks.Length;i++){var mark=compositor.CreateSpriteVisual();mark.Brush=inks[prism?i:0];mark.Opacity=.4f;layer.Children.InsertAtTop(mark);peaks[i]=mark;}
    }
    internal void Update(ReadOnlySpan<float> values,ReadOnlySpan<float> held=default)
    {
        owner.IsVisible = !values.IsEmpty;
        if (values.IsEmpty) return;
        int count = bars.Length;
        for (int i = 0; i < count; i++)
        {
            var bar = bars[i]; float value = SpectrumDrawing.Band(values, i, style == SpectrumStyle.Curve ? count + 1 : count);
            if (style == SpectrumStyle.Curve)
            {
                float x = width * i / count, x2 = width * (i + 1) / count, y = height * (1 - value), y2 = height * (1 - SpectrumDrawing.Band(values, i + 1, count + 1));
                var delta = new Vector2(x2 - x, y2 - y); bar.Size = new(delta.Length() + .5f, 1.5f); bar.Offset = new(x, y, 0); bar.RotationAngle = MathF.Atan2(delta.Y, delta.X);
            }
            else if (style == SpectrumStyle.Radial)
            {
                float radius = Math.Min(width, height) * .3f, angle = i * MathF.Tau / count - MathF.PI / 2;
                bar.Size = new(value * Math.Min(width, height) * .19f, 2); bar.Offset = new(width / 2 + radius * MathF.Cos(angle), height / 2 + radius * MathF.Sin(angle), 0); bar.RotationAngle = angle;
            }
            else
            {
                float pitch = width / count, gap = Math.Min(3, pitch * .4f), h = value * height;
                bar.Size = new(Math.Max(.5f, pitch - gap), h); bar.Offset = new(i * pitch + gap / 2, style is SpectrumStyle.Mirror or SpectrumStyle.Minimal ? (height - h) / 2 : height - h, 0);
                if(peaks.Length>i){float peak=SpectrumDrawing.Band(held,i,count)*height;peaks[i].IsVisible=peak>h+2&&peak>1;peaks[i].Size=new(Math.Max(.5f,pitch-gap),1);peaks[i].Offset=new(i*pitch+gap/2,Math.Max(0,height-peak),0);}
            }
        }
    }
    public void Dispose() { foreach (var bar in bars.Concat(peaks)) { (transitionLayer??owner).Children.Remove(bar); bar.Brush = null; bar.Dispose(); } if(transitionLayer is not null){owner.Children.Remove(transitionLayer);transitionLayer.Dispose();} foreach(var ink in inks)ink.Dispose(); }
}
