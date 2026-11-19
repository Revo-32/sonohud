using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SonoHUD.App.Media;
using SonoHUD.App.Views;
using SonoHUD.Core;
using Windows.UI.Composition;

namespace SonoHUD.App.Rendering;

// Texture uploads happen only at line/document/layout transitions; word progress changes clips.
internal sealed class CompositionLyrics : IDisposable
{
    readonly Compositor compositor;
    readonly D2DSurfaces surfaces;
    readonly SpriteVisual parent;
    readonly SceneElement element;
    readonly Action uploaded;
    readonly List<IDisposable> owned=[];
    sealed record WordMask(InsetClip Clip, LyricWord Word, Rect Bounds, double From, double To)
    {
        internal float Right = float.NaN;
    }
    readonly List<WordMask> masks = [];
    OverlayLyricLayout? layout;
    int displayedPage = -1;
    LyricDocument? document;
    LyricFrame lastFrame = new(null,-1,false);

    internal CompositionLyrics(Compositor compositor,D2DSurfaces surfaces,SpriteVisual parent,SceneElement element,Action uploaded)
    {this.compositor=compositor;this.surfaces=surfaces;this.parent=parent;this.element=element;this.uploaded=uploaded;}

    internal void Update(LyricDocument doc,LyricFrame frame,string status,bool playing,double? trackEnd)
    {
        var motion = LyricsMotion.Between(element.AnimationProfile, playing && ReferenceEquals(document,doc) && SystemParameters.ClientAreaAnimation, lastFrame, frame);
        lastFrame = frame;
        document = doc;
        if (layout is null || !layout.Matches(doc,frame,status,trackEnd))
        {
            layout = new(element, doc, frame, status, trackEnd);
            displayedPage = -1;
        }
        int page = layout.PageAt(frame.Position);
        if (displayedPage != page)
        {
            Clear();
            foreach (var row in layout.Rows(page)) Draw(row,layout);
            displayedPage = page;
            if (motion.Milliseconds > 0)
            {
                using var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = TimeSpan.FromMilliseconds(motion.Milliseconds);
                fade.InsertKeyFrame(0,parent.Opacity*(float)motion.StartOpacity); fade.InsertKeyFrame(1,parent.Opacity);
                using var rise = compositor.CreateScalarKeyFrameAnimation(); rise.Duration = fade.Duration;
                rise.InsertKeyFrame(0,(float)motion.Distance); rise.InsertKeyFrame(1,0);
                parent.StartAnimation("Opacity",fade); parent.StartAnimation("Offset.Y",rise);
            }
        }
        foreach (var mask in masks)
        {
            double progress = Math.Clamp((frame.WordProgress(mask.Word)-mask.From)/(mask.To-mask.From),0,1);
            float right = (float)Math.Max(0,element.Width-(mask.Bounds.Left+mask.Bounds.Width*progress));
            if (right!=mask.Right) { mask.Clip.RightInset=right; mask.Right=right; }
        }
    }
    CompositionSurfaceBrush Surface(FormattedText text,double height)
    {
        var surface = Raster(text,element.Width,height);
        var brush = compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        owned.Add(surface); owned.Add(brush);
        return brush;
    }
    SpriteVisual Sprite(LyricVisualRow row, CompositionSurfaceBrush brush)
    {
        var visual = compositor.CreateSpriteVisual();
        visual.Offset=new(0,(float)row.Top,0); visual.Size=new((float)element.Width,(float)row.Height); visual.Brush=brush;
        parent.Children.InsertAtTop(visual); owned.Add(visual);
        return visual;
    }
    void Draw(LyricVisualRow row,OverlayLyricLayout layout)
    {
        if (string.IsNullOrWhiteSpace(row.Text)) return;
        var text = layout.Text(row);
        var visual = Sprite(row, Surface(text,row.Height));
        if (row.Role == LyricRole.Previous) visual.Opacity = (float)OverlayLyricLayout.PastOpacity;
        if (row.Role != LyricRole.Current || layout.Line.Words is not {Count: >0} words) return;
        int offset = 0, rowEnd = row.Start + text.Text.Length;
        CompositionSurfaceBrush? accent = null;
        foreach (var word in words)
        {
            int start = Math.Max(row.Start,offset), end = Math.Min(rowEnd,offset+word.Text.Length);
            int wordStart = offset; offset += word.Text.Length;
            if (end <= start) continue;
            var geometry = text.BuildHighlightGeometry(new(0,0), start-row.Start, end-start);
            if (geometry is null) continue;
            var bounds = geometry.Bounds; bounds.Intersect(new Rect(0,0,element.Width,row.Height));
            if (bounds.IsEmpty) continue;
            accent ??= Surface(layout.Text(row,ElementDrawing.Brush(element.Style.Background)),row.Height);
            var part = Sprite(row,accent);
            var clip = compositor.CreateInsetClip((float)bounds.Left,(float)bounds.Top,(float)(element.Width-bounds.Left),(float)Math.Max(0,row.Height-bounds.Bottom));
            part.Clip=clip; owned.Add(clip);
            masks.Add(new(clip,word,bounds,(start-wordStart)/(double)word.Text.Length,(end-wordStart)/(double)word.Text.Length));
        }
    }
    CompositionDrawingSurface Raster(FormattedText text,double width,double height)
    {
        const double scale=2;var dv=new DrawingVisual();using(var dc=dv.RenderOpen()){dc.PushTransform(new ScaleTransform(scale,scale));dc.DrawText(text,new(0,0));dc.Pop();}
        int w=Math.Max(1,(int)Math.Ceiling(width*scale)),h=Math.Max(1,(int)Math.Ceiling(height*scale));var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(dv);byte[] pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);uploaded();return surfaces.Upload(pixels,w,h);
    }
    void Clear(){parent.Children.RemoveAll();foreach(var item in owned.AsEnumerable().Reverse())item.Dispose();owned.Clear();masks.Clear();}
    public void Dispose()=>Clear();
}
