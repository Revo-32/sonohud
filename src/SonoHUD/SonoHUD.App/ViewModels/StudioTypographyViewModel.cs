using System.Windows.Media;
using SonoHUD.App.Views;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel
{
    public sealed record WeightOption(int Value,string Label);
    public WeightOption[] TitleWeights { get; } = [new(0,"디자인 기본"),new(400,"보통 · 400"),new(500,"중간 · 500"),new(600,"약간 굵게 · 600"),new(700,"굵게 · 700")];
    public string FontCoverage => FontCatalog.Describe(FontFamily);
    public FontFamily PreviewFontFamily => FontCatalog.Resolve(FontFamily);
    public double TextSizePercent
    {
        get => Document.Overlay.TextScale*100;
        set { if(double.IsFinite(value))Change("글자 크기 변경",d=>d.Overlay.TextScale=Math.Clamp(Math.Round(value/5)*5,90,115)/100); }
    }
    public string TextSizeLabel => $"{TextSizePercent:0}%";
    public int TitleWeight
    {
        get => Document.Overlay.TitleWeight;
        set { if(TitleWeights.Any(o=>o.Value==value))Change("곡명 굵기 변경",d=>d.Overlay.TitleWeight=value); }
    }
}
