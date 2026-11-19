namespace SonoHUD.Core;

// Shared values for the WPF typed-resource adapter and the Composition scene projection.
public static class DesignTokens
{
    public const string Background = "#111111";
    public const string Surface = "#0A0A0A";
    public const string Elevated = "#1A1A1A";
    public const string TextPrimary = "#E8E8E3";
    public const string TextSecondary = "#AAAAA4";
    public const string TextTertiary = "#858580";
    public const string Accent = "#E4483D";
    public const string Border = "#2A2A28";
    public const string Hover = "#252524";
    public const string Pressed = "#070707";
    public const string Selected = "#20201E";
    public const string Disabled = "#666662";
    public const string Error = "#E87870";
    public const string Warning = "#D9B86C";
    public const string Success = "#9EBCAD";
    public const string FontFamily = "Wanted Sans";
    public const string MonoFontFamily = "JetBrains Mono";
    public const string LightBackground = "#EAECE9";
    public const string LightSurface = "#F4F3EF";
    public const string LightElevated = "#E7E9E5";
    public const string LightTextPrimary = "#1C2224";
    public const string LightTextSecondary = "#58615F";
    public const string LightBorder = "#C4C9C4";

    public const double PanelRadius = 10, ControlRadius = 6, ControlHeight = 40, IconSize = 18;
    public const double Space4 = 4, Space8 = 8, Space12 = 12, Space16 = 16, Space24 = 24, Space32 = 32;
    public const double DisplaySize = 32, HeadingSize = 20, BodySize = 14, CaptionSize = 12, MetadataSize = 11, TimeSize = 12;
    public const int MotionCalmMilliseconds = 160, MotionDynamicMilliseconds = 240, MotionCinematicMilliseconds = 400;
    public const int MotionExitMilliseconds = 120, MotionPageMilliseconds = 120, MotionMetadataMilliseconds = 160;
    public const int MotionHoverMilliseconds = 90, MotionPressedMilliseconds = 60;
}
