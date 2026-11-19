using SonoHUD.Core;
using SonoHUD.PluginContracts;

namespace SonoHUD.App.Media;

internal enum LyricsStatus { Disabled, Waiting, Searching, Ready, Candidates, NotFound, Instrumental, Error, Demo }
// UI-thread coordinator. Providers do I/O off-thread; generation checks protect every publication.
internal sealed class OnlineLyrics : IDisposable
{
    ILyricsProvider? provider;
    readonly Func<LyricsSettings> settings;
    readonly Action<LyricsPreference> save;
    readonly Dictionary<string,(DateTimeOffset At,LyricDocument Doc)> cache=[];
    readonly Dictionary<string,(DateTimeOffset At,IReadOnlyList<LyricRecord> Choices)> recentSearches=[];
    static string LookupKey(LyricTrack t)=>t.Key+"|"+(t.Duration is >0 ? Math.Round(t.Duration.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
    CancellationTokenSource? request;
    int generation;
    bool disposed;
    string session="";
    string? trackKey;
    int preferenceIndex=-1;
    LyricDocument? clockDocument;
    double rangeStart, rangeEnd;
    int rangeLine;
    internal LyricTrack? Track {get;private set;}
    internal LyricDocument Document {get;private set;}=LyricDocument.Empty;
    internal LyricFrame Frame {get;private set;}=new(null,-1,false);
    internal LyricsStatus Status {get;private set;}=LyricsStatus.Waiting;
    internal string Message {get;private set;}="음악을 재생하면 가사를 검색합니다.";
    internal IReadOnlyList<LyricRecord> Candidates {get;private set;}=[];
    LyricsPreference? Preference
    {
        get
        {
            if(trackKey is null)return null;
            var preferences=settings().Tracks;
            if(preferenceIndex>=0 && preferenceIndex<preferences.Count && preferences[preferenceIndex].TrackKey==trackKey)
                return preferences[preferenceIndex];
            for(int i=0;i<preferences.Count;i++)
                if(preferences[i].TrackKey==trackKey){preferenceIndex=i;return preferences[i];}
            return null;
        }
    }
    internal int OffsetMs=>Preference?.OffsetMilliseconds??0;
    internal event Action? Changed;
    internal event Action? FrameChanged;
    internal OnlineLyrics(ILyricsProvider? provider,Func<LyricsSettings> settings,Action<LyricsPreference> save){this.provider=provider;this.settings=settings;this.save=save;}
    internal void ReplaceProvider(ILyricsProvider? value)
    {
        Cancel();
        provider=value;Track=null;trackKey=null;preferenceIndex=-1;session="";
        Document=LyricDocument.Empty;Frame=new(null,-1,false);Candidates=[];
        cache.Clear();recentSearches.Clear();
        Set(value is null?LyricsStatus.Disabled:LyricsStatus.Waiting,"");
    }
    internal async Task Observe(LyricTrack? track,string sessionIdentity,bool demo=false,string? sample=null)
    {
        if(disposed || provider is null)return;
        bool enabled=settings().OnlineEnabled;
        bool sameTags=Track?.Title==track?.Title && Track?.Artist==track?.Artist && Track?.Album==track?.Album;
        string? nextKey=sameTags?trackKey:track?.Key;
        bool same=trackKey==nextKey && session==sessionIdentity;
        if(same && track is { Duration:null } && Track?.Duration is double lastDuration)track=track with{Duration=lastDuration};
        if(same && (track?.Duration==Track?.Duration || Math.Abs((track?.Duration??-10)-(Track?.Duration??-20))<=2) && (demo?Status==LyricsStatus.Demo:enabled?Status!=LyricsStatus.Disabled:Status==LyricsStatus.Disabled))return;
        Cancel(); Track=track;trackKey=nextKey;preferenceIndex=-1;session=sessionIdentity;Document=LyricDocument.Empty;Frame=new(null,-1,false);Candidates=[];
        if(demo){Document=LyricParser.Plain(sample);Set(LyricsStatus.Demo,"SonoHUD가 직접 작성한 샘플입니다. 현재 음악의 가사가 아닙니다.");return;}
        if(!enabled){cache.Clear();recentSearches.Clear();Set(LyricsStatus.Disabled,"가사 옵션에서 검색을 켜면 현재 곡 정보를 활성 가사 제공자에 보냅니다.");return;}
        if(track is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist)){Set(LyricsStatus.Waiting,"곡명과 아티스트를 기다립니다.");return;}
        string key=LookupKey(track);
        if(recentSearches.TryGetValue(key,out var recent) && DateTimeOffset.UtcNow-recent.At<TimeSpan.FromMinutes(2)){ShowChoices(recent.Choices);return;}
        if(cache.TryGetValue(key,out var cached) && DateTimeOffset.UtcNow-cached.At<TimeSpan.FromMinutes(10)){Apply(cached.Doc);return;}
        await Query(false);
    }
    internal Task Retry(bool candidates=false)=>disposed || provider is null || Track is null || !settings().OnlineEnabled || Status==LyricsStatus.Demo ? Task.CompletedTask:Query(candidates);
    async Task Query(bool candidates)
    {
        Cancel();var track=Track!;int epoch=generation;request=new();var token=request.Token;
        // An explicit revalidation replaces the cached body, including 404/removal results.
        // A later track return must not resurrect lyrics invalidated by the provider.
        string key=LookupKey(track);cache.Remove(key);recentSearches.Remove(key);
        Document=LyricDocument.Empty;Frame=new(null,-1,false);Candidates=[];Set(LyricsStatus.Searching,"가사를 찾고 있습니다…");
        bool Current()=>!disposed && !token.IsCancellationRequested && epoch==generation && settings().OnlineEnabled;
        try
        {
            var lookup=await provider!.LookupAsync(track,Preference,candidates,token);
            if(!Current())return;
            var match=lookup.Match;
            if(match is null)
            {
                Candidates=lookup.Candidates.Take(32).Select(x=>x.Summary()).ToArray();
                if(!candidates)
                {
                    if(recentSearches.Count>=32)recentSearches.Remove(recentSearches.MinBy(x=>x.Value.At).Key);
                    recentSearches[key]=(DateTimeOffset.UtcNow,Candidates);
                }
                ShowChoices(Candidates);return;
            }
            var doc=await Task.Run(()=>LyricParser.Parse(match),token);if(Current()){Remember(LookupKey(track),doc);Apply(doc);}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception error) when(Plugins.PluginHost.Recoverable(error))
        {if(Current())Set(LyricsStatus.Error,error is LyricsRequestException ? error.Message:"가사 데이터를 읽을 수 없습니다. 다시 검색해 주세요.");}
    }
    internal async Task Select(long id)
    {
        if(disposed || provider is null || Track is null || !Candidates.Any(x=>x.Id==id) || !settings().OnlineEnabled)return;
        Cancel();var track=Track;int epoch=generation;request=new();var token=request.Token;Set(LyricsStatus.Searching,"선택한 가사를 불러옵니다…");
        try
        {
            var record=await provider.SelectAsync(id,token);if(record is null)throw new LyricsRequestException("선택한 가사가 더 이상 제공되지 않습니다. 다시 검색해 주세요.");
            var doc=await Task.Run(()=>LyricParser.Parse(record),token);
            if(disposed || token.IsCancellationRequested || generation!=epoch)return;
            save(new(track.Key,id,OffsetMs,new LyricTrack(record.TrackName,record.ArtistName,record.AlbumName,record.Duration).Key));Remember(LookupKey(track),doc);Apply(doc);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception e) when(Plugins.PluginHost.Recoverable(e)){if(!disposed && generation==epoch)Set(LyricsStatus.Error,e is LyricsRequestException?e.Message:"선택한 가사를 읽지 못했습니다.");}
    }
    internal void Offset(int milliseconds)
    {
        if(trackKey is null)return;var existing=Preference;
        save(new(trackKey,existing?.RecordId,Math.Clamp(milliseconds,-10000,10000),existing?.RecordKey));Changed?.Invoke();
    }
    internal void Tick(double? position,bool uncertain)
    {
        // During an unconfirmed seek keep the last known highlight; never pretend target acceptance is confirmation.
        var next=uncertain?Frame with{Uncertain=true}:FrameAt(position);
        if(Frame!=next){Frame=next;FrameChanged?.Invoke();}
    }
    LyricFrame FrameAt(double? position)
    {
        if(!Document.Timed || position is not double seconds || !double.IsFinite(seconds))return new(null,-1,false);
        double at=seconds+OffsetMs/1000d;
        if(!ReferenceEquals(clockDocument,Document) || at<rangeStart || at>=rangeEnd)
        {
            // The active line cannot change between consecutive start/end boundaries.
            // Re-evaluate the canonical overlap/gap rule after seeking in either direction.
            clockDocument=Document;rangeStart=double.NegativeInfinity;rangeEnd=double.PositiveInfinity;
            for(int i=0;i<Document.Lines.Count;i++)
            {
                var line=Document.Lines[i];
                if(line.Start is double start)Include(start);
                if(line.End is double end)Include(end);
            }
            rangeLine=LyricFrame.At(Document,at).Current;
        }
        return new(at,rangeLine,false);

        void Include(double boundary)
        {
            if(boundary<=at)rangeStart=Math.Max(rangeStart,boundary);
            else rangeEnd=Math.Min(rangeEnd,boundary);
        }
    }
    void ShowChoices(IReadOnlyList<LyricRecord> choices){Candidates=choices;Set(choices.Count>0?LyricsStatus.Candidates:LyricsStatus.NotFound,choices.Count>0?"같은 버전의 곡을 선택해 주세요. 제목·아티스트·재생시간을 확인하세요.":"이 곡의 가사를 찾지 못했습니다.");}
    void Remember(string key,LyricDocument doc){recentSearches.Remove(key);if(cache.Count>=4)cache.Remove(cache.OrderBy(x=>x.Value.At).First().Key);cache[key]=(DateTimeOffset.UtcNow,doc);}
    void Apply(LyricDocument doc){Document=doc;Set(doc.Format==LyricFormat.Instrumental?LyricsStatus.Instrumental:doc.Format==LyricFormat.None?LyricsStatus.NotFound:LyricsStatus.Ready,doc.Format==LyricFormat.Instrumental?"보컬 가사가 없는 연주곡입니다.":doc.Format==LyricFormat.None?"이 곡에 표시할 가사가 없습니다.":doc.Warning);}
    void Set(LyricsStatus status,string message){Status=status;Message=message;Changed?.Invoke();FrameChanged?.Invoke();}
    void Cancel(){generation++;clockDocument=null;request?.Cancel();request?.Dispose();request=null;}
    public void Dispose(){if(disposed)return;disposed=true;Cancel();cache.Clear();recentSearches.Clear();Document=LyricDocument.Empty;Candidates=[];provider=null;Changed=null;FrameChanged=null;}
}
