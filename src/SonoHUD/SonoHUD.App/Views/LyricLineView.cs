using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SonoHUD.Core;
using SonoHUD.App.Rendering;

namespace SonoHUD.App.Views;

internal sealed class LyricLineView : FrameworkElement
{
    internal LyricLine Line {get;}
    internal bool Active {get;private set;}
    internal bool Past {get;private set;}
    internal LyricFrame Frame {get;private set;}
    FormattedText? text;
    DrawingGroup? baseDrawing, highlights;
    sealed record WordMask(LyricWord Word, Rect Bounds, RectangleGeometry Reveal)
    {
        internal double Progress = -1;
    }
    readonly List<WordMask> words = [];
    double layoutWidth=-1;
    internal LyricLineView(LyricLine line){Line=line;MinHeight=40;Margin=new(0,0,0,12);Focusable=false;}
    internal void Update(LyricFrame frame,bool active,bool past=false)
    {
        bool layoutChanged = active != Active || Past != past;
        Frame = frame; Active = active; Past = past;
        if (layoutChanged)
        {
            text = null;
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (active) UpdateMasks();
    }
    internal void RefreshColors(){text=null;InvalidateVisual();}
    void Layout(double width)
    {
        width=Math.Max(24,double.IsFinite(width)?width:640);
        if(text is not null && Math.Abs(layoutWidth-width)<.5)return;layoutWidth=width;
        text=new(Line.Text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(DesignResources.FontFamily,FontStyles.Normal,Active?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),Active?26:Past?19:22,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=width,LineHeight=Active?39:34,Trimming=TextTrimming.None};
        DrawingGroup Draw(Brush brush){var group=new DrawingGroup();text.SetForegroundBrush(brush);using(var dc=group.Open())dc.DrawText(text,new(0,0));if(group.CanFreeze)group.Freeze();return group;}
        baseDrawing = Active
            ? Draw(TryFindResource("StudioLyricsCurrent") as Brush ?? DesignResources.TextPrimary)
            : Draw(TryFindResource("StudioLyricsContext") as Brush ?? DesignResources.TextSecondary);
        words.Clear();
        highlights = null;
        if (!Active || Line.Words is not { Count: > 0 } timed) return;
        var accent = Draw(TryFindResource("StudioLyricsWord") as Brush ?? DesignResources.LyricsWord);
        highlights = new DrawingGroup();
        using var overlay = highlights.Open();
        int offset = 0;
        foreach(var word in timed)
        {
            if (word.Text.Length > 0 && offset + word.Text.Length <= Line.Text.Length &&
                text.BuildHighlightGeometry(new(0,0),offset,word.Text.Length) is { } geometry && !geometry.Bounds.IsEmpty)
            {
                geometry.Freeze();
                var reveal = new RectangleGeometry();
                words.Add(new(word, geometry.Bounds, reveal));
                overlay.PushClip(geometry);
                overlay.PushClip(reveal);
                overlay.DrawDrawing(accent);
                overlay.Pop();
                overlay.Pop();
            }
            offset+=word.Text.Length;
        }
        UpdateMasks();
    }
    // The renderer retains glyphs and word outlines. Only the timed reveal rectangle changes.
    void UpdateMasks()
    {
        foreach (var mask in words)
        {
            double progress = Frame.WordProgress(mask.Word);
            if (mask.Progress == progress) continue;
            mask.Progress = progress;
            var bounds = mask.Bounds;
            mask.Reveal.Rect = new(bounds.X, bounds.Y, bounds.Width * progress, bounds.Height);
        }
    }
    protected override Size MeasureOverride(Size availableSize){Layout(availableSize.Width);return new(double.IsFinite(availableSize.Width)?availableSize.Width:layoutWidth,Math.Max(40,text!.Height));}
    protected override void OnRender(DrawingContext drawingContext)
    {
        Layout(ActualWidth);
        if(Past)drawingContext.PushOpacity(OverlayLyricLayout.PastOpacity);
        drawingContext.DrawDrawing(baseDrawing!);
        if(Past)drawingContext.Pop();
        if(highlights is not null)drawingContext.DrawDrawing(highlights);
    }
}
