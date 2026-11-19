using System.Windows;
using System.Windows.Media;
using SonoHUD.App.Media;
using SonoHUD.App.Rendering;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

// A presentation-only view of the same scene content used by the desktop renderer.
public sealed class OverlayPreview : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(Scene), typeof(OverlayPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PreserveUserScaleProperty = DependencyProperty.Register(
        nameof(PreserveUserScale), typeof(bool), typeof(OverlayPreview), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Scene? Scene { get => (Scene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public bool PreserveUserScale { get => (bool)GetValue(PreserveUserScaleProperty); set => SetValue(PreserveUserScaleProperty, value); }
    PlaybackState playback = new();
    readonly float[] spectrum=new float[96],spectrumPeaks=new float[96];
    int peakLength;
    int spectrumLength;
    readonly Dictionary<Guid,(SceneElement Element,DrawingGroup Drawing,Brush Ink)> meterLayers=[];
    internal void SetSpectrum(ReadOnlySpan<float> values,ReadOnlySpan<float> peaks=default)
    {
        spectrumLength=Math.Min(values.Length,spectrum.Length);values[..spectrumLength].CopyTo(spectrum);
        peakLength=Math.Min(peaks.Length,spectrumPeaks.Length);peaks[..peakLength].CopyTo(spectrumPeaks);
        // Updating retained meter drawings does not recreate artwork, text or marquee layouts.
        foreach(var layer in meterLayers.Values)
        {using var context=layer.Drawing.Open();SpectrumDrawing.Draw(context,layer.Element.Width,layer.Element.Height,spectrum.AsSpan(0,spectrumLength),layer.Ink,SpectrumDrawing.Style(layer.Element),0,spectrumPeaks.AsSpan(0,peakLength));}
    }
    sealed record ScrollingTitle(string Key, TitleMarqueeLayout Layout, DrawingGroup? Drawing, TranslateTransform? Translation)
    {
        internal bool Running;
        internal void Stop() { if(Translation is not null) { Translation.BeginAnimation(TranslateTransform.XProperty, null); Translation.X=0; } Running=false; }
    }
    readonly Dictionary<Guid, ScrollingTitle> scrollingTitles = [];
    double? snapshotSeconds;

    public OverlayPreview()
    {
        ClipToBounds = true; Focusable = false; IsHitTestVisible = false;
        Loaded += (_,_) => { SystemParameters.StaticPropertyChanged+=MotionPreferenceChanged; InvalidateVisual(); };
        Unloaded += (_,_) => { SystemParameters.StaticPropertyChanged-=MotionPreferenceChanged; StopTitles(); };
        IsVisibleChanged += (_,_) => { if(!IsVisible)StopTitles();else InvalidateVisual(); };
    }
    void MotionPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)){StopTitles();InvalidateVisual();} }
    void StopTitles() { foreach(var title in scrollingTitles.Values)title.Stop(); }
    // Deterministic offscreen evidence, never an input or desktop automation path.
    internal void SetMarqueeSnapshot(double? seconds) { snapshotSeconds=seconds;StopTitles();InvalidateVisual(); }

    internal void SetContent(Scene? scene, PlaybackState state)
    {
        if(scene is null) { StopTitles();scrollingTitles.Clear();meterLayers.Clear(); }
        if (lyricLayout is not null && (scene is null || !scene.Elements.Any(e=>e.Type==ElementKind.LyricsText)))
        { lyricLayout=null; lyricElement=null; }
        Scene = scene;
        playback = state;
        InvalidateVisual();
    }

    OverlayLyricLayout? lyricLayout;
    SceneElement? lyricElement;
    int lyricPage;
    internal void RefreshLyricFrame(LyricDocument document,LyricFrame frame)
    {
        if (!IsVisible || lyricLayout is null || document.Lines.Count==0) return;
        if (lyricLayout.Matches(document,frame,"",playback.DurationSeconds) && lyricLayout.PageAt(frame.Position) == lyricPage) return;
        playback = playback with { PreviewLyrics=document, PreviewLyricFrame=frame };
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Scene is not { } scene || ActualWidth <= 0 || ActualHeight <= 0) return;
        var liveTitles=new HashSet<Guid>();
        var liveMeters=new HashSet<Guid>();
        var bounds = DesktopLayout.Content(scene);
        double fitWidth = bounds.Width, fitHeight = bounds.Height;
        if (PreserveUserScale)
        {
            // Keep a fixed frame while the user's scale changes; otherwise fitting would cancel it.
            if (Enum.TryParse<PresetKind>(scene.UserSettings.GetValueOrDefault("preset"), out var preset) && Enum.IsDefined(preset))
            {
                var dimensions = OverlayPresets.Dimensions(preset);
                fitWidth = dimensions.Width * 1.5;
                fitHeight = dimensions.Height * 1.5;
            }
            else { fitWidth = 840; fitHeight = 372; }
        }
        // Compact gallery cards already have outer padding; keep their shapes readable.
        double padding = ActualHeight < 100 ? 2 : 12;
        double scale = Math.Max(.001, Math.Min(Math.Max(1, ActualWidth - padding * 2) / fitWidth, Math.Max(1, ActualHeight - padding * 2) / fitHeight));
        double x = (ActualWidth - bounds.Width * scale) / 2 - bounds.X * scale;
        double y = (ActualHeight - bounds.Height * scale) / 2 - bounds.Y * scale;
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        bool clipped = PresetClip.TryCreate(scene, 1, System.Numerics.Vector2.Zero, out var clip);
        if (clipped) dc.PushClip(clip.WpfGeometry());
        foreach (var frame in SceneMath.Flatten(scene))
        {
            if (!frame.Visible || frame.Element.Type == ElementKind.Group) continue;
            var m = frame.Transform;
            dc.PushTransform(new MatrixTransform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32));
            dc.PushOpacity(frame.Opacity);
            var element=frame.Element;
            bool motionAllowed=element.Marquee && scene.AnimationProfile!=AnimationProfile.Off && element.AnimationProfile!=AnimationProfile.Off && SystemParameters.ClientAreaAnimation;
            if(element.Type is ElementKind.AudioLevel or ElementKind.Spectrum)
            {
                liveMeters.Add(element.Id);var drawing=new DrawingGroup();Brush ink=scene.UserSettings.ContainsKey("spectrumGradient")?PrismPaint.Spectrum:ElementDrawing.Brush(element.Style.Foreground);
                using(var context=drawing.Open())SpectrumDrawing.Draw(context,element.Width,element.Height,spectrum.AsSpan(0,spectrumLength),ink,SpectrumDrawing.Style(element),0,spectrumPeaks.AsSpan(0,peakLength));
                meterLayers[element.Id]=(element,drawing,ink);dc.DrawDrawing(drawing);
            }
            else if(element.Type==ElementKind.BackgroundPanel && scene.UserSettings.GetValueOrDefault("appearance")==MusicTheme.PrismName)
            {
                ElementDrawing.Draw(dc,element,playback);
                var alpha=((Color)ColorConverter.ConvertFromString(element.Style.Background)).A;
                dc.PushOpacity(alpha/255d);
                dc.DrawRectangle(PrismPaint.Spectrum,null,new Rect(0,0,element.Width,2));
                dc.Pop();
            }
            else if (element.Type == ElementKind.LyricsText && playback.PreviewLyrics is { } lyrics)
            {
                var frameTime = playback.PreviewLyricFrame;
                if (lyricLayout is null || !Equals(lyricElement,element) || !lyricLayout.Matches(lyrics,frameTime,"",playback.DurationSeconds))
                {
                    lyricElement=element;
                    lyricLayout=new(element,lyrics,frameTime,"",playback.DurationSeconds);
                }
                lyricPage=lyricLayout.PageAt(frameTime.Position);
                foreach (var row in lyricLayout.Rows(lyricPage))
                {
                    if (row.Role == LyricRole.Previous) dc.PushOpacity(OverlayLyricLayout.PastOpacity);
                    dc.DrawText(lyricLayout.Text(row),new(0,row.Top));
                    if (row.Role == LyricRole.Previous) dc.Pop();
                }
            }
            else if(motionAllowed)
            {
                string key=ElementDrawing.ContentKey(element,playback);
                liveTitles.Add(element.Id);
                if(!scrollingTitles.TryGetValue(element.Id,out var title) || title.Key!=key)
                {
                    title?.Stop();
                    var layout=TitleMarquee.Measure(element,playback);
                    var drawing=layout.CanAnimate?TitleMarquee.CreateDrawing(element,layout):default;
                    title=new(key,layout,drawing.Drawing,drawing.Translation);
                    scrollingTitles[element.Id]=title;
                }
                if(title.Layout.CanAnimate)
                {
                    if(snapshotSeconds is double seconds)TitleMarquee.Draw(dc,element,playback,title.Layout,seconds);
                    else
                    {
                        if(IsLoaded && IsVisible && !title.Running) { TitleMarquee.Start(title.Translation!,title.Layout);title.Running=true; }
                        dc.DrawDrawing(title.Drawing!);
                    }
                }
                else ElementDrawing.Draw(dc,element,playback);
            }
            else ElementDrawing.Draw(dc,element,playback);
            dc.Pop();
            dc.Pop();
        }
        if (clipped) dc.Pop();
        dc.Pop();
        dc.Pop();
        foreach(var id in scrollingTitles.Keys.Where(id=>!liveTitles.Contains(id)).ToArray()) { scrollingTitles[id].Stop();scrollingTitles.Remove(id); }
        foreach(var id in meterLayers.Keys.Where(id=>!liveMeters.Contains(id)).ToArray())meterLayers.Remove(id);
    }
}
