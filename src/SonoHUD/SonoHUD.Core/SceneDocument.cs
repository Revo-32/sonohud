using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonoHUD.Core;

public enum ElementKind { AlbumArtwork, TrackTitle, ArtistName, LyricsText, ProgressBar, PlaybackControls, BackgroundPanel, Text, Image, Group, AlbumTitle, PlaybackSource, PlaybackStatus, PlaybackTime, AudioLevel, Spectrum }
public enum AnimationProfile { Off, Calm, Dynamic, Cinematic }
public sealed record ElementStyle
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public string? ContextForeground { get; set; }
    public string Foreground { get; set; } = "#FFF0F3F7";
    public string Background { get; set; } = "#E6181E28";
    public string FontFamily { get; set; } = DesignTokens.FontFamily;
    public double FontSize { get; set; } = 22;
    public int FontWeight { get; set; } = 400;
    public double CornerRadius { get; set; } = 12;
    public string Text { get; set; } = "새 텍스트";
    public string ImagePath { get; set; } = "";
    public bool SuppressRedundantAlbum { get; set; }
}
public sealed record SceneElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ElementKind Type { get; set; }
    public string Name { get; set; } = "요소";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 44;
    public double Opacity { get; set; } = 1;
    public double Rotation { get; set; }
    public double Scale { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Marquee { get; set; }
    public int ZIndex { get; set; }
    public Guid? ParentGroup { get; set; }
    public ElementStyle Style { get; set; } = new();
    public AnimationProfile AnimationProfile { get; set; } = AnimationProfile.Calm;
}
public sealed record Scene
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Music Widget";
    public bool Active { get; set; } = true;
    public bool Visible { get; set; } = true;
    public string MonitorId { get; set; } = "";
    public double X { get; set; } = 64;
    public double Y { get; set; } = 72;
    public double Width { get; set; } = 560;
    public double Height { get; set; } = 240;
    public bool ClickThrough { get; set; } = true;
    public int ZOrder { get; set; }
    public AnimationProfile AnimationProfile { get; set; } = AnimationProfile.Calm;
    public Dictionary<string, string> UserSettings { get; set; } = [];
    public List<SceneElement> Elements { get; set; } = [];
}
public sealed record AppSettings
{
    public AppearanceSettings? Appearance { get; set; }
    public bool ShowOverlaysOnStartup { get; set; } = true;
    public bool KeepRunningOnClose { get; set; }
    public bool DefaultClickThrough { get; set; } = true;
    public string DefaultMonitorId { get; set; } = "";
    public AnimationProfile DefaultAnimation { get; set; } = AnimationProfile.Calm;
    public int HotkeyFunction { get; set; } = 9;
    public bool HotkeyEnabled { get; set; } = true;
    public bool GridSnap { get; set; } = true;
    // Empty keeps automatic selection; live session indexes are deliberately not persisted.
    public string MediaSourceAppId { get; set; } = "";
    // Opt-in meter for the entire default output, not the selected media session.
    public bool ShowSystemOutputLevel { get; set; }
    public SpectrumSettings? Spectrum { get; set; }
    public LyricsSettings? Lyrics { get; set; }
    [JsonIgnore] public SpectrumSettings EffectiveSpectrum => (Spectrum ?? new() { Enabled = ShowSystemOutputLevel }).Sanitize();
}
public sealed record SceneDocument
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<Scene> Scenes { get; set; } = [];
}
public static class SceneJson
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(SceneDocument d) => JsonSerializer.Serialize(d, Options);
    public static SceneDocument Parse(string text) { var d = JsonSerializer.Deserialize<SceneDocument>(text, Options) ?? throw new InvalidDataException("빈 문서입니다."); SceneValidation.Check(d); return d; }
    public static SceneDocument Clone(SceneDocument d) => Parse(Serialize(d));
}
public static class SceneValidation
{
    public static void Check(SceneDocument d)
    {
        if (d is null || d.SchemaVersion != 1) throw new InvalidDataException("지원하지 않는 Scene 스키마입니다.");
        if (d.Settings is null || d.Scenes is null || d.Scenes.Count > 16) throw new InvalidDataException("Scene 문서 구조가 잘못되었습니다.");
        if (d.Settings.HotkeyFunction is < 8 or > 12 || !Enum.IsDefined(d.Settings.DefaultAnimation)) throw new InvalidDataException("설정 값이 잘못되었습니다.");
        var sceneIds = new HashSet<Guid>();
        foreach (var s in d.Scenes)
        {
            if (s is null || s.Id == Guid.Empty || !sceneIds.Add(s.Id) || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100 || s.Elements is null || s.Elements.Count > 200 || s.UserSettings is null || !Enum.IsDefined(s.AnimationProfile)) throw new InvalidDataException("Scene 식별자·이름·요소 수가 잘못되었습니다.");
            Box(s.X, s.Y, s.Width, s.Height); var ids = new HashSet<Guid>();
            foreach (var e in s.Elements)
            {
                if (e is null || e.Id == Guid.Empty || !ids.Add(e.Id) || !Enum.IsDefined(e.Type) || !Enum.IsDefined(e.AnimationProfile) || e.Style is null || string.IsNullOrWhiteSpace(e.Name)) throw new InvalidDataException("요소 구조가 잘못되었습니다.");
                Box(e.X, e.Y, e.Width, e.Height, e.Type == ElementKind.Group ? 200000 : 4096);
                if (!double.IsFinite(e.Scale) || e.Scale is < .05 or > 20 || !double.IsFinite(e.Rotation) || Math.Abs(e.Rotation) > 36000 || !double.IsFinite(e.Opacity) || e.Opacity is < 0 or > 1) throw new InvalidDataException("변환 값이 잘못되었습니다.");
                var st = e.Style;
                if (!Color(st.Foreground) || !Color(st.Background) || (st.ContextForeground is not null && !Color(st.ContextForeground)) || !double.IsFinite(st.FontSize) || st.FontSize is < 6 or > 200 || st.FontWeight is < 100 or > 900 || !double.IsFinite(st.CornerRadius) || st.CornerRadius is < 0 or > 500 || string.IsNullOrWhiteSpace(st.FontFamily) || st.FontFamily.Length > 100 || st.Text is null || st.Text.Length > 4000 || st.ImagePath is null || st.ImagePath.Length > 2048) throw new InvalidDataException("스타일 값이 잘못되었습니다.");
            }
            foreach (var e in s.Elements)
            {
                var seen = new HashSet<Guid> { e.Id }; var parent = e.ParentGroup;
                while (parent is Guid id)
                {
                    if (!seen.Add(id) || seen.Count > 9) throw new InvalidDataException("그룹 순환 또는 최대 깊이 초과입니다.");
                    var p = s.Elements.SingleOrDefault(x => x.Id == id);
                    if (p is null || p.Type != ElementKind.Group) throw new InvalidDataException("그룹 참조가 잘못되었습니다.");
                    parent = p.ParentGroup;
                }
            }
        }
    }
    static void Box(double x, double y, double w, double h, double maximumSize = 4096) { if (new[] { x, y, w, h }.Any(v => !double.IsFinite(v)) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000 || w < 1 || w > maximumSize || h < 1 || h > maximumSize) throw new InvalidDataException("좌표·크기는 유효한 범위여야 합니다."); }
    public static bool Color(string? s) => s is { Length: 7 or 9 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
public readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}
public sealed record ElementFrame(SceneElement Element, Matrix3x2 Transform, double Opacity, bool Visible, bool Locked)
{
    public Bounds Bounds => SceneMath.BoundsOf(Element, Transform);
}
public static class SceneMath
{
    public static Matrix3x2 Local(SceneElement e) => Matrix3x2.CreateScale((float)e.Scale) * Matrix3x2.CreateRotation((float)(e.Rotation * Math.PI / 180)) * Matrix3x2.CreateTranslation((float)e.X, (float)e.Y);
    public static IReadOnlyList<ElementFrame> Flatten(Scene scene)
    {
        var result = new List<ElementFrame>();
        void Visit(Guid? parent, Matrix3x2 matrix, double opacity, bool visible, bool locked)
        {
            foreach (var e in scene.Elements.Where(x => x.ParentGroup == parent).OrderBy(x => x.ZIndex))
            {
                var m = Local(e) * matrix; var f = new ElementFrame(e, m, opacity * e.Opacity, visible && e.Visible, locked || e.Locked); result.Add(f);
                if (e.Type == ElementKind.Group) Visit(e.Id, m, f.Opacity, f.Visible, f.Locked);
            }
        }
        Visit(null, Matrix3x2.Identity, 1, true, false); return result;
    }
    public static Bounds BoundsOf(SceneElement e, Matrix3x2 m)
    {
        var ps = new[] { Vector2.Zero, new Vector2((float)e.Width, 0), new Vector2(0, (float)e.Height), new Vector2((float)e.Width, (float)e.Height) }.Select(p => Vector2.Transform(p, m)).ToArray();
        return new(ps.Min(p => p.X), ps.Min(p => p.Y), ps.Max(p => p.X) - ps.Min(p => p.X), ps.Max(p => p.Y) - ps.Min(p => p.Y));
    }
    public static Matrix3x2 ParentTransform(Scene s, SceneElement e) => e.ParentGroup is Guid id ? Flatten(s).Single(f => f.Element.Id == id).Transform : Matrix3x2.Identity;
    public static Guid? Hit(Scene s, double x, double y)
    {
        foreach (var f in Flatten(s).Reverse())
        {
            if (!f.Visible || f.Locked || f.Element.Type == ElementKind.Group || !Matrix3x2.Invert(f.Transform, out var inverse)) continue;
            var p = Vector2.Transform(new((float)x, (float)y), inverse);
            if (p.X >= 0 && p.Y >= 0 && p.X <= f.Element.Width && p.Y <= f.Element.Height) return f.Element.Id;
        }
        return null;
    }
    public static void AssignTransform(SceneElement e, Matrix3x2 m)
    {
        e.X = m.M31; e.Y = m.M32; e.Scale = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12); e.Rotation = Math.Atan2(m.M12, m.M11) * 180 / Math.PI;
    }
}
public static class SceneFactory
{
    public static Scene Music(string monitor, AppSettings settings)
    {
        var s = new Scene { MonitorId = monitor, ClickThrough = settings.DefaultClickThrough, AnimationProfile = settings.DefaultAnimation };
        void Add(ElementKind type, string name, double x, double y, double w, double h, int z, string color = "#FFF0F3F7", double size = 22)
            => s.Elements.Add(new() { Type = type, Name = name, X = x, Y = y, Width = w, Height = h, ZIndex = z, AnimationProfile = settings.DefaultAnimation, Style = new() { Foreground = color, FontSize = size } });
        Add(ElementKind.BackgroundPanel, "배경", 0, 0, 560, 240, 0); s.Elements[0].Locked = true;
        Add(ElementKind.AlbumArtwork, "앨범아트", 24, 28, 128, 128, 1);
        Add(ElementKind.TrackTitle, "곡명", 176, 30, 360, 34, 2, size: 24); s.Elements[^1].Style.FontWeight = 600;
        Add(ElementKind.ArtistName, "아티스트", 176, 70, 360, 26, 3, "#FFA9B6C6", 16);
        Add(ElementKind.LyricsText, "샘플 가사", 176, 107, 360, 40, 4, "#FFD6DFE9", 17); s.Elements[^1].Style.Text = "오늘의 리듬을 나만의 공간에";
        Add(ElementKind.ProgressBar, "진행률", 24, 184, 512, 5, 5, "#FF6ED8C5");
        Add(ElementKind.PlaybackControls, "재생 제어", 176, 146, 160, 28, 6, "#FF6ED8C5", 21);
        return s;
    }
}
