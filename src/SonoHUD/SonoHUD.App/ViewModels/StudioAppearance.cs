using System.Windows.Media;
using System.Windows.Media.Imaging;
using SonoHUD.Core;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel
{
    internal bool LyricsLayout { get; private set; }
    internal AppearanceSettings Appearance => Document.Settings.Appearance ?? new(Accent: LegacyAccent());
    string? LegacyAccent()
    {
        var rgb="#"+Document.Overlay.AccentColor[^6..];
        return rgb.Equals(DesignTokens.Accent,StringComparison.OrdinalIgnoreCase)?null:rgb;
    }
    AppearanceSettings? resolvedSettings;
    MusicTheme? palette;
    string? artKey, artColor;
    internal MusicTheme Palette
    {
        get
        {
            var options=Appearance;
            var key=options.ArtworkAccent?Media.State.ArtworkKey:null;
            if(palette is not null && resolvedSettings==options && artKey==key)return palette;
            if(key!=artKey || palette is null)artColor=key is null?null:ArtworkColor(AlbumArt);
            artKey=key;resolvedSettings=options;
            return palette=MusicTheme.Resolve(options,artColor);
        }
    }
    internal void SetAppearance(AppearanceSettings value)=>Change("전체 테마 저장",d=>d.Settings.Appearance=value);
    internal Scene CreatePreview(OverlayConfig config,bool sampleLyrics=false)=>OverlayPresets.Create(config,SpectrumOptions,Palette,LyricsLayout || sampleLyrics, LyricsProviderName);
    static string? ArtworkColor(BitmapSource? art)
    {
        if(art is null || art.PixelWidth==0 || art.PixelHeight==0)return null;
        var scaled=new TransformedBitmap(art,new ScaleTransform(24d/art.PixelWidth,24d/art.PixelHeight));
        var rgb=new FormatConvertedBitmap(scaled,PixelFormats.Bgra32,null,0);
        byte[] pixels=new byte[rgb.PixelWidth*rgb.PixelHeight*4];rgb.CopyPixels(pixels,rgb.PixelWidth*4,0);
        var bins=new Dictionary<int,(int Count,int R,int G,int B)>();
        for(int i=0;i<pixels.Length;i+=4){
            if(pixels[i+3]<160)continue;
            int r=pixels[i+2],g=pixels[i+1],b=pixels[i],max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b));
            if(max<45 || min>225 || max-min<16)continue;
            int key=(r>>5)*64+(g>>5)*8+(b>>5);bins.TryGetValue(key,out var bin);
            bins[key]=(bin.Count+1,bin.R+r,bin.G+g,bin.B+b);
        }
        if(bins.Count==0)return null;
        var best=bins.Values.MaxBy(b=>b.Count);
        var color=$"#{best.R/best.Count:X2}{best.G/best.Count:X2}{best.B/best.Count:X2}";
        return MusicTheme.Mix(color,"#999999",.15);
    }
}