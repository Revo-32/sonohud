using SonoHUD.App.Platform;
using SonoHUD.Core;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel
{
    public sealed record SpectrumChoice<T>(T Value, string Label);
    internal SpectrumSettings SpectrumOptions => Document.Settings.EffectiveSpectrum;
    void ChangeSpectrum(Func<SpectrumSettings,SpectrumSettings> change) => Change("스펙트럼 설정 변경", d => { d.Settings.Spectrum=change(d.Settings.EffectiveSpectrum).Sanitize(); });
    public bool SpectrumEnabled { get=>SpectrumOptions.Enabled; set {if(value!=SpectrumEnabled)ChangeSpectrum(s=>s with{Enabled=value});} }
    public bool SpectrumHome { get=>SpectrumOptions.Home; set {if(value!=SpectrumHome)ChangeSpectrum(s=>s with{Home=value});} }
    public bool SpectrumOverlay { get=>SpectrumOptions.Overlay; set {if(value!=SpectrumOverlay)ChangeSpectrum(s=>s with{Overlay=value});} }
    public bool SpectrumTaskbar { get=>SpectrumOptions.Taskbar; set {if(value!=SpectrumTaskbar)ChangeSpectrum(s=>s with{Taskbar=value});} }
    public bool HomeSpectrumVisible => SpectrumEnabled && SpectrumHome;
    public SpectrumStyle SelectedSpectrumStyle { get=>SpectrumOptions.Style; set {if(value!=SelectedSpectrumStyle)ChangeSpectrum(s=>s with{Style=value});} }
    public double SpectrumIntensity { get=>SpectrumOptions.Intensity; set {if(value!=SpectrumIntensity)ChangeSpectrum(s=>s with{Intensity=value});} }
    public double SpectrumMinimumHz { get=>SpectrumOptions.MinimumHz; set {if(value!=SpectrumMinimumHz)ChangeSpectrum(s=>s with{MinimumHz=value});} }
    public double SpectrumMaximumHz { get=>SpectrumOptions.MaximumHz; set {if(value!=SpectrumMaximumHz)ChangeSpectrum(s=>s with{MaximumHz=value});} }
    public int SpectrumBands { get=>SpectrumOptions.Bands; set {if(value!=SpectrumBands)ChangeSpectrum(s=>s with{Bands=value});} }
    public double SpectrumSensitivity { get=>SpectrumOptions.SensitivityDb; set {if(value!=SpectrumSensitivity)ChangeSpectrum(s=>s with{SensitivityDb=value});} }
    public double SpectrumRange { get=>SpectrumOptions.DynamicRangeDb; set {if(value!=SpectrumRange)ChangeSpectrum(s=>s with{DynamicRangeDb=value});} }
    public double SpectrumAttack { get=>SpectrumOptions.AttackMs; set {if(value!=SpectrumAttack)ChangeSpectrum(s=>s with{AttackMs=value});} }
    public double SpectrumRelease { get=>SpectrumOptions.ReleaseMs; set {if(value!=SpectrumRelease)ChangeSpectrum(s=>s with{ReleaseMs=value});} }
    public bool SpectrumAutoGain { get=>SpectrumOptions.AutoGain; set {if(value!=SpectrumAutoGain)ChangeSpectrum(s=>s with{AutoGain=value});} }
    public int SpectrumFrameLimit { get=>SpectrumOptions.FrameLimit; set {if(value!=SpectrumFrameLimit)ChangeSpectrum(s=>s with{FrameLimit=value});} }
    public string SpectrumDevice { get=>SpectrumOptions.DeviceId; set {if(value is not null && value!=SpectrumDevice)ChangeSpectrum(s=>s with{DeviceId=value});} }
    public string SpectrumStatus { get; internal set; } = "꺼짐";
    internal void NotifySpectrum() => Notify(nameof(SpectrumStatus));
    public IReadOnlyList<SpectrumOutput> SpectrumOutputs { get; private set; } = [new("","Windows 기본 출력 따라가기")];
    internal void SetSpectrumOutputs(IReadOnlyList<SpectrumOutput> devices)
    {
        SpectrumOutputs = devices.Any(d=>d.Id==SpectrumDevice) ? devices : [..devices,new(SpectrumDevice,"연결되지 않은 저장 장치 · 재연결 대기")];
        Notify(nameof(SpectrumOutputs)); Notify(nameof(SpectrumDevice));
    }
    public IReadOnlyList<SpectrumChoice<SpectrumStyle>> SpectrumStyles { get; } = [new(SpectrumStyle.Preset,"디자인에 맞게"),new(SpectrumStyle.Bars,"주파수 막대"),new(SpectrumStyle.Curve,"부드러운 곡선"),new(SpectrumStyle.Mirror,"대칭 스펙트럼"),new(SpectrumStyle.Minimal,"작은 주파수선")];
    public IReadOnlyList<int> SpectrumBandChoices { get; } = [24,32,48,64,96];
    public IReadOnlyList<int> SpectrumFrameChoices { get; } = [60,120,144,165,240];
}
