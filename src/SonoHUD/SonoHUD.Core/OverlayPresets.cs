using System.Security.Cryptography;
using System.Text;

namespace SonoHUD.Core;

public static class OverlayPresets
{
    public static bool ShowsLyrics(PresetKind preset) => preset is PresetKind.LyricsFocus or PresetKind.LyricStrip or PresetKind.LyricSleeve;
    public static string DisplayName(PresetKind preset) => preset switch
    {
        PresetKind.MusicCard => "Music Card", PresetKind.LyricsFocus => "Lyrics Focus",
        PresetKind.AlbumTile => "Album Tile", PresetKind.InfoRail => "Info Rail", PresetKind.StatusStrip => "Status Strip",
        PresetKind.LyricStrip => "Lyric Strip", PresetKind.LyricSleeve => "Lyric Sleeve", _ => preset.ToString()
    };
    public static (double Width, double Height) Dimensions(PresetKind preset) => preset switch
    {
        PresetKind.Compact => (380, 104),
        PresetKind.MusicCard => (460, 196),
        PresetKind.LyricsFocus => (480, 284),
        PresetKind.Cinematic => (560, 208),
        PresetKind.AlbumTile => (280, 424),
        PresetKind.InfoRail => (300, 300),
        PresetKind.Poster => (320, 440),
        PresetKind.StatusStrip => (640, 72),
        PresetKind.LyricStrip => (620, 148),
        PresetKind.LyricSleeve => (328, 392),
        _ => throw new InvalidDataException("지원하지 않는 디자인 프리셋입니다.")
    };

    public static Guid ElementId(Guid overlayId, string role) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"SonoHUD.Overlay:{overlayId:N}:{role}"))[..16]);

    public static SceneDocument Render(OverlayDocument document)
    {
        OverlayValidation.Check(document);
        var render = new SceneDocument { Settings = document.Settings with { }, Scenes = [Create(document.Overlay,document.Settings.EffectiveSpectrum)] };
        SceneValidation.Check(render);
        return render;
    }

    public static Scene Create(OverlayConfig config, SpectrumSettings? spectrum = null, MusicTheme? palette = null, bool lyricsLayout = true, string? providerName = null)
    {
        OverlayValidation.Check(config);
        var (width, height) = Dimensions(config.Preset);
        bool light = config.Theme == OverlayTheme.Light;
        string foreground = light ? DesignTokens.LightTextPrimary : DesignTokens.TextPrimary;
        string secondary = light ? DesignTokens.LightTextSecondary : DesignTokens.TextSecondary;
        string accent = light ? LightAccent(config.AccentColor) : config.AccentColor;
        string surface = light ? DesignTokens.LightSurface : DesignTokens.Surface;
        string elevated = light ? DesignTokens.LightElevated : DesignTokens.Elevated;
        string border = light ? DesignTokens.LightBorder : DesignTokens.Border;
        if (palette is not null)
        {
            foreground=palette.Ink; secondary=palette.Secondary; accent=palette.Accent;
            surface=palette.Surface; elevated=palette.Raised; border=palette.Border;
        }
        string background = $"#{(int)Math.Round(config.BackgroundOpacity * 255):X2}{surface[^6..]}";
        var rootId = ElementId(config.Id, "root");
        var scene = new Scene
        {
            Id = config.Id, Name = config.Preset.ToString(), Active = true, Visible = config.Enabled,
            MonitorId = config.MonitorId, X = config.X, Y = config.Y, Width = width * config.Scale, Height = height * config.Scale,
            ClickThrough = config.ClickThrough, AnimationProfile = config.Animation,
            UserSettings = new() { ["preset"] = config.Preset.ToString(), ["theme"] = config.Theme.ToString(), ["format"] = "SonoHUD.Overlay" },
            Elements = [new() { Id = rootId, Type = ElementKind.Group, Name = "프리셋 크기", Width = width, Height = height, Scale = config.Scale, Locked = false, AnimationProfile = config.Animation }]
        };
        if (palette?.IsPrism==true) scene.UserSettings["appearance"]=MusicTheme.PrismName;
        if (palette?.SpectrumGradient==true) scene.UserSettings["spectrumGradient"]="1";
        int z = 0;
        void Add(string role, ElementKind kind, string name, double x, double y, double w, double h, double fontSize = 12, string? color = null, string text = "", int weight = 400)
        {
            scene.Elements.Add(new()
            {
                Id = ElementId(config.Id, role), Type = kind, Name = name, X = x, Y = y, Width = w, Height = h,
                ParentGroup = rootId, ZIndex = z++, AnimationProfile = config.Animation, Marquee = kind == ElementKind.TrackTitle,
                Style = new()
                {
                    Foreground = color ?? foreground, Background = kind == ElementKind.LyricsText ? palette?.LyricsWord ?? accent : kind == ElementKind.AlbumArtwork ? elevated : kind == ElementKind.ProgressBar ? palette?.ProgressTrack ?? border : background,
                    ContextForeground = kind == ElementKind.LyricsText ? palette?.LyricsContext : null,
                    FontFamily = config.FontFamily, FontSize = fontSize * config.TextScale,
                    FontWeight = kind == ElementKind.TrackTitle && config.TitleWeight != 0 ? config.TitleWeight : weight,
                    Text = text, CornerRadius = kind == ElementKind.AlbumArtwork ? 8 : 0
                }
            });
        }
        void Art(double x, double y, double w, double h) { if (config.ShowArtwork) Add("artwork", ElementKind.AlbumArtwork, "앨범아트", x, y, w, h); }
        void Title(double x, double y, double w, double h, double size, int weight = 600) => Add("title", ElementKind.TrackTitle, "곡명", x, y, w, h, size, weight: weight);
        void Artist(double x, double y, double w, double h, double size = 12) { if (config.ShowArtist) Add("artist", ElementKind.ArtistName, "아티스트", x, y, w, h, size, secondary); }
        void Time(double x, double y, double w, double h, double size = 10) => Add("time", ElementKind.PlaybackTime, "현재 위치와 전체 시간", x, y, w, h, size, secondary);
        void Progress(double x, double y, double w, double h = 3) { if (config.ShowProgress) Add("progress", ElementKind.ProgressBar, "진행률", x, y, w, h, color: palette?.Progress ?? accent); }
        void Spectrum(double x, double y, double w, double h, string style = "Bars") => Add("audio-level", ElementKind.Spectrum, "시스템 주파수 스펙트럼", x, y, w, h, color: palette?.Spectrum ?? accent, text: spectrum is { Style: not SpectrumStyle.Preset } ? spectrum.Style.ToString() : style);
        void Source(double x,double y,double w) => Add("sample-label",ElementKind.Text,"가사 출처",x,y,w,16,10,secondary,string.IsNullOrWhiteSpace(providerName) ? "현재 곡 가사" : providerName + " · 현재 곡 가사",500);
        void Lyrics(double x,double y,double w,double h,double size) => Add("lyrics",ElementKind.LyricsText,"현재 곡 가사",x,y,w,h,size,text:"오늘의 리듬을 따라\n나만의 공간에 머물러요",weight:600);

        // One raster surface plus one shared outer clip prevents opacity accumulation and
        // fractional-scale seams. Information hierarchy comes from layout, not stacked panels.
        Add("background", ElementKind.BackgroundPanel, "배경", 0, 0, width, height);
        switch (config.Preset)
        {
            case PresetKind.Compact:
            {
                Art(12, 12, 72, 72); double left = config.ShowArtwork ? 98 : 14, span = width - left - 14;
                Title(left, 14, span, 28, 17); Artist(left, 48, span, 18);
                Time(left, 74, span - 80, 15, 9.5); Spectrum(300, 68, 66, 20);
                Progress(left, 92, span, 2); break;
            }
            case PresetKind.MusicCard:
            {
                Art(14, 14, 148, 148); double left = config.ShowArtwork ? 184 : 18, span = width - left - 18;
                Title(left, 32, span, 32, 21); Artist(left, 80, span, 22, 14);
                Spectrum(left, 112, span, 26); Time(left, 150, span, 16, 10);
                Progress(left, 176, span); break;
            }
            case PresetKind.LyricsFocus:
            {
                if(!lyricsLayout){
                    bool showFft=spectrum?.Enabled==true && spectrum.Overlay;
                    double art=showFft?68:140, top=showFft?16:42;
                    Art(18,top,art,art);double x=config.ShowArtwork?art+34:20;
                    Title(x,top+8,width-x-20,34,21);Artist(x,top+48,width-x-20,22,14);
                    Spectrum(20,showFft?104:192,440,showFft?124:36,"Bars");
                    Time(20,244,440,18,11);Progress(20,270,440);break;
                }
                Art(18, 16, 44, 44); double left = config.ShowArtwork ? 76 : 20;
                Title(left, 14, width - left - 20, 24, 16); Artist(left, 42, width - left - 20, 18);
                Source(20,73,440);
                Lyrics(20,94,440,106,25);
                Spectrum(20, 208, 440, 22, "Minimal"); Time(20, 244, 440, 18, 11); Progress(20, 270, 440); break;
            }
            case PresetKind.Cinematic:
            {
                Art(392, 24, 160, 160); double span = config.ShowArtwork ? 344 : 512;
                Title(24, 36, span, 46, 30); Artist(24, 100, span, 22, 14);
                Spectrum(24, 130, span, 30, "Curve");
                Time(24, 178, 112, 16); Progress(148, 184, span - 124); break;
            }
            case PresetKind.AlbumTile:
            {
                Art(28, 16, 224, 224);
                Title(20, 260, 240, 32, 21); Artist(20, 304, 240, 20, 13);
                Spectrum(20, 342, 240, 30, "Mirror"); Time(20, 380, 240, 16);
                Progress(20, 402, 240); break;
            }
            case PresetKind.InfoRail:
            {
                Art(20, 28, 88, 88); Time(config.ShowArtwork ? 124 : 20, 56, config.ShowArtwork ? 156 : 260, 20, 12);
                Spectrum(config.ShowArtwork ? 124 : 20, 88, config.ShowArtwork ? 156 : 260, 28);
                Title(20, 152, 260, 36, 24); Artist(20, 220, 260, 24, 15);
                Progress(20, 278, 260, 4); break;
            }
            case PresetKind.Poster:
            {
                Title(20, 20, 280, 40, 26); Artist(20, 72, 280, 20, 13); Art(20, 112, 280, 260);
                Spectrum(20, 384, 280, 16, "Mirror");
                Time(20, 410, 142, 18, 11); Progress(174, 416, 126); break;
            }
            case PresetKind.StatusStrip:
            {
                Art(10, 10, 44, 44); double left = config.ShowArtwork ? 64 : 12;
                Title(left, config.ShowArtist ? 8 : 20, 514 - left, 26, 16); Artist(left, 38, 514 - left, 16, 10);
                Time(534, 22, 94, 18, 11);
                Spectrum(548, 46, 80, 12, "Minimal");
                Progress(left, 62, 628 - left, 2); break;
            }
            case PresetKind.LyricStrip:
            {
                Art(16,14,32,32); double left=config.ShowArtwork?60:16;
                Title(left,12,292-left,26,16); Artist(306,15,142,18,12);
                if (lyricsLayout) { Source(462,15,142); Lyrics(18,52,584,62,24); }
                Spectrum(18,lyricsLayout?117:60,348,lyricsLayout?12:52,"Minimal");
                Time(432,116,170,18); Progress(18,137,584,2); break;
            }
            case PresetKind.LyricSleeve:
            {
                Art(20,20,112,112); double left=config.ShowArtwork?146:20;
                Title(left,30,308-left,30,19); Artist(left,68,308-left,22,14);
                Time(left,102,308-left,18,11);
                if (lyricsLayout) { Source(20,148,288); Lyrics(20,172,288,146,26); }
                Spectrum(20,lyricsLayout?335:165,288,lyricsLayout?24:172,"Bars");
                Progress(20,376,288,3); break;
            }
        }
        SceneValidation.Check(new() { Scenes = [scene] });
        return scene;
    }

    static string LightAccent(string color)
    {
        string alpha = color.Length == 9 ? color[1..3] : "FF";
        string rgb = color[^6..];
        int Shade(int offset) => (int)Math.Round(Convert.ToInt32(rgb.Substring(offset * 2, 2), 16) * .55);
        return $"#{alpha}{Shade(0):X2}{Shade(1):X2}{Shade(2):X2}";
    }
}
