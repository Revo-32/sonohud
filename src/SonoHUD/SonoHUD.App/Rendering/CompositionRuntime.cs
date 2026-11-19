using System.Numerics;
using System.Runtime.InteropServices;
using SonoHUD.Core;
using SonoHUD.App.Platform;
using SonoHUD.App.Media;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace SonoHUD.App.Rendering;

internal sealed class CompositionRuntime
{
    readonly Compositor compositor;
    readonly D2DSurfaces surfaces;
    readonly Windows.System.DispatcherQueueController? controller;
    readonly Dictionary<Guid, SceneWindow> windows = [];
    readonly AppLog log;
    bool stopped;
    readonly float[] spectrum = new float[96],spectrumPeaks=new float[96];
    int peakLength;
    int spectrumLength;
    internal void UpdateSpectrum(ReadOnlySpan<float> values,ReadOnlySpan<float> peaks=default) { spectrumLength=Math.Min(values.Length,spectrum.Length);values[..spectrumLength].CopyTo(spectrum);peakLength=Math.Min(peaks.Length,spectrumPeaks.Length);peaks[..peakLength].CopyTo(spectrumPeaks);if(!stopped)foreach(var window in windows.Values)window.UpdateSpectrum(spectrum.AsSpan(0,spectrumLength),spectrumPeaks.AsSpan(0,peakLength)); }
    internal event Action<string>? CommandRequested;
    LyricDocument lyricsDocument=LyricDocument.Empty;
    LyricFrame lyricsFrame=new(null,-1,false);
    string lyricsMessage="가사를 기다립니다";
    internal void UpdateLyrics(LyricDocument document,LyricFrame frame,string message){lyricsDocument=document;lyricsFrame=frame;lyricsMessage=message;if(!stopped)foreach(var window in windows.Values)window.UpdateLyrics(document,frame,message);}
    internal int WindowCount => windows.Count;
    internal int NodeCount => windows.Values.Sum(w => w.NodeCount);
    internal long UploadCount { get; private set; }
    internal CompositionRuntime(AppLog log)
    {
        this.log = log;
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new() { Size = 12, ThreadType = 2, ApartmentType = 2 }, out var abi));
            try { controller = MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(abi); } finally { Marshal.Release(abi); }
        }
        compositor = new(); surfaces = new(compositor);
    }
    internal void Update(SceneDocument document, PlaybackState playback, Guid? editingScene = null, bool suspended = false)
    {
        if (stopped) return; var live = new HashSet<Guid>(); var monitors = Native.Monitors();
        foreach (var s in document.Scenes.OrderBy(s => s.ZOrder))
        {
            bool show = (s.Active && s.Visible && !suspended) || s.Id == editingScene;
            if (!show) { if (windows.TryGetValue(s.Id, out var hidden)) hidden.Hide(); continue; }
            live.Add(s.Id);
            if (!windows.TryGetValue(s.Id, out var window)) { window = new(compositor, surfaces, log, s.Id, () => UploadCount++, c => CommandRequested?.Invoke(c)); windows[s.Id] = window; }
            var monitor = monitors.FirstOrDefault(m => m.Device == s.MonitorId) ?? monitors.FirstOrDefault(m => !m.Primary) ?? monitors.First();
            window.Apply(s, playback, monitor, s.Id == editingScene, editingScene.HasValue);
            window.UpdateLyrics(lyricsDocument,lyricsFrame,lyricsMessage);
            window.UpdateSpectrum(spectrum.AsSpan(0,spectrumLength),spectrumPeaks.AsSpan(0,peakLength));
        }
        foreach (var id in windows.Keys.Where(id => document.Scenes.All(s => s.Id != id)).ToArray()) { windows[id].Dispose(); windows.Remove(id); }
    }
    internal object Snapshot() => windows.Select(p => new { id = p.Key, hwnd = p.Value.Handle.ToInt64(), nodes = p.Value.NodeCount, visible = Native.IsWindowVisible(p.Value.Handle), styles = Native.GetWindowLongPtrW(p.Value.Handle, -20).ToInt64(), p.Value.Bounds, p.Value.MotionStarts, p.Value.MotionProfile }).ToArray();
    internal void ReplayMotion() { if(!stopped)foreach(var window in windows.Values)window.ReplayMotion(); }
    internal async Task ShutdownAsync()
    {
        if (stopped) return; stopped = true; foreach (var w in windows.Values) w.Dispose(); windows.Clear(); surfaces.Dispose(); compositor.Dispose();
        if (controller is not null) await controller.ShutdownQueueAsync(); log.Write("composition_stopped", new { windows = windows.Count });
    }
    [StructLayout(LayoutKind.Sequential)] struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
}
internal sealed class SceneWindow : IDisposable
{
    static readonly Dictionary<IntPtr, SceneWindow> routes = [];
    static readonly Native.WndProc wndProc = Route;
    static bool registered;
    const string ClassName = "SonoHUD.M4.Overlay";
    readonly Compositor compositor; readonly D2DSurfaces surfaces; readonly DesktopWindowTarget target; readonly ContainerVisual root; readonly AppLog log; readonly Action uploaded; readonly Action<string> command;
    readonly InsetClip insetClip;
    CompositionRoundedRectangleGeometry? presetGeometry;
    CompositionGeometricClip? presetClip;
    readonly Dictionary<Guid, Node> nodes = []; string order = ""; Scene? scene; PlaybackState? playback; bool clickThrough = true; double dpiScale = 1; Vector2 sceneOffset;
    AnimationProfile? appliedProfile; string appliedPreset = "";
    string metadataIdentity = "";
    System.Windows.Threading.DispatcherTimer? hideTimer;
    bool hiding, disposed, presenting;
    long motionEndsAt;
    internal IntPtr Handle { get; }
    internal Native.Rect Bounds { get; private set; }
    internal int NodeCount => nodes.Count;
    internal long MotionStarts { get; private set; }
    internal string MotionProfile => appliedProfile?.ToString() ?? "Uninitialized";
    sealed class Node : IDisposable
    {
        internal SpriteVisual Visual = null!; internal CompositionDrawingSurface? Surface; internal string Key = ""; internal Matrix4x4 Matrix; internal float Opacity; internal bool Initialized;
        internal SpriteVisual? Scroller; internal InsetClip? TextClip; internal TitleMarqueeLayout? Marquee; internal bool MarqueeRunning;
        internal ContainerVisual? TextHost;
        internal CompositionVisualSurface? TextSurface;
        internal CompositionSurfaceBrush? TextSurfaceBrush;
        internal CompositionLinearGradientBrush? TextMask;
        internal bool FrameVisible;
        internal SpriteVisual? PrismEdge;
        internal CompositionSpectrum? Spectrum;
        internal CompositionLyrics? Lyrics;
        internal void StopMarquee() { Scroller?.StopAnimation("Offset.X"); if(Scroller is not null)Scroller.Offset=Vector3.Zero;MarqueeRunning=false; }
        internal void ClearContent()
        {
            StopMarquee();
            Spectrum?.Dispose();Spectrum=null;
            Lyrics?.Dispose();Lyrics=null;
            if(PrismEdge is {} edge)
            {
                Visual.Children.Remove(edge);var light=(CompositionLinearGradientBrush)edge.Brush;
                edge.Brush=null; PrismPaint.ReleaseStops(light);light.Dispose();edge.Dispose();PrismEdge=null;
            }
            var brush=Visual.Brush;Visual.Brush=null;brush?.Dispose();
            if(Scroller is not null) { if(TextHost is not null)TextHost.Children.Remove(Scroller);else Visual.Children.Remove(Scroller);var textBrush=Scroller.Brush;Scroller.Brush=null;textBrush?.Dispose();Scroller.Dispose();Scroller=null; }
            TextSurfaceBrush?.Dispose();TextSurfaceBrush=null;TextMask?.Dispose();TextMask=null;TextSurface?.Dispose();TextSurface=null;TextHost?.Dispose();TextHost=null;
            Visual.Clip=null;TextClip?.Dispose();TextClip=null;Surface?.Dispose();Surface=null;Marquee=null;
        }
        public void Dispose() { Visual.StopAnimation("Opacity"); Visual.StopAnimation("Offset");ClearContent();Visual.Dispose(); }
    }
    internal SceneWindow(Compositor compositor, D2DSurfaces surfaces, AppLog log, Guid id, Action uploaded, Action<string> command)
    {
        this.compositor = compositor; this.surfaces = surfaces; this.log = log; this.uploaded = uploaded; this.command = command;
        if (!registered) { var wc = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Proc = wndProc, Instance = Native.GetModuleHandleW(null), Name = ClassName }; if (Native.RegisterClassExW(ref wc) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); registered = true; }
        Handle = Native.CreateWindowExW(Native.NoActivate | Native.ToolWindow | Native.Transparent | Native.Layered | Native.Topmost | Native.NoRedirection, ClassName, "SonoHUD · Scene", 0x80000000, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); routes[Handle] = this;
        Native.SetLayeredWindowAttributes(Handle, 0, 255, 2); compositor.As<IDesktopInterop>().CreateDesktopWindowTarget(Handle, false, out var abi);
        try { target = MarshalInterface<DesktopWindowTarget>.FromAbi(abi); } finally { Marshal.Release(abi); }
        root = compositor.CreateContainerVisual(); insetClip = compositor.CreateInsetClip(); root.Clip = insetClip; target.Root = root; log.Write("overlay_created", new { id, hwnd = Handle.ToInt64() });
    }
    internal void Apply(Scene value, PlaybackState playback, Native.Monitor monitor, bool editing, bool preserveZ)
    {
        bool wasHiding=hiding; CancelHide();
        string identity=$"{playback.Source}|{playback.IsDemo}|{playback.Title}|{playback.Artist}|{playback.AlbumTitle}|{playback.ArtworkKey}";
        bool metadataChanged=metadataIdentity.Length>0 && metadataIdentity!=identity;
        metadataIdentity=identity;
        scene = value; this.playback = playback; var monitors = Native.DesktopMonitors(); dpiScale = DesktopLayout.Resolve(value, monitors).Scale;
        var origin = DesktopLayout.VisibleOrigin(value, monitors); var bounds = DesktopLayout.WindowBounds(value, monitors);
        int x = (int)bounds.X, y = (int)bounds.Y, width = (int)bounds.Width, height = (int)bounds.Height;
        sceneOffset = new((float)(origin.X - x), (float)(origin.Y - y));
        Bounds = new() { Left = x, Top = y, Right = x + width, Bottom = y + height };
        bool wasVisible = Native.IsWindowVisible(Handle); clickThrough = value.ClickThrough || editing;
        long ex = Native.GetWindowLongPtrW(Handle, -20).ToInt64(); long wanted = clickThrough ? ex | Native.Transparent : ex & ~Native.Transparent;
        if (wanted != ex) Native.SetWindowLongPtrW(Handle, -20, new(wanted));
        presenting=Native.SetWindowPos(Handle, new(-1), x, y, width, height, 0x10 | 0x40 | (preserveZ && wasVisible ? 4u : 0)) || wasVisible;
        root.Size = new(width, height);
        ApplyClip(value);
        var frame = SceneMath.Flatten(value).Where(f => f.Element.Type != ElementKind.Group).ToArray(); var alive = frame.Select(f => f.Element.Id).ToHashSet();
        foreach (var f in frame)
        {
            var e = f.Element;
            if (!nodes.TryGetValue(e.Id, out var n)) { n = new() { Visual = compositor.CreateSpriteVisual() }; nodes[e.Id] = n; root.Children.InsertAtTop(n.Visual); }
            n.FrameVisible=f.Visible; n.Visual.IsVisible = f.Visible; if (!f.Visible) { n.StopMarquee();continue; }
            double physicalScale=Math.Sqrt(f.Transform.M11*f.Transform.M11+f.Transform.M12*f.Transform.M12)*dpiScale;
            double rasterScale=Math.Clamp(Math.Ceiling(physicalScale*4)/4,1,3);
            bool scrollAllowed=e.Marquee && !editing && value.AnimationProfile!=AnimationProfile.Off && e.AnimationProfile!=AnimationProfile.Off && System.Windows.SystemParameters.ClientAreaAnimation;
            string key = ElementDrawing.ContentKey(e, playback)+$"|raster:{rasterScale}|scroll:{scrollAllowed}|motion:{(e.Type==ElementKind.LyricsText?e.AnimationProfile:AnimationProfile.Off)}";
            bool prismField=e.Type==ElementKind.BackgroundPanel && value.UserSettings.GetValueOrDefault("appearance")==MusicTheme.PrismName;
            bool prismBands=e.Type==ElementKind.Spectrum && value.UserSettings.ContainsKey("spectrumGradient");
            bool prismMotion=prismField && !editing && value.AnimationProfile!=AnimationProfile.Off && System.Windows.SystemParameters.ClientAreaAnimation;
            if(prismField || prismBands)key+=$"|prism:{prismField}:{prismBands}:{prismMotion}";
            if (n.Key != key)
            {
                if(prismField)
                {
                    n.ClearContent();
                    var surface=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(e.Style.Background);
                    n.Visual.Brush=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(surface.A,surface.R,surface.G,surface.B));
                    var edge=compositor.CreateSpriteVisual();edge.Size=new((float)e.Width,2);
                    edge.Brush=PrismPaint.DesktopRibbon(compositor,prismMotion,surface.A);
                    n.Visual.Children.InsertAtTop(edge);n.PrismEdge=edge;n.Key=key;
                }
                else if(e.Type==ElementKind.LyricsText)
                {
                    n.ClearContent();n.Lyrics=new(compositor,surfaces,n.Visual,e,uploaded);n.Key=key;
                }
                else if(e.Type is ElementKind.AudioLevel or ElementKind.Spectrum)
                {
                    (double Y,double Height)? previous=null;
                    if(!editing && e.AnimationProfile!=AnimationProfile.Off && System.Windows.SystemParameters.ClientAreaAnimation &&
                        appliedPreset==nameof(PresetKind.LyricsFocus) && value.Name==appliedPreset && n.Spectrum is {} old)
                        previous=(old.SlotY,old.SlotHeight);
                    n.ClearContent(); n.Spectrum=new(compositor,n.Visual,e,previous,prismBands);
                    n.Key=key;
                }
                else
                {
                var layout=scrollAllowed?TitleMarquee.Measure(e,playback):null;
                bool scrolling=layout?.CanAnimate==true;
                var rasterElement=scrolling ? e with {Width=layout!.ContentWidth-20} : e;
                var image = ElementDrawing.Raster(rasterElement, playback, out int rw, out int rh,rasterScale,scrolling?8192:2048);
                var surface = surfaces.Upload(image, rw, rh); var brush = compositor.CreateSurfaceBrush(surface); brush.Stretch = CompositionStretch.Fill;
                n.ClearContent();n.Surface=surface;
                if(scrolling)
                {
                    n.TextClip=compositor.CreateInsetClip();n.Visual.Clip=n.TextClip;
                    n.Scroller=compositor.CreateSpriteVisual();n.Scroller.Size=new((float)layout!.ContentWidth-20,(float)e.Height);n.Scroller.Brush=brush;n.Scroller.Offset=new(10,0,0);
                    n.TextHost=compositor.CreateContainerVisual();n.TextHost.Size=new((float)e.Width,(float)e.Height);n.TextHost.Children.InsertAtTop(n.Scroller);
                    n.TextSurface=compositor.CreateVisualSurface();n.TextSurface.SourceVisual=n.TextHost;n.TextSurface.SourceSize=n.TextHost.Size;
                    n.TextSurfaceBrush=compositor.CreateSurfaceBrush(n.TextSurface);n.TextSurfaceBrush.Stretch=CompositionStretch.Fill;
                    n.TextMask=compositor.CreateLinearGradientBrush();n.TextMask.StartPoint=Vector2.Zero;n.TextMask.EndPoint=new(1,0);
                    float edge=Math.Min(.12f,10/(float)e.Width);var clear=Windows.UI.Color.FromArgb(0,255,255,255);var opaque=Windows.UI.Color.FromArgb(255,255,255,255);
                    n.TextMask.ColorStops.Add(compositor.CreateColorGradientStop(0,clear));n.TextMask.ColorStops.Add(compositor.CreateColorGradientStop(edge,opaque));n.TextMask.ColorStops.Add(compositor.CreateColorGradientStop(1-edge,opaque));n.TextMask.ColorStops.Add(compositor.CreateColorGradientStop(1,clear));
                    var mask=compositor.CreateMaskBrush();mask.Source=n.TextSurfaceBrush;mask.Mask=n.TextMask;n.Visual.Brush=mask;n.Marquee=layout;
                }
                else n.Visual.Brush=brush;
                n.Key = key; uploaded();
                }
            }
            var m = f.Transform * Matrix3x2.CreateScale((float)dpiScale) * Matrix3x2.CreateTranslation(sceneOffset); var matrix = new Matrix4x4(m.M11, m.M12, 0, 0, m.M21, m.M22, 0, 0, 0, 0, 1, 0, m.M31, m.M32, 0, 1);
            if (!n.Initialized || matrix != n.Matrix) { n.Visual.TransformMatrix = matrix; n.Matrix = matrix; }
            n.Visual.Size = new((float)e.Width, (float)e.Height);
            if(n.Marquee is not null && !n.MarqueeRunning)StartMarquee(n);
            float opacity = (float)f.Opacity;
            if (!n.Initialized || Math.Abs(n.Opacity - opacity) > .0001) { n.Visual.StopAnimation("Opacity"); n.Visual.Opacity = opacity; n.Opacity = opacity; }
            n.Initialized = true;
        }
        foreach (var id in nodes.Keys.Where(id => !alive.Contains(id)).ToArray()) { root.Children.Remove(nodes[id].Visual); nodes[id].Dispose(); nodes.Remove(id); }
        string next = string.Join('|', frame.Select(f => f.Element.Id)); if (order != next) { root.Children.RemoveAll(); foreach (var f in frame) root.Children.InsertAtTop(nodes[f.Element.Id].Visual); order = next; }
        string preset = value.UserSettings.GetValueOrDefault("preset", "");
        bool motionChanged = appliedProfile != value.AnimationProfile || appliedPreset != preset;
        if (editing || value.AnimationProfile == AnimationProfile.Off || !System.Windows.SystemParameters.ClientAreaAnimation) ResetMotion();
        else if (!wasVisible || wasHiding || motionChanged) PlayMotion(value, frame, !wasVisible || wasHiding ? "shown" : "selection_changed");
        else if(metadataChanged)PlayMotion(value,frame,"metadata_changed",true);
        appliedProfile = value.AnimationProfile;
        appliedPreset = preset;
    }
    internal void UpdateSpectrum(ReadOnlySpan<float> values,ReadOnlySpan<float> peaks=default)
    {
        if(!presenting)return;
        foreach(var node in nodes.Values)
        {
            if(node.Spectrum is null)continue;
            node.Spectrum.Update(node.FrameVisible?values:[],peaks);
        }
    }
    internal void UpdateLyrics(LyricDocument document,LyricFrame frame,string message)
    { if(presenting)foreach(var node in nodes.Values)if(node.FrameVisible)node.Lyrics?.Update(document,frame,message,playback?.IsPlaying==true,playback?.DurationSeconds); }
    void StartMarquee(Node node)
    {
        var layout=node.Marquee!;
        using var animation=compositor.CreateScalarKeyFrameAnimation();
        using var linear=compositor.CreateLinearEasingFunction();
        animation.Duration=TimeSpan.FromSeconds(layout.DurationSeconds);animation.IterationBehavior=AnimationIterationBehavior.Forever;
        foreach(var frame in layout.Keyframes)animation.InsertKeyFrame((float)(frame.Seconds/layout.DurationSeconds),(float)frame.Offset+10,linear);
        node.Scroller!.StartAnimation("Offset.X",animation);node.MarqueeRunning=true;
    }
    void ApplyClip(Scene value)
    {
        if (!PresetClip.TryCreate(value, dpiScale, sceneOffset, out var layout))
        {
            root.Clip = insetClip;
            return;
        }
        if (presetGeometry is null)
        {
            presetGeometry = compositor.CreateRoundedRectangleGeometry();
            presetClip = compositor.CreateGeometricClip(presetGeometry);
        }
        presetGeometry.Size = layout.Size;
        presetGeometry.CornerRadius = new(layout.Radius);
        presetClip!.TransformMatrix = layout.Transform;
        root.Clip = presetClip;
    }
    void ResetMotion()
    {
        motionEndsAt=0;
        root.StopAnimation("Opacity"); root.StopAnimation("Offset"); root.StopAnimation("Scale");
        root.Opacity = 1; root.Offset = Vector3.Zero; root.Scale = Vector3.One;
        foreach (var node in nodes.Values) { node.Visual.StopAnimation("Opacity"); node.Visual.StopAnimation("Offset"); node.Visual.Opacity = node.Opacity; node.Visual.Offset = Vector3.Zero; }
    }
    void PlayMotion(Scene value, IReadOnlyList<ElementFrame> frame, string reason, bool metadataOnly=false)
    {
        ResetMotion();
        var profile = value.AnimationProfile;
        if (profile == AnimationProfile.Off) return;
        MotionStarts++;
        log.Write("overlay_motion", new { scene = value.Id, profile = profile.ToString(), reason, starts = MotionStarts });
        int duration = metadataOnly && profile==AnimationProfile.Calm ? DesignTokens.MotionMetadataMilliseconds : profile switch { AnimationProfile.Calm => DesignTokens.MotionCalmMilliseconds, AnimationProfile.Dynamic => DesignTokens.MotionDynamicMilliseconds, _ => DesignTokens.MotionCinematicMilliseconds };
        motionEndsAt=Environment.TickCount64+duration+(profile==AnimationProfile.Cinematic?96:profile==AnimationProfile.Dynamic?48:0);
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .75f), new Vector2(.25f, 1));
        if(!metadataOnly)using (var fade = compositor.CreateScalarKeyFrameAnimation())
        {
            fade.Duration = TimeSpan.FromMilliseconds(duration); fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1, easing); root.StartAnimation("Opacity", fade);
        }
        if (profile == AnimationProfile.Calm && !metadataOnly) return;
        int index = 0;
        foreach (var element in frame)
        {
            if (!element.Visible || element.Element.Type is ElementKind.Group or ElementKind.BackgroundPanel || element.Element.AnimationProfile == AnimationProfile.Off || !nodes.TryGetValue(element.Element.Id, out var node)) continue;
            bool content=element.Element.Type is ElementKind.TrackTitle or ElementKind.ArtistName or ElementKind.AlbumArtwork or ElementKind.LyricsText or ElementKind.Text;
            if(metadataOnly && element.Element.Type is not (ElementKind.TrackTitle or ElementKind.ArtistName or ElementKind.AlbumArtwork))continue;
            int delay = Math.Min(index++, 4) * (profile == AnimationProfile.Cinematic ? 24 : profile==AnimationProfile.Dynamic ? 12 : 0);
            using var reveal = compositor.CreateScalarKeyFrameAnimation(); reveal.Duration = TimeSpan.FromMilliseconds(duration + delay);
            float from=metadataOnly ? .45f : 0;
            reveal.InsertKeyFrame(0, from);
            if (delay > 0) reveal.InsertKeyFrame((float)delay / (duration + delay), from);
            reveal.InsertKeyFrame(1, node.Opacity, easing); node.Visual.StartAnimation("Opacity", reveal);
            // Interactive control slots stay exactly aligned with their native hit regions.
            if(content && profile is AnimationProfile.Dynamic or AnimationProfile.Cinematic)
            {
                using var translate=compositor.CreateVector3KeyFrameAnimation();translate.Duration=TimeSpan.FromMilliseconds(duration+delay);
                translate.InsertKeyFrame(0,new Vector3(0,(float)((profile==AnimationProfile.Cinematic?8:4)*dpiScale),0));
                translate.InsertKeyFrame(1,Vector3.Zero,easing);node.Visual.StartAnimation("Offset",translate);
            }
        }
    }
    internal void ReplayMotion()
    {
        if(scene is null || !Native.IsWindowVisible(Handle) || hiding || !System.Windows.SystemParameters.ClientAreaAnimation)return;
        PlayMotion(scene,SceneMath.Flatten(scene),"preview");
    }
    void HideWindow()
    {
        Native.ShowWindow(Handle,0);presenting=false;ResetMotion();
        foreach(var node in nodes.Values)
            if(node.PrismEdge!=null){node.ClearContent();node.Key="";}
    }
    void CancelHide() { hideTimer?.Stop();hideTimer=null;hiding=false; }
    internal void Hide(bool immediate=false)
    {
        foreach(var node in nodes.Values)node.StopMarquee();
        // A new entrance/content reveal must never jump to full opacity before hiding.
        if(immediate || scene is null || scene.AnimationProfile==AnimationProfile.Off || !System.Windows.SystemParameters.ClientAreaAnimation || Environment.TickCount64<motionEndsAt)
        { CancelHide();HideWindow();return; }
        if(hiding || !Native.IsWindowVisible(Handle))return;
        hiding=true;clickThrough=true;
        long style=Native.GetWindowLongPtrW(Handle,-20).ToInt64();Native.SetWindowLongPtrW(Handle,-20,new(style|Native.Transparent));
        ResetMotion();using var fade=compositor.CreateScalarKeyFrameAnimation();fade.Duration=TimeSpan.FromMilliseconds(DesignTokens.MotionExitMilliseconds);
        fade.InsertKeyFrame(0,1);fade.InsertKeyFrame(1,0);root.StartAnimation("Opacity",fade);
        var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(DesignTokens.MotionExitMilliseconds) };
        hideTimer=timer;
        timer.Tick+=(_,_)=>{timer.Stop();if(disposed || !ReferenceEquals(hideTimer,timer))return;hideTimer=null;hiding=false;HideWindow();};timer.Start();
        log.Write("overlay_motion_exit",new{duration=DesignTokens.MotionExitMilliseconds});
    }
    static IntPtr Route(IntPtr h, uint message, IntPtr wp, IntPtr lp)
    {
        if (routes.TryGetValue(h, out var w))
        {
            if (message == 0x21) return new(3);
            if (message == 0x84)
            {
                double hx = ((short)(lp.ToInt64() & 0xffff) - w.Bounds.Left - w.sceneOffset.X) / w.dpiScale;
                double hy = ((short)((lp.ToInt64() >> 16) & 0xffff) - w.Bounds.Top - w.sceneOffset.Y) / w.dpiScale;
                return new(w.clickThrough || w.scene is null || !PresetClip.Contains(w.scene, hx, hy) || SceneMath.Hit(w.scene, hx, hy) is null ? -1 : 1);
            }
            if (message == 0x202 && !w.clickThrough && w.scene is not null)
            {
                double x = ((short)(lp.ToInt64() & 0xffff) - w.sceneOffset.X) / w.dpiScale, y = ((short)((lp.ToInt64() >> 16) & 0xffff) - w.sceneOffset.Y) / w.dpiScale;
                var id = PresetClip.Contains(w.scene, x, y) ? SceneMath.Hit(w.scene, x, y) : null; var element = w.scene.Elements.FirstOrDefault(e => e.Id == id);
                if (element?.Type == ElementKind.PlaybackControls)
                {
                    var f = SceneMath.Flatten(w.scene).Single(f => f.Element.Id == element.Id); Matrix3x2.Invert(f.Transform, out var inv); var p = Vector2.Transform(new((float)x, (float)y), inv);
                    string requested = p.X < element.Width / 3 ? "previous" : p.X > element.Width * 2 / 3 ? "next" : "toggle";
                    bool allowed = requested switch { "previous" => w.playback?.CanPrevious == true, "next" => w.playback?.CanNext == true, _ => w.playback?.CanToggle == true };
                    if (allowed) w.command(requested);
                }
                return IntPtr.Zero;
            }
        }
        return Native.DefWindowProcW(h, message, wp, lp);
    }
    public void Dispose() { if(disposed)return;disposed=true;Hide(true); target.Root = null; foreach (var node in nodes.Values) node.Dispose(); nodes.Clear(); root.Children.RemoveAll(); root.Clip = null; presetClip?.Dispose(); presetGeometry?.Dispose(); insetClip.Dispose(); root.Dispose(); target.Dispose(); routes.Remove(Handle); Native.DestroyWindow(Handle); log.Write("overlay_destroyed", new { hwnd = Handle.ToInt64(), stillAlive = Native.IsWindow(Handle) }); }
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target); void EnsureOnThread(uint id); }
}

