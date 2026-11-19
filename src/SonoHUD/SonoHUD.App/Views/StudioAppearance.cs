using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

internal static class StudioAppearance
{
    internal static void Apply(ResourceDictionary resources,MusicTheme p,bool transition=false)
    {
        var colors=new Dictionary<string,string>{
            ["Background"]=p.Background,["Panel"]=p.Surface,["Elevated"]=p.Raised,["Line"]=p.Border,
            ["Ink"]=p.Ink,["Muted"]=p.Secondary,["Tertiary"]=p.Muted,["Accent"]=p.Accent,["OnAccent"]=p.OnAccent,
            ["Selected"]=p.Selected,["Hover"]=p.Hover,["Pressed"]=p.Pressed,["Focus"]=p.Accent,
            ["Disabled"]=p.Muted,["Error"]=p.Error,["Warning"]=p.Warning,["Success"]=p.Success,
            ["Progress"]=p.Progress,["Playhead"]=p.Playhead,["ProgressTrack"]=p.ProgressTrack,["Spectrum"]=p.Spectrum,
            ["LyricsCurrent"]=p.LyricsCurrent,["LyricsContext"]=p.LyricsContext,["LyricsWord"]=p.LyricsWord
        };
        foreach(var (role,value) in colors){
            string key="Studio"+role;
            var color=(Color)ColorConverter.ConvertFromString(value);
            var brush=new SolidColorBrush(color);
            if(transition && role=="Accent" && resources[key] is SolidColorBrush old && old.Color!=color)
                brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(old.Color,color,TimeSpan.FromMilliseconds(240)){FillBehavior=FillBehavior.Stop});
            else brush.Freeze();
            resources[key]=brush;
        }
    }
}
