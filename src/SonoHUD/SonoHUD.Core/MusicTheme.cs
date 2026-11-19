namespace SonoHUD.Core;

public sealed record AppearanceSettings(string Theme = MusicTheme.PrismName, string? Accent = null, string? Spectrum = null, bool ArtworkAccent = false)
{
    internal AppearanceSettings MigrateTheme()
    {
        string name = Theme switch
        {
            "Rail Mint" => "Moss Paper",
            "Graphite Blue" => "Silverprint",
            "Nocturne" => "Night Ferry",
            "Studio Amber" or "Carbon Signal" => MusicTheme.PrismName,
            _ => Theme
        };
        return name == Theme ? this : this with { Theme = name };
    }
}

public sealed record MusicTheme(string Name, string Background, string Surface, string Raised,
    string Ink, string Secondary, string Muted, string Border, string Accent, string Spectrum,
    string Progress, string LyricsWord)
{
    public const string PrismName = "Prism RGB";
    public bool IsPrism => Name == PrismName;
    public static IReadOnlyList<MusicTheme> Gallery { get; } = [
        new(PrismName, "#101010", "#191919", "#272727", "#F8F8FA", "#CDCDD2", "#B0B0B6", "#4D4D4D", "#ECECF2", "#69EDFF", "#6AF1E3", "#FFE595") { Playhead = "#FF80C6", SpectrumGradient=true },
        new("Silverprint",   "#D8DDE1", "#F1F3F4", "#E3E7EA", "#1C232B", "#4F5B64", "#4F5B64", "#9DA7B0", "#244A88", "#54677B", "#244A88", "#224680"),
        new("Night Ferry",   "#151C2B", "#222C40", "#2E3A50", "#F1EFE8", "#B9C2CC", "#B9C2CC", "#53617A", "#DBC69F", "#A5B4C7", "#CAD1D8", "#E3CFA9") { Playhead = "#DBC69F" },
        new("Moss Paper",    "#CFD1C0", "#E7E8DA", "#D9DCCC", "#292E25", "#515A48", "#515A48", "#929982", "#414F2E", "#626E52", "#3C4531", "#475F2D") { Playhead = "#414F2E" },
        new("Monochrome",    "#101112", "#252628", "#38393B", "#F6F6F3", "#D3D3CF", "#B5B6B6", "#616264", "#F0F0ED", "#C2C4C3", "#E1E1DC", "#FFFFFF"),
        new("Porcelain",     "#EEE9DF", "#FFFCF5", "#E6DED1", "#30291F", "#554B40", "#665A4F", "#9F9284", "#813F2F", "#71613E", "#4D4134", "#783D29")
    ];
    public bool SpectrumGradient {get; init;}
    public bool Light => Contrast(Surface, "#000000") > Contrast(Surface, "#FFFFFF");
    public string Playhead { get; init; } = Progress;
    public string Selected => Mix(Raised, Ink, Light ? .10 : .09);
    public string Hover => Mix(Surface, Ink, .10);
    public string Pressed => Mix(Surface, Ink, .17);
    public string ProgressTrack => Border;
    public string LyricsCurrent => Ink;
    public string LyricsContext => Secondary;
    public string Error => Light ? "#922B37" : "#FFA6A0";
    public string Warning => Light ? "#694B15" : "#ECD091";
    public string Success => Light ? "#286047" : "#A9D4AD";
    public string OnAccent => Contrast(Accent,"#101010") >= Contrast(Accent,"#FFFFFF") ? "#101010" : "#FFFFFF";
    public static bool IsRgb(string? hex) => hex is { Length: 7 } && hex[0]=='#' && hex.Skip(1).All(Uri.IsHexDigit);
    public static double Contrast(string a,string b)
    {
        static double Luma(string s)
        {
            double Channel(int offset) { double n=Convert.ToByte(s.Substring(offset,2),16)/255d; return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4); }
            return .2126*Channel(1)+.7152*Channel(3)+.0722*Channel(5);
        }
        var x=Luma(a);var y=Luma(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    public static string Mix(string a,string b,double amount)
    {
        int C(int i) => (int)Math.Round(Convert.ToByte(a.Substring(i,2),16)*(1-amount)+Convert.ToByte(b.Substring(i,2),16)*amount);
        return $"#{C(1):X2}{C(3):X2}{C(5):X2}";
    }
    public static MusicTheme Resolve(AppearanceSettings settings,string? artwork)
    {
        string name = settings.MigrateTheme().Theme;
        var theme=Gallery.FirstOrDefault(x=>x.Name==name)??Gallery[0];
        string Safe(string? input,string fallback,double ratio)
        {
            if(!IsRgb(input))return fallback;
            for(int n=0;n<=20;n++){
                var shade=Mix(input!,theme.Light?"#000000":"#FFFFFF",n/20d);
                if(Contrast(shade,theme.Raised)>=ratio && Contrast(shade,theme.Surface)>=ratio && Contrast(shade,theme.Background)>=ratio)return shade;
            }
            return fallback;
        }
        string? accent=settings.Accent;
        if(settings.ArtworkAccent && IsRgb(artwork))
        {
            // Keep the cover hue within the theme's colorfulness and luminance range.
            int[] channels=[Convert.ToInt32(artwork![1..3],16),Convert.ToInt32(artwork[3..5],16),Convert.ToInt32(artwork[5..7],16)];
            int[] target=[Convert.ToInt32(theme.Accent[1..3],16),Convert.ToInt32(theme.Accent[3..5],16),Convert.ToInt32(theme.Accent[5..7],16)];
            double Luma(int[] c)=>.2126*c[0]+.7152*c[1]+.0722*c[2];
            double midpoint=Luma(target), center=Luma(channels);
            double strength=Math.Min(1,(target.Max()-target.Min())/(double)Math.Max(1,channels.Max()-channels.Min()));
            int Tone(int channel)=>(int)Math.Clamp(Math.Round(midpoint+(channel-center)*strength),0,255);
            accent=$"#{Tone(channels[0]):X2}{Tone(channels[1]):X2}{Tone(channels[2]):X2}";
        }
        return theme with { Accent=Safe(accent,theme.Accent,4.5), Spectrum=Safe(settings.Spectrum,theme.Spectrum,3), SpectrumGradient=theme.IsPrism && !IsRgb(settings.Spectrum) };
    }
}
