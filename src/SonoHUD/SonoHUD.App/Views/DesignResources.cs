using System.Windows.Media;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

public static class DesignResources
{
    static MusicTheme Default => MusicTheme.Gallery[0];
    public static Color ProgressColor => ColorOf(Default.Progress);
    public static Color PlayheadColor => ColorOf(Default.Playhead);
    public static Color ProgressTrackColor => ColorOf(Default.ProgressTrack);
    public static Color SpectrumColor => ColorOf(Default.Spectrum);
    public static Color LyricsCurrentColor => ColorOf(Default.LyricsCurrent);
    public static Color LyricsContextColor => ColorOf(Default.LyricsContext);
    public static Color LyricsWordColor => ColorOf(Default.LyricsWord);
    public static Color OnAccentColor => ColorOf(Default.OnAccent);
    public static Color FocusColor => ColorOf(Default.Accent);
    static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
    static Brush BrushOf(string value) { var brush = new SolidColorBrush(ColorOf(value)); brush.Freeze(); return brush; }

    public static Color BackgroundColor => ColorOf(Default.Background);
    public static Color SurfaceColor => ColorOf(Default.Surface);
    public static Color ElevatedColor => ColorOf(Default.Raised);
    public static Color TextPrimaryColor => ColorOf(Default.Ink);
    public static Color TextSecondaryColor => ColorOf(Default.Secondary);
    public static Color TextTertiaryColor => ColorOf(Default.Muted);
    public static Color AccentColor => ColorOf(Default.Accent);
    public static Color BorderColor => ColorOf(Default.Border);
    public static Color HoverColor => ColorOf(Default.Hover);
    public static Color PressedColor => ColorOf(Default.Pressed);
    public static Color SelectedColor => ColorOf(Default.Selected);
    public static Color DisabledColor => ColorOf(Default.Muted);
    public static Color ErrorColor => ColorOf(Default.Error);
    public static Color WarningColor => ColorOf(Default.Warning);
    public static Color SuccessColor => ColorOf(Default.Success);

    public static Brush LyricsWord { get; } = BrushOf(Default.LyricsWord);
    public static Brush Background { get; } = BrushOf(Default.Background);
    public static Brush Surface { get; } = BrushOf(Default.Surface);
    public static Brush Elevated { get; } = BrushOf(Default.Raised);
    public static Brush TextPrimary { get; } = BrushOf(Default.Ink);
    public static Brush TextSecondary { get; } = BrushOf(Default.Secondary);
    public static Brush TextTertiary { get; } = BrushOf(Default.Muted);
    public static Brush Accent { get; } = BrushOf(Default.Accent);
    public static Brush Border { get; } = BrushOf(Default.Border);
    public static Brush Hover { get; } = BrushOf(Default.Hover);
    public static Brush Pressed { get; } = BrushOf(Default.Pressed);
    public static Brush Selected { get; } = BrushOf(Default.Selected);
    public static Brush Disabled { get; } = BrushOf(Default.Muted);
    public static Brush Error { get; } = BrushOf(Default.Error);
    public static Brush Warning { get; } = BrushOf(Default.Warning);
    public static Brush Success { get; } = BrushOf(Default.Success);
    public static FontFamily FontFamily { get; } = FontCatalog.Resolve(FontCatalog.DefaultName);
    public static FontFamily MonoFontFamily { get; } = FontCatalog.Resolve(FontCatalog.MonoName);
}
