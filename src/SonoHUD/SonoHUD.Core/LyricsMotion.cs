namespace SonoHUD.Core;

public readonly record struct LyricsMotion(int Milliseconds,double Distance,double StartOpacity)
{
    public static LyricsMotion Between(AnimationProfile profile,bool allowed,LyricFrame before,LyricFrame next)
    {
        if(!allowed || before.Uncertain || next.Uncertain || before.Current<0 || next.Current!=before.Current+1 ||
            next.Position-before.Position is not (>=0 and <=.35))return default;
        return profile switch{
            AnimationProfile.Calm=>new(180,3,.82),
            AnimationProfile.Dynamic=>new(140,7,.68),
            AnimationProfile.Cinematic=>new(300,10,.58),
            _=>default
        };
    }
}