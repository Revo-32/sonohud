using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SonoHUD.Core;
using Windows.UI.Composition;
using System.Numerics;

namespace SonoHUD.App.Rendering;

// Shared stops keep WPF and the retained desktop compositor on the same palette.
internal static class PrismPaint
{
    internal static readonly Color[] Light = ColorsOf("#FF4DAD","#FF9968","#FFE683","#70FFD3","#55DDFF","#9E87FF","#FF65CB");
    static Color[] ColorsOf(params string[] values) => values.Select(v=>(Color)ColorConverter.ConvertFromString(v)).ToArray();
    internal static readonly LinearGradientBrush Spectrum = Gradient(Light,false);
    internal static readonly SolidColorBrush[] Bands = Enumerable.Range(0,64).Select(i=>
    {
        double t=i/63d*(Light.Length-1);
        int left=(int)t; var a=Light[left]; var b=Light[Math.Min(left+1,Light.Length-1)]; double f=t-left;
        byte C(byte x,byte y)=>(byte)Math.Round(x+(y-x)*f);
        var result=new SolidColorBrush(Color.FromRgb(C(a.R,b.R),C(a.G,b.G),C(a.B,b.B))); result.Freeze();return result;
    }).ToArray();
    static readonly Pen[] Spokes=Bands.Select(b=>{var p=new Pen(b,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};p.Freeze();return p;}).ToArray();
    internal static Pen Spoke(int i,int count)=>Spokes[(int)Math.Round(i*63d/Math.Max(1,count-1))];
    internal static SolidColorBrush Band(int i,int count)=>Bands[(int)Math.Round(i*63d/Math.Max(1,count-1))];
    internal static LinearGradientBrush Ribbon(bool motion)=>Gradient(Light,motion);

    static LinearGradientBrush Gradient(Color[] colors,bool motion,byte alpha=255)
    {
        var brush=new LinearGradientBrush {StartPoint=new(0,.25),EndPoint=new(1,.67),ColorInterpolationMode=ColorInterpolationMode.SRgbLinearInterpolation};
        for(int i=0;i<colors.Length;i++)
        {
            double offset=i/(double)(colors.Length-1);var color=colors[i];color.A=alpha;
            var stop=new GradientStop(color,offset);brush.GradientStops.Add(stop);
            if(motion)
            {
                var next=colors[(i+2)%colors.Length];next.A=alpha;
                stop.BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(color,next,TimeSpan.FromSeconds(4.6))
                {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
            }
            if(motion && i>0 && i<colors.Length-1)
                stop.BeginAnimation(GradientStop.OffsetProperty,new DoubleAnimation(offset,offset+Shift(i),TimeSpan.FromSeconds(3.4))
                {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
        }
        if(!motion)brush.Freeze();return brush;
    }
    static double Shift(int i)=>i%2==0?-.065:.065;

    internal static CompositionLinearGradientBrush DesktopRibbon(Compositor compositor,bool motion,byte alpha)
    {
        var colors=Light;
        var brush=compositor.CreateLinearGradientBrush();brush.StartPoint=new(0,.25f);brush.EndPoint=new(1,.67f);
        for(int i=0;i<colors.Length;i++)
        {
            float offset=i/(float)(colors.Length-1);var c=colors[i];
            var stop=compositor.CreateColorGradientStop(offset,Windows.UI.Color.FromArgb(alpha,c.R,c.G,c.B));
            brush.ColorStops.Add(stop);
            if(!motion)continue;
            using var easing=compositor.CreateCubicBezierEasingFunction(new Vector2(.37f,0),new Vector2(.63f,1));
            var next=colors[(i+2)%colors.Length];
            var firstColor=Windows.UI.Color.FromArgb(alpha,c.R,c.G,c.B);
            using var hue=compositor.CreateColorKeyFrameAnimation();
            hue.Duration=TimeSpan.FromSeconds(9.2);hue.IterationBehavior=AnimationIterationBehavior.Forever;
            hue.InsertKeyFrame(0,firstColor);hue.InsertKeyFrame(.5f,Windows.UI.Color.FromArgb(alpha,next.R,next.G,next.B),easing);
            hue.InsertKeyFrame(1,firstColor,easing);stop.StartAnimation("Color",hue);
            if(i==0 || i==colors.Length-1)continue;
            using var move=compositor.CreateScalarKeyFrameAnimation();
            move.Duration=TimeSpan.FromSeconds(6.8);move.IterationBehavior=AnimationIterationBehavior.Forever;
            move.InsertKeyFrame(0,offset);move.InsertKeyFrame(.5f,offset+(float)Shift(i),easing);move.InsertKeyFrame(1,offset,easing);
            stop.StartAnimation("Offset",move);
        }
        return brush;
    }

    internal static void ReleaseStops(CompositionLinearGradientBrush brush)
    {
        var stops=brush.ColorStops.ToArray();brush.ColorStops.Clear();
        foreach(var stop in stops) {stop.StopAnimation("Offset");stop.StopAnimation("Color");stop.Dispose();}
    }

    // A shallow fan of refracted light confined to the record sleeve's display surface.
    internal static DrawingBrush Refraction(bool motion)
    {
        var group=new DrawingGroup();
        using(var dc=group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,600,160));
            for(int i=0;i<Light.Length;i++)
            {
                var c=Light[i];c.A=(byte)(138-i*6);
                var geometry=new StreamGeometry();
                using(var g=geometry.Open()){
                    g.BeginFigure(new Point(155+i*9,175),false,false);
                    g.BezierTo(new Point(310+i*7,140-i*6),new Point(420-i*9,156-i*7),new Point(634,62+i*7),true,false);
                }
                dc.DrawGeometry(null,new Pen(new SolidColorBrush(c),1+i*.11),geometry);
                if(motion && i%2==0)
                {
                    var dashes=new DashStyle(new[]{13d,100d},i*22);
                    dashes.BeginAnimation(DashStyle.OffsetProperty,new DoubleAnimation(i*22,i*22-113,TimeSpan.FromSeconds(2.5+i*.16)){RepeatBehavior=RepeatBehavior.Forever});
                    var pulse=new Pen(new SolidColorBrush(Light[i]),2){DashStyle=dashes,DashCap=PenLineCap.Round};
                    dc.DrawGeometry(null,pulse,geometry);
                }
            }
        }
        if(group.CanFreeze)group.Freeze();
        var brush=new DrawingBrush(group){Viewbox=new Rect(0,0,600,160),ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
        if(motion){
            var shift=new TranslateTransform();brush.RelativeTransform=shift;
            shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-.025,.035,TimeSpan.FromSeconds(4.2)){
                RepeatBehavior=RepeatBehavior.Forever,AutoReverse=true,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
        }else brush.Freeze();
        return brush;
    }
}
