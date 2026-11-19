using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonoHUD.Core;

public enum PresetKind { Compact = 0, MusicCard = 1, LyricsFocus = 2, Cinematic = 3, AlbumTile = 4, InfoRail = 5, Poster = 6, StatusStrip = 7, LyricStrip = 8, LyricSleeve = 9 }
public enum OverlayTheme { Dark, Light }

public sealed record OverlayConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; }
    public PresetKind Preset { get; set; } = PresetKind.MusicCard;
    public OverlayTheme Theme { get; set; } = OverlayTheme.Dark;
    public string AccentColor { get; set; } = "#FF" + DesignTokens.Accent[1..];
    public string FontFamily { get; set; } = "Wanted Sans";
    public double TextScale { get; set; } = 1;
    public int TitleWeight { get; set; }
    public double Scale { get; set; } = 1;
    public double BackgroundOpacity { get; set; } = .9;
    public string MonitorId { get; set; } = "";
    public double X { get; set; } = 64;
    public double Y { get; set; } = 72;
    public bool ClickThrough { get; set; } = true;
    public AnimationProfile Animation { get; set; } = AnimationProfile.Calm;
    public bool ShowArtwork { get; set; } = true;
    public bool ShowArtist { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    // Retained only to round-trip schema 2 documents written before the read-only presets.
    public bool ShowControls { get; set; } = true;
}

public sealed record OverlayDocument
{
    public string Format { get; set; } = "SonoHUD.Overlay";
    public int SchemaVersion { get; set; } = 2;
    public OverlayConfig Overlay { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public TaskbarConfig Taskbar { get; set; } = new();
}

public static class OverlayJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(OverlayDocument document)
    {
        OverlayValidation.Check(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static OverlayDocument Parse(string text)
    {
        using var header = JsonDocument.Parse(text);
        var root = header.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version))
            throw new InvalidDataException("오버레이 설정의 형식과 버전 정보가 없습니다.");
        if (format.GetString() != "SonoHUD.Overlay" || version != 2)
            throw new NotSupportedException("이 버전에서 지원하지 않는 오버레이 설정입니다. 원본을 보존했습니다.");
        if (!root.TryGetProperty("overlay", out var overlay) || overlay.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("단일 오버레이 설정이 없습니다.");
        var document = JsonSerializer.Deserialize<OverlayDocument>(text, Options) ?? throw new InvalidDataException("빈 오버레이 설정입니다.");
        OverlayValidation.Check(document);
        return document;
    }

    public static OverlayDocument Clone(OverlayDocument document) => Parse(Serialize(document));
}

public static class OverlayValidation
{
    public static void Check(OverlayDocument document)
    {
        if (document is null || document.Format != "SonoHUD.Overlay" || document.SchemaVersion != 2 || document.Overlay is null || document.Settings is null)
            throw new InvalidDataException("오버레이 문서 구조가 잘못되었습니다.");
        Check(document.Overlay);
        Check(document.Taskbar);
        var settings = document.Settings;
        if (settings.Appearance is { } appearance) settings.Appearance = appearance.MigrateTheme();
        if (settings.Spectrum is not null) settings.Spectrum = settings.Spectrum.Sanitize();
        if (settings.Lyrics is not null) settings.Lyrics = settings.Lyrics.Sanitize();
        if (settings.HotkeyFunction is < 8 or > 12 || !Enum.IsDefined(settings.DefaultAnimation) || settings.DefaultMonitorId is null || settings.DefaultMonitorId.Length > 256 || settings.MediaSourceAppId is null || settings.MediaSourceAppId.Length > 512)
            throw new InvalidDataException("일반 설정 값이 잘못되었습니다.");
    }

    public static void Check(OverlayConfig config)
    {
        if (config is null || config.Id == Guid.Empty || !Enum.IsDefined(config.Preset) || !Enum.IsDefined(config.Theme) || !Enum.IsDefined(config.Animation))
            throw new InvalidDataException("오버레이 식별자·프리셋·테마·애니메이션이 잘못되었습니다.");
        if (!SceneValidation.Color(config.AccentColor) || string.IsNullOrWhiteSpace(config.FontFamily) || config.FontFamily.Length > 100)
            throw new InvalidDataException("강조 색상이나 글꼴을 확인하세요.");
        if (!double.IsFinite(config.TextScale) || config.TextScale is < .9 or > 1.15 || config.TitleWeight is not (0 or 400 or 500 or 600 or 700))
            throw new InvalidDataException("글자 크기는 90~115%, 제목 두께는 기본 또는 400~700 범위로 설정하세요.");
        if (!double.IsFinite(config.Scale) || config.Scale is < .75 or > 1.5 ||
            !double.IsFinite(config.BackgroundOpacity) || config.BackgroundOpacity is < 0 or > 1)
            throw new InvalidDataException("크기는 75~150%, 배경 불투명도는 0~100% 범위로 설정하세요.");
        if (config.MonitorId is null || config.MonitorId.Length > 256 || !double.IsFinite(config.X) || !double.IsFinite(config.Y) || Math.Abs(config.X) > 100000 || Math.Abs(config.Y) > 100000)
            throw new InvalidDataException("오버레이 모니터와 위치를 확인하세요.");
    }

    public static void Check(TaskbarConfig config)
    {
        if (config is null || config.MonitorId is null || config.MonitorId.Length > 256 ||
            !Enum.IsDefined(config.Position) || !Enum.IsDefined(config.Style) ||
            !double.IsFinite(config.Width) || config.Width is < 160 or > 520)
            throw new InvalidDataException("작업 표시줄 위젯의 모니터·배치·너비 설정을 확인하세요.");
    }
}
