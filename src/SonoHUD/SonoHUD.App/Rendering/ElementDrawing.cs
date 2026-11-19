using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SonoHUD.Core;
using SonoHUD.App.Media;
using SonoHUD.App.Views;

namespace SonoHUD.App.Rendering;

internal static class ElementDrawing
{
    static readonly Dictionary<string, BitmapSource> images = [];
    internal static string AssetDirectory { get; set; } = "";
    internal static Brush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    internal static string Text(SceneElement e, PlaybackState p) => e.Type switch
    {
        ElementKind.TrackTitle => e.Marquee ? TitleMarquee.SingleLine(p.Title) : p.Title,
        ElementKind.ArtistName => p.Artist,
        ElementKind.AlbumTitle => e.Style.SuppressRedundantAlbum && (string.IsNullOrWhiteSpace(p.AlbumTitle) || string.Equals(p.AlbumTitle.Trim(), p.Title.Trim(), StringComparison.OrdinalIgnoreCase)) ? "" : string.IsNullOrWhiteSpace(p.AlbumTitle) ? "앨범 정보 없음" : p.AlbumTitle,
        ElementKind.PlaybackSource => string.IsNullOrWhiteSpace(p.Source) ? "플레이어 없음" : p.Source,
        ElementKind.PlaybackStatus => PlaybackStatus(p),
        ElementKind.PlaybackTime => $"{TimeValue(p.PositionSeconds)} / {TimeValue(p.DurationSeconds)}",
        ElementKind.LyricsText => p.Lyrics.Kind==LyricsKind.PlainText ? string.Join('\n',p.Lyrics.PlainText.Split('\n').Take(3)) : p.IsDemo?e.Style.Text:"가사를 기다립니다",
        ElementKind.Text => e.Name=="가사 출처" && (p.IsDemo || p.Lyrics.IsDemo) ? "SonoHUD 자체 제작 샘플 가사" : e.Style.Text,
        _ => ""
    };
    static string PlaybackStatus(PlaybackState p) => p.IsDemo ? "자체 샘플 · " + (p.IsPlaying ? "재생 중" : "일시정지") :
        string.IsNullOrWhiteSpace(p.Source) ? "연결된 플레이어 없음" : p.IsPlaying ? "재생 중" :
        p.Status is "일시정지" or "정지" or "플레이어 닫힘" or "재생 준비" or "곡 전환 중" ? p.Status : "재생 상태 확인 중";
    static string TimeValue(double? seconds)
    {
        if (seconds is not double value || !double.IsFinite(value) || value < 0 || value > TimeSpan.MaxValue.TotalSeconds) return "—";
        long whole = (long)Math.Floor(value);
        return whole >= 3600 ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}" : $"{whole / 60}:{whole % 60:00}";
    }
    internal static string ContentKey(SceneElement e, PlaybackState p) => $"{e.Type}|{e.Width}|{e.Height}|{e.Style}|marquee:{e.Marquee}|{(e.Marquee ? $"{p.Source}:{p.Artist}:{p.AlbumTitle}:{p.IsDemo}" : "")}|{Text(e, p)}|{(e.Type == ElementKind.AlbumArtwork ? p.ArtworkKey : "")}|{(e.Type == ElementKind.ProgressBar ? Math.Round(p.Progress * 500) : 0)}|{(e.Type == ElementKind.PlaybackControls ? $"{p.IsPlaying}:{p.CanPrevious}:{p.CanToggle}:{p.CanNext}" : "")}";
    internal static BitmapSource? Artwork(PlaybackState p)
    {
        if (p.Artwork is null || p.ArtworkKey.Length == 0) return null; if (images.TryGetValue(p.ArtworkKey, out var found)) return found;
        try { using var stream = new MemoryStream(p.Artwork); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 512; image.StreamSource = stream; image.EndInit(); image.Freeze(); Cache(p.ArtworkKey, image); return image; } catch (Exception e) when (e is NotSupportedException or IOException or ArgumentException) { return null; }
    }
    static void Cache(string key, BitmapSource image) { if (images.Count >= 16) images.Remove(images.Keys.First()); images[key] = image; }
    internal static void ClearCache() => images.Clear();
    static BitmapSource? Asset(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains(':')) return null;
        string path = Path.Combine(AssetDirectory, name); if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) return null;
        string key = path + File.GetLastWriteTimeUtc(path).Ticks; if (images.TryGetValue(key, out var image)) return image;
        try { using var stream = File.OpenRead(path); var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.DecodePixelWidth = 1024; b.StreamSource = stream; b.EndInit(); b.Freeze(); Cache(key, b); return b; } catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException) { return null; }
    }
    internal static Rect ArtworkBounds(double imageWidth, double imageHeight, Rect destination)
    {
        if (!double.IsFinite(imageWidth) || !double.IsFinite(imageHeight) || imageWidth <= 0 || imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        double scale = Math.Max(destination.Width / imageWidth, destination.Height / imageHeight);
        double width = imageWidth * scale, height = imageHeight * scale;
        return new(destination.X + (destination.Width - width) / 2, destination.Y + (destination.Height - height) / 2, width, height);
    }
    internal static FontFamily ResolveFontFamily(string requested) => FontCatalog.Resolve(requested);
    internal static string ControlInk(string accentColor)
    {
        double Luminance(string value)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            double Channel(byte component) { double s = component / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        double background = Luminance(accentColor);
        double Contrast(string candidate) { double ink = Luminance(candidate); return (Math.Max(background, ink) + .05) / (Math.Min(background, ink) + .05); }
        return Contrast(DesignTokens.Background) >= Contrast(DesignTokens.TextPrimary) ? DesignTokens.Background : DesignTokens.TextPrimary;
    }
    internal static void Draw(DrawingContext dc, SceneElement e, PlaybackState p, double pixelsPerDip = 1)
    {
        var rect = new Rect(0, 0, e.Width, e.Height); dc.PushClip(new RectangleGeometry(rect)); var color = Brush(e.Style.Foreground);
        switch (e.Type)
        {
            case ElementKind.BackgroundPanel: dc.DrawRoundedRectangle(Brush(e.Style.Background), null, rect, e.Style.CornerRadius, e.Style.CornerRadius); break;
            case ElementKind.AlbumArtwork:
                dc.PushClip(new RectangleGeometry(rect, e.Style.CornerRadius, e.Style.CornerRadius));
                var art = Artwork(p);
                if (art is not null) dc.DrawImage(art, ArtworkBounds(art.PixelWidth, art.PixelHeight, rect));
                else
                {
                    dc.DrawRectangle(Brush(e.Style.Background), null, rect);
                    var placeholder = Brush(e.Style.Foreground); double radius = Math.Min(e.Width, e.Height) * .24;
                    var line = new Pen(placeholder, Math.Max(1, radius * .055));
                    dc.PushOpacity(.48); dc.DrawEllipse(null, line, new(e.Width / 2, e.Height / 2), radius, radius);
                    dc.DrawEllipse(null, line, new(e.Width / 2, e.Height / 2), radius * .72, radius * .72);
                    dc.DrawEllipse(placeholder, null, new(e.Width / 2, e.Height / 2), radius * .14, radius * .14); dc.Pop();
                }
                dc.Pop();
                break;
            case ElementKind.Image:
                // Preserve the legacy arbitrary-image Fill contract; album art uses aspect-preserving crop above.
                var image = Asset(e.Style.ImagePath);
                if (image is not null) dc.DrawImage(image, rect);
                else { dc.DrawRoundedRectangle(Brush("#FF25423F"), null, rect, 12, 12); var pen = new Pen(Brush("#FF84CDBF"), Math.Max(1, e.Width * .018)); dc.DrawEllipse(null, pen, new(e.Width / 2, e.Height / 2), e.Width * .25, e.Height * .25); dc.DrawEllipse(Brush("#FF84CDBF"), null, new(e.Width / 2, e.Height / 2), e.Width * .045, e.Height * .045); }
                break;
            case ElementKind.ProgressBar:
                double progressRadius = Math.Min(e.Height / 2, DesignTokens.ControlRadius);
                dc.DrawRoundedRectangle(Brush(e.Style.Background), null, rect, progressRadius, progressRadius); dc.DrawRoundedRectangle(color, null, new(0, 0, e.Width * p.Progress, e.Height), progressRadius, progressRadius); break;
            case ElementKind.PlaybackControls:
                double mid = e.Height / 2, cell = e.Width / 3, button = Math.Max(1, Math.Min(e.Height, cell - 8));
                var onAccent = Brush(ControlInk(e.Style.Foreground));
                for (int index = 0; index < 3; index++)
                {
                    bool enabled = index == 0 ? p.CanPrevious : index == 1 ? p.CanToggle : p.CanNext;
                    dc.PushOpacity(enabled ? 1 : .45);
                    double x = cell * (index + .5), r = Math.Min(button * .21, 7);
                    Brush glyph = index == 1 ? onAccent : color;
                    if (index == 1) dc.DrawRoundedRectangle(color, null, new(x - button / 2, mid - button / 2, button, button), DesignTokens.ControlRadius, DesignTokens.ControlRadius);
                    if (index == 1 && p.IsPlaying) { dc.DrawRectangle(glyph, null, new(x - r * .6, mid - r, r * .4, r * 2)); dc.DrawRectangle(glyph, null, new(x + r * .2, mid - r, r * .4, r * 2)); }
                    else
                    {
                        double sign = index == 0 ? -1 : 1; var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(new(x - sign * r / 2, mid - r), true, true); c.LineTo(new(x + sign * r, mid), true, false); c.LineTo(new(x - sign * r / 2, mid + r), true, false); }
                        g.Freeze(); dc.DrawGeometry(glyph, null, g);
                        if (index != 1) dc.DrawLine(new Pen(glyph, 1.6), new(x + sign * (r + 3), mid - r), new(x + sign * (r + 3), mid + r));
                    }
                    dc.Pop();
                }
                break;
            // The live output meter has an independent data path; absent data draws nothing.
            case ElementKind.AudioLevel:
            case ElementKind.Spectrum:
            case ElementKind.Group: break;
            default:
                double lineHeight = e.Style.FontSize * 1.25;
                var text = new FormattedText(Text(e, p), CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface(ResolveFontFamily(e.Style.FontFamily), FontStyles.Normal, FontWeight.FromOpenTypeWeight(e.Style.FontWeight), FontStretches.Normal), e.Style.FontSize, color, pixelsPerDip)
                { MaxTextWidth = Math.Max(1, e.Width), MaxTextHeight = Math.Max(1, e.Height), LineHeight = lineHeight, MaxLineCount = e.Marquee ? 1 : Math.Max(1, (int)Math.Floor(e.Height / lineHeight)), Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(text, new(0, 0)); break;
        }
        dc.Pop();
    }
    internal static double RasterDensity(SceneElement e, double requestedScale = 1, int maximumPixels = 2048) => Math.Min(double.IsFinite(requestedScale) && requestedScale > 0 ? Math.Clamp(requestedScale, .25, 4) : 1, maximumPixels / Math.Max(e.Width, e.Height));
    internal static byte[] Raster(SceneElement e, PlaybackState p, out int width, out int height, double rasterScale = 1, int maximumPixels = 2048)
    {
        double scale = RasterDensity(e, rasterScale, maximumPixels); width = Math.Clamp((int)Math.Ceiling(e.Width * scale), 1, maximumPixels); height = Math.Clamp((int)Math.Ceiling(e.Height * scale), 1, maximumPixels);
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) { dc.PushTransform(new ScaleTransform(scale, scale)); Draw(dc, e, p, scale); dc.Pop(); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); byte[] pixels = new byte[checked(width * height * 4)]; bitmap.CopyPixels(pixels, width * 4, 0); return pixels;
    }
}
