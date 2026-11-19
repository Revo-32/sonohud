using System.Windows.Threading;
using System.IO;
using System.Text.Json;
using SonoHUD.PluginContracts;
using SonoHUD.App.Plugins;
using SonoHUD.App.Media;
using SonoHUD.Core;
using SonoHUD.App.Rendering;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel
{
    internal OnlineLyrics Lyrics {get;private set;}=null!;
    SpectrumFrameClock? lyricsClock;
    bool lyricsStarted;
    bool lyricsPresentationVisible=true;
    internal PluginHost Plugins { get; private set; }=null!;
    ILyricsProvider? attachedProvider;
    string? providerSettingsPath;
    LyricsSettings? isolatedPreferences;
    public bool HasLyricsProvider=>attachedProvider is not null;
    public string LyricsProviderName=>Plugins?.LyricsManifest is {} m ? m.ProviderName ?? m.Name : attachedProvider is null ? "" : "가사 제공자";
    public string LyricsProviderDisclosure=>Plugins?.LyricsManifest?.Disclosure??"";
    LyricsSettings? lyricsSettingsInput;
    LyricsSettings lyricsSettingsCache = new();
    internal LyricsSettings LyricsOptions
    {
        get
        {
            if(isolatedPreferences is not null)return isolatedPreferences;
            if(!ReferenceEquals(lyricsSettingsInput,Document.Settings.Lyrics)){lyricsSettingsInput=Document.Settings.Lyrics;lyricsSettingsCache=(lyricsSettingsInput??new()).Sanitize();}
            return lyricsSettingsCache;
        }
    }
    internal PlaybackState LyricsPreviewPlayback => Media.State with { PreviewLyrics=Lyrics.Document, PreviewLyricFrame=Lyrics.Frame, Lyrics = new(){Kind=LyricsKind.PlainText,PlainText=string.Join('\n',Lyrics.Document.Lines.Skip(Math.Max(0,Lyrics.Frame.Current-1)).Take(3).Select(x=>x.Text)),IsDemo=Media.State.IsDemo} };
    public bool OnlineLyricsEnabled
    {
        get=>LyricsOptions.OnlineEnabled;
        set{SaveLyricsOptions(LyricsOptions with{OnlineEnabled=value});ObserveLyrics();Notify(nameof(OnlineLyricsEnabled));}
    }
    void SaveLyricsOptions(LyricsSettings value)
    {
        if(providerSettingsPath is null)
        {
            if(attachedProvider is not null)Change("가사 설정 저장",d=>d.Settings.Lyrics=value);
            return;
        }
        string temp=providerSettingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var clean=value.Sanitize();
            File.WriteAllText(temp,JsonSerializer.Serialize(clean));
            File.Move(temp,providerSettingsPath,true);
            isolatedPreferences=clean;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {Status="확장 설정을 저장하지 못했습니다. 데이터 폴더의 쓰기 권한을 확인하세요.";Log.Error("확장 설정",e);}
        finally {try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal void InitializeLyrics(ILyricsProvider? provider=null)
    {
        if(Plugins is null)
        {
            Plugins=new PluginHost(Repository.DirectoryPath);
            Plugins.Changed+=OnPluginsChanged;
        }
        if(Lyrics is null)
        {
            Lyrics=new(null,()=>LyricsOptions,preference=>{
                var options=LyricsOptions with{Tracks=LyricsOptions.Tracks.Where(x=>x.TrackKey!=preference.TrackKey).Append(preference).ToList()};
                SaveLyricsOptions(options);
            });
            Lyrics.Changed+=RefreshLyricsCadence;
        }
        AttachProvider(provider??Plugins.Provider);
    }
    void OnPluginsChanged()
    {
        if(!ReferenceEquals(attachedProvider,Plugins.Provider))AttachProvider(Plugins.Provider);
        NotifyAll();Changed?.Invoke();
    }
    void AttachProvider(ILyricsProvider? provider)
    {
        attachedProvider=provider;
        providerSettingsPath=null;isolatedPreferences=null;
        if(provider is not null && ReferenceEquals(provider,Plugins.Provider) && Plugins.LyricsManifest is {} m)
        {
            try
            {
                providerSettingsPath=Plugins.SettingsFile(m.Id);
                if(File.Exists(providerSettingsPath))
                {
                    PluginPackage.CheckLink(File.GetAttributes(providerSettingsPath));
                    if(new FileInfo(providerSettingsPath).Length>256*1024) throw new InvalidDataException("확장 설정 크기 제한");
                    isolatedPreferences=JsonSerializer.Deserialize<LyricsSettings>(File.ReadAllText(providerSettingsPath))?.Sanitize()??new(){OnlineEnabled=false};
                }
                else
                {
                    isolatedPreferences=m.ImportLegacyLyricsPreferences ? (Document.Settings.Lyrics??new()).Sanitize() : new(){OnlineEnabled=false};
                    SaveLyricsOptions(isolatedPreferences);
                }
            }
            catch(Exception e) when(PluginHost.Recoverable(e)){isolatedPreferences=new(){OnlineEnabled=false};Log.Error("확장 설정 읽기",e);}
        }
        Lyrics.ReplaceProvider(provider);
        ObserveLyrics(); NotifyAll();
    }
    internal void StartOnlineLyrics()
    {
        if(lyricsStarted || stopped)return;lyricsStarted=true;
        ObserveLyrics();
    }
    internal void ObserveLyrics()
    {
        if(!lyricsStarted)return;
        var p=Media.State;
        var track=p.Source.Length==0 || p.TrackTagsMissing ? null : new LyricTrack(p.Title,p.Artist,p.AlbumTitle,p.DurationSeconds);
        _=Lyrics.Observe(track,Media.LyricsSessionIdentity,p.IsDemo,p.Lyrics.PlainText);
        RefreshLyricsCadence();
    }
    internal void SetLyricsPresentationVisible(bool visible)
    {
        if(lyricsPresentationVisible==visible)return;
        lyricsPresentationVisible=visible;
        if(lyricsStarted && !stopped)RefreshLyricsCadence();
    }
    void RefreshLyricsCadence()
    {
        if (Lyrics.Status != LyricsStatus.Searching)
        {
            bool next=Lyrics.Document.Lines.Count>0;
            if(next!=LyricsLayout){LyricsLayout=next; Notify(nameof(LyricsLayout)); Changed?.Invoke();}
        }
        bool animate=lyricsStarted && !stopped && lyricsPresentationVisible && Lyrics.Document.Timed && Media.State.IsPlaying;
        if(animate)lyricsClock??=new(Dispatcher.CurrentDispatcher,60,ObserveLyricsFrame);
        else{lyricsClock?.Dispose();lyricsClock=null;}
        if(lyricsPresentationVisible)ObserveLyricsFrame();
    }
    internal void ObserveLyricsFrame()
    {
        var p=Media.State;double? position=Media.DisplayPositionNow;
        bool ended=p.Status is "정지" or "플레이어 닫힘" || p.DurationSeconds is double end && position>=end;
        Lyrics.Tick(ended?null:position,Media.SeekUncertain || !ended && p.IsPlaying && Media.LyricsTimelineUncertain);
    }
    void StopLyrics(){lyricsClock?.Dispose();lyricsClock=null;Lyrics.Dispose();Plugins.Changed-=OnPluginsChanged;Plugins.Dispose();}
}
