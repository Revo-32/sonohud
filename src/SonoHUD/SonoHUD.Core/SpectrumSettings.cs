namespace SonoHUD.Core;

public enum SpectrumStyle { Preset, Bars, Curve, Mirror, Radial, Minimal }

// Independent from decorative motion. Nullable storage in AppSettings distinguishes
// legacy opt-in from an explicit new switch without overwriting users' preferences.
public sealed record SpectrumSettings
{
    public bool Enabled { get; set; }
    public bool Home { get; set; } = true;
    public bool Overlay { get; set; } = true;
    public bool Taskbar { get; set; } = true;
    public SpectrumStyle Style { get; set; } = SpectrumStyle.Preset;
    public double Intensity { get; set; } = 1;
    public double MinimumHz { get; set; } = 30;
    public double MaximumHz { get; set; } = 18000;
    public int Bands { get; set; } = 64;
    public double SensitivityDb { get; set; }
    public double DynamicRangeDb { get; set; } = 60;
    public double AttackMs { get; set; } = 35;
    public double ReleaseMs { get; set; } = 220;
    public bool AutoGain { get; set; } = true;
    public string DeviceId { get; set; } = "";
    public int FrameLimit { get; set; } = 60;

    public SpectrumSettings Sanitize() => this with
    {
        Style = Enum.IsDefined(Style) ? Style : SpectrumStyle.Preset,
        Intensity = Finite(Intensity, .4, 1.6, 1),
        MinimumHz = Finite(MinimumHz, 20, 1000, 30),
        MaximumHz = Finite(MaximumHz, 2000, 22000, 18000),
        Bands = Math.Clamp(Bands, 12, 96),
        SensitivityDb = Finite(SensitivityDb, -18, 18, 0),
        DynamicRangeDb = Finite(DynamicRangeDb, 24, 90, 60),
        AttackMs = Finite(AttackMs, 5, 250, 35),
        ReleaseMs = Finite(ReleaseMs, 30, 1500, 220),
        DeviceId = DeviceId is { Length: <= 512 } ? DeviceId : "",
        FrameLimit = FrameLimit is 60 or 120 or 144 or 165 or 240 ? FrameLimit : 60
    };
    static double Finite(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
