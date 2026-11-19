using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SonoHUD.App.Media;
using SonoHUD.Core;

namespace SonoHUD.App.Rendering;

internal sealed record TitleMarqueeLayout(string Text, double ContentWidth, double ViewportWidth, double Height, bool Enabled)
{
    internal double Travel => Math.Max(0, ContentWidth - ViewportWidth);
    internal bool CanAnimate => Enabled && Travel > 1 && ContentWidth <= 8192 && Height <= 512;
    internal double MoveSeconds => Math.Max(1, Travel / 28);
    internal double DurationSeconds => 4 + 2 * MoveSeconds;
    internal (double Seconds, double Offset)[] Keyframes => [(0, 0), (2, 0), (2 + MoveSeconds, -Travel), (4 + MoveSeconds, -Travel), (DurationSeconds, 0)];
    internal double OffsetAt(double seconds)
    {
        if (!CanAnimate || !double.IsFinite(seconds) || seconds <= 0) return 0;
        double t = seconds % DurationSeconds;
        if (t <= 2) return 0;
        if (t < 2 + MoveSeconds) return -Travel * (t - 2) / MoveSeconds;
        if (t <= 4 + MoveSeconds) return -Travel;
        return -Travel * (1 - (t - 4 - MoveSeconds) / MoveSeconds);
    }
}

// One measured line and one timeline for the WPF preview and Composition overlay.
// Moving a cached drawing/surface needs no timer-driven text rasterization or texture uploads.
internal static class TitleMarquee
{
    internal static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    static FormattedText Format(SceneElement element, string text) => new(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
        new Typeface(ElementDrawing.ResolveFontFamily(element.Style.FontFamily), FontStyles.Normal, FontWeight.FromOpenTypeWeight(element.Style.FontWeight), FontStretches.Normal),
        element.Style.FontSize, ElementDrawing.Brush(element.Style.Foreground), 1)
        { MaxLineCount = 1, LineHeight = element.Style.FontSize * 1.25 };

    internal static TitleMarqueeLayout Measure(SceneElement element, PlaybackState state)
    {
        string text = SingleLine(ElementDrawing.Text(element, state));
        var formatted = Format(element, text);
        double width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + Math.Max(0, formatted.OverhangTrailing) + 1);
        if(width>element.Width)width+=20; // Clear ink at both ends of the alpha fade.
        return new(text, width, element.Width, element.Height, element.Marquee && element.Type == ElementKind.TrackTitle);
    }

    internal static void Draw(DrawingContext dc, SceneElement element, PlaybackState state, TitleMarqueeLayout layout, double seconds)
    {
        if (!layout.CanAnimate) { ElementDrawing.Draw(dc, element, state); return; }
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, element.Width, element.Height)));
        dc.PushOpacityMask(EdgeMask(element.Width));
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,element.Width,element.Height));
        dc.PushTransform(new TranslateTransform(layout.OffsetAt(seconds), 0));
        dc.DrawText(Format(element, layout.Text), new Point(10,0));
        dc.Pop(); dc.Pop(); dc.Pop();
    }

    internal static (DrawingGroup Drawing, TranslateTransform Translation) CreateDrawing(SceneElement element, TitleMarqueeLayout layout)
    {
        var translation = new TranslateTransform();
        var text = new DrawingGroup { Transform = translation };
        using (var dc = text.Open()) dc.DrawText(Format(element, layout.Text), new Point(10,0));
        var clipped = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, element.Width, element.Height)),OpacityMask=EdgeMask(element.Width) };
        clipped.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,element.Width,element.Height))));
        clipped.Children.Add(text);
        return (clipped, translation);
    }
    internal static Brush EdgeMask(double width)
    {
        double edge=Math.Min(.12,10/Math.Max(1,width));
        var brush=new LinearGradientBrush { MappingMode=BrushMappingMode.Absolute,StartPoint=new(0,0),EndPoint=new(width,0),GradientStops=new(){new(Colors.Transparent,0),new(Colors.White,edge),new(Colors.White,1-edge),new(Colors.Transparent,1)} };
        brush.Freeze();return brush;
    }

    internal static AnimationClock Start(TranslateTransform translation, TitleMarqueeLayout layout)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(layout.DurationSeconds), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var frame in layout.Keyframes) animation.KeyFrames.Add(new LinearDoubleKeyFrame(frame.Offset, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(frame.Seconds))));
        var clock=animation.CreateClock();
        translation.ApplyAnimationClock(TranslateTransform.XProperty, clock, HandoffBehavior.SnapshotAndReplace);
        return clock;
    }
}
