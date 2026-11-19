using System.Security.Cryptography;
using System.Windows.Threading;
using SonoHUD.App.Platform;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SonoHUD.App.Media;

internal sealed record MediaSourceCandidate(string Key, string SourceAppId, string Label);

internal sealed class MediaService : IDisposable
{
    readonly Dispatcher dispatcher; readonly AppLog log; readonly CancellationTokenSource lifetime = new(); readonly DispatcherTimer poll, displayTick;
    readonly PlaybackDisplayClock displayClock;
    readonly Func<CancellationToken,Task<GlobalSystemMediaTransportControlsSessionManager>> requestManager;
    Task? connection;
    CancellationTokenSource? activeRead;
    int activeReadVersion;
    GlobalSystemMediaTransportControlsSessionManager? manager;
    readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> sessions = [];
    GlobalSystemMediaTransportControlsSession? selected;
    IReadOnlyList<MediaSourceCandidate> sessionCatalog = [];
    string preferredSourceAppId = "", selectionWaitingStatus = "미디어 세션 없음";
    string selectedKey = "auto", trackKey = ""; int generation, demoTrack; bool disposed, refreshing, refreshAgain, demo, busy;
    int metadataVersion, appliedMetadataVersion;
    long observationNumber;
    byte[]? artwork; string artworkKey = ""; bool refreshArtwork = true;
    readonly SonoHUD.Core.TimelineGate timelineGate = new();
    public PlaybackState State { get; private set; } = new();
    internal PlaybackState RawState { get; private set; } = new();
    PlaybackState? pendingSeekBefore;
    double pendingSeekTarget;
    string pendingSeekIdentity = "";
    bool seekWaitEnded;
    internal void BeginSeekConfirmation(PlaybackState before, double target)
    {
        pendingSeekBefore = before;
        pendingSeekTarget = target;
        pendingSeekIdentity = SeekIdentity;
        seekWaitEnded = false;
    }
    internal void EndSeekConfirmation()
    {
        seekWaitEnded = true;
        ObserveSeekConfirmation(RawState);
    }
    void ObserveSeekConfirmation(PlaybackState snapshot)
    {
        if (pendingSeekBefore is not {} before) return;
        // After the confirmation window, follow a newer position even when the player chose
        // a different one. Only a position near the request can confirm success.
        if (PlaybackTimeline.Confirms(before, snapshot, pendingSeekTarget) ||
            (seekWaitEnded && !snapshot.IsPositionEstimated && snapshot.PositionSeconds.HasValue &&
             snapshot.TimelineUpdated > before.TimelineUpdated && pendingSeekIdentity == SeekIdentity))
            pendingSeekBefore = null;
    }
    internal bool SeekUncertain => pendingSeekBefore is not null && pendingSeekIdentity == SeekIdentity;
    internal double? DisplayPositionNow => demo ? State.PositionSeconds : displayClock.ProjectPosition(RawState);
    internal bool LyricsTimelineUncertain => !demo && displayClock.IsUncertain;
    internal IReadOnlyList<MediaSessionItem> Sessions { get; private set; } = [new("auto", "시스템 선택 세션")];
    internal event EventHandler? Changed;
    internal bool DemoMode => demo;
    internal bool Busy => busy;
    internal int MetadataVersion => Volatile.Read(ref metadataVersion);
    internal bool CanSeek => State.CanSeek && (demo || MetadataVersion == Volatile.Read(ref appliedMetadataVersion));
    internal string SelectedKey => selectedKey;
    internal string PreferredSourceAppId => preferredSourceAppId;
    internal string LyricsSessionIdentity => generation+"|"+selectedKey+"|"+State.Source;
    internal string SeekIdentity => string.Join('\0', generation.ToString(System.Globalization.CultureInfo.InvariantCulture), (demo ? 0 : MetadataVersion).ToString(System.Globalization.CultureInfo.InvariantCulture), selectedKey, State.Source, State.Title, State.Artist, State.AlbumTitle);
    internal MediaService(Dispatcher dispatcher, AppLog log, PlaybackDisplayClock? displayClock = null)
        : this(dispatcher,log,displayClock,token=>GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token))
    { }
    internal MediaService(Dispatcher dispatcher, AppLog log, PlaybackDisplayClock? displayClock,
        Func<CancellationToken,Task<GlobalSystemMediaTransportControlsSessionManager>> requestManager)
    {
        this.dispatcher = dispatcher; this.log = log; this.displayClock = displayClock ?? new();
        this.requestManager = requestManager;
        poll = new(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { if(manager is null) _ = StartAsync(); else RequestRefresh(); }, dispatcher); poll.Stop();
        displayTick = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        displayTick.Tick += (_, _) => TickDisplayClock();
    }
    internal Task StartAsync()
    {
        if(disposed || manager is not null)return Task.CompletedTask;
        return connection is {IsCompleted:false} ? connection : connection=ConnectAsync();
    }
    async Task ConnectAsync()
    {
        poll.Stop();
        try
        {
            var next=await requestManager(lifetime.Token);
            if(disposed)return;
            manager=next;
            manager.SessionsChanged+=SessionsChanged; manager.CurrentSessionChanged+=CurrentChanged;
            poll.Interval=TimeSpan.FromSeconds(1);poll.Start();displayTick.Start();
            RebuildSessions();if(manager is not null)RequestRefresh();
        }
        catch(OperationCanceledException) when(disposed) { }
        catch(Exception error) { if(!disposed)RetryConnection(error); }
    }
    void ReleaseManager()
    {
        var old=manager;manager=null;
        if(old is null)return;
        try{old.SessionsChanged-=SessionsChanged;}catch(Exception error){log.Error("SMTC 세션 목록 구독 종료",error);}
        try{old.CurrentSessionChanged-=CurrentChanged;}catch(Exception error){log.Error("SMTC 현재 세션 구독 종료",error);}
    }
    void RetryConnection(Exception error)
    {
        log.Error("SMTC 연결",error);
        ReleaseManager();Detach();sessions.Clear();sessionCatalog=[];
        selectedKey=preferredSourceAppId.Length==0?"auto":"app:"+Uri.EscapeDataString(preferredSourceAppId);
        UpdateSessionChoices();SetLiveState(new(){Status="미디어 연결 실패 · 자동으로 다시 연결합니다"});
        displayTick.Stop();poll.Interval=TimeSpan.FromSeconds(5);poll.Start();Changed?.Invoke(this,EventArgs.Empty);
    }
    void Dispatch(Action action) { if (!disposed && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(() => { if (!disposed) action(); }); }
    void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs __) => Dispatch(() => { if(!ReferenceEquals(sender,manager))return; RebuildSessions(); if(manager is not null)RequestRefresh(); });
    void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs __) => Dispatch(() => { if (ReferenceEquals(sender,manager) && selectedKey == "auto") { SelectCurrent(); if(manager is not null)RequestRefresh(); } });
    void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs __) { if(ReferenceEquals(sender,Volatile.Read(ref selected)))InvalidateMetadata(sender); }
    void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs __) => Dispatch(() =>
    {
        if (!demo && ReferenceEquals(sender, selected) && MetadataVersion == Volatile.Read(ref appliedMetadataVersion))
        {
            try
            {
                var info = sender.GetPlaybackInfo();
                TryPublishMetadata(RawState with { IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, PlaybackRate = info.PlaybackRate, Status = PlaybackStatus(info), CanToggle = ToggleCommand(info) is not null }, MetadataVersion);
            }
            catch { displayClock.Suspend(); }
        }
        RequestRefresh();
    });
    void TimelineChanged(GlobalSystemMediaTransportControlsSession _, TimelinePropertiesChangedEventArgs __) => Dispatch(RequestRefresh);
    internal void InvalidateMetadata() => InvalidateMetadata(null);
    void InvalidateMetadata(GlobalSystemMediaTransportControlsSession? sender)
    {
        // Invalidate before dispatching: a queued mouse-up must not commit the old track's seek.
        int version=Interlocked.Increment(ref metadataVersion);
        Dispatch(() =>
        {
            if(version!=MetadataVersion || sender is not null && !ReferenceEquals(sender,selected))return;
            if(activeReadVersion!=version)activeRead?.Cancel();
            if(!demo && version!=Volatile.Read(ref appliedMetadataVersion))
            {
                InvalidateCurrentTimeline();Changed?.Invoke(this,EventArgs.Empty);
            }
            refreshArtwork=true;RequestRefresh();
        });
    }
    internal bool TryPublishMetadata(PlaybackState snapshot, int version)
    {
        if (disposed || demo || version != MetadataVersion) return false;
        Volatile.Write(ref appliedMetadataVersion, version);
        snapshot = snapshot with { IsPositionEstimated = false, IsPlaybackRateAssumed = false };
        bool rawChanged = snapshot != RawState;
        double? previousSecond = DisplaySecond(State);
        RawState = snapshot;
        observationNumber++;
        ObserveSeekConfirmation(snapshot);
        string identity = string.Join('\0', generation.ToString(), version.ToString(), selectedKey, snapshot.Source, snapshot.Title, snapshot.Artist, snapshot.AlbumTitle);
        State = displayClock.Observe(snapshot, identity);
        if (rawChanged || previousSecond != DisplaySecond(State)) Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
    static double? DisplaySecond(PlaybackState value) => value.PositionSeconds is double seconds && double.IsFinite(seconds) ? Math.Floor(seconds) : null;
    internal void TickDisplayClock()
    {
        if (disposed || demo || MetadataVersion != Volatile.Read(ref appliedMetadataVersion)) return;
        double? previousSecond = DisplaySecond(State);
        State = displayClock.Project(RawState);
        // Keep all consumers on the same display state without rebuilding galleries at 4 Hz.
        if (previousSecond != DisplaySecond(State)) Changed?.Invoke(this, EventArgs.Empty);
    }
    void InvalidateCurrentTimeline()
    {
        timelineGate.Accept(true, State.TimelineUpdated);
        SetLiveState(State with { CanSeek = false, PositionSeconds = null, DurationSeconds = null, SeekMinimumSeconds = null, SeekMaximumSeconds = null, IsPositionEstimated = false, IsPlaybackRateAssumed = false });
    }
    internal static string? ToggleCommand(bool playing, bool supportsToggle, bool supportsPlay, bool supportsPause)
    {
        if (supportsToggle) return "toggle";
        if (playing && supportsPause) return "pause";
        if (!playing && supportsPlay) return "play";
        return null;
    }
    static string? ToggleCommand(GlobalSystemMediaTransportControlsSessionPlaybackInfo info)
    {
        var controls = info.Controls;
        return ToggleCommand(info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            controls.IsPlayPauseToggleEnabled, controls.IsPlayEnabled, controls.IsPauseEnabled);
    }
    static string PlaybackStatus(GlobalSystemMediaTransportControlsSessionPlaybackInfo info) => info.PlaybackStatus.ToString() switch { "Playing" => "재생 중", "Paused" => "일시정지", "Stopped" => "정지", "Closed" => "플레이어 닫힘", "Opened" => "재생 준비", "Changing" => "곡 전환 중", _ => "재생 상태 확인 중" };
    void SetCommandStatus(string message) { RawState = RawState with { CommandStatus = message }; State = State with { CommandStatus = message }; }
    // 실제 세션 연결은 DEMO 중에도 관리하되 화면의 샘플 상태는 유지한다.
    void SetLiveState(PlaybackState state) { if (!demo) { pendingSeekBefore=null;displayClock.Reset(); RawState = state; State = state; } }
    void Detach()
    {
        var old=selected;if(old is null)return;
        selected=null;generation++;activeRead?.Cancel();
        try{old.MediaPropertiesChanged-=MediaChanged;}catch(Exception error){log.Error("SMTC 메타데이터 구독 종료",error);}
        try{old.PlaybackInfoChanged-=PlaybackChanged;}catch(Exception error){log.Error("SMTC 재생 구독 종료",error);}
        try{old.TimelinePropertiesChanged-=TimelineChanged;}catch(Exception error){log.Error("SMTC 타임라인 구독 종료",error);}
        trackKey="";artwork=null;artworkKey="";timelineGate.Reset();refreshArtwork=true;SetLiveState(new());
    }
    void Attach(GlobalSystemMediaTransportControlsSession? value)
    {
        if (ReferenceEquals(value, selected)) return; Detach(); selected = value; if (value is null) return;
        value.MediaPropertiesChanged += MediaChanged; value.PlaybackInfoChanged += PlaybackChanged; value.TimelinePropertiesChanged += TimelineChanged;
    }
    void RebuildSessions()
    {
        if (manager is null) return;
        try
        {
            var live = manager.GetSessions().ToArray(); sessions.Clear(); var count = new Dictionary<string, int>(); var items = new List<MediaSourceCandidate>(); string? retainedKey = null;
            foreach (var s in live) { string source = s.SourceAppUserModelId; count.TryGetValue(source, out int index); count[source] = ++index; string key = source + "#" + index; sessions[key] = s; items.Add(new(key, source, source + (index > 1 ? $" ({index})" : ""))); if (ReferenceEquals(s, selected)) retainedKey = key; }
            ApplySessionCatalog(items, retainedKey);
            log.Write("media_sessions", new { count = sessions.Count });
        }
        catch (Exception e) { RetryConnection(e); }
    }
    void SelectCurrent()
    {
        try {var current = manager?.GetCurrentSession(); var matches = current is null ? [] : sessions.Values.Where(s => s.SourceAppUserModelId == current.SourceAppUserModelId).ToArray(); Attach(matches.Length == 1 ? matches[0] : null);}
        catch(Exception error){RetryConnection(error);}
    }
    // The catalog boundary can be verified without starting SMTC or controlling another process.
    internal static string? ResolvePreferredSessionKey(string sourceAppId, IReadOnlyList<MediaSourceCandidate> catalog, string? retainedKey = null)
    {
        if (sourceAppId.Length == 0) return "auto";
        var matches = catalog.Where(item => string.Equals(item.SourceAppId, sourceAppId, StringComparison.Ordinal)).ToArray();
        if (retainedKey is not null && matches.Any(item => item.Key == retainedKey)) return retainedKey;
        return matches.Length == 1 ? matches[0].Key : null;
    }
    internal void ApplySessionCatalog(IReadOnlyList<MediaSourceCandidate> catalog, string? retainedKey = null)
    {
        sessionCatalog = catalog.ToArray();
        string? resolved = ResolvePreferredSessionKey(preferredSourceAppId, sessionCatalog, retainedKey);
        selectedKey = resolved ?? "app:" + Uri.EscapeDataString(preferredSourceAppId);
        UpdateSessionChoices();
        if (selectedKey == "auto") SelectCurrent(); else Attach(resolved is null ? null : sessions.GetValueOrDefault(resolved));
        if (selected is null) SetLiveState(new() { Status = selectionWaitingStatus });
        Changed?.Invoke(this, EventArgs.Empty);
    }
    void UpdateSessionChoices()
    {
        var items = new List<MediaSessionItem> { new("auto", "시스템 선택 세션") };
        items.AddRange(sessionCatalog.Select(item => new MediaSessionItem(item.Key, item.Label)));
        int matches = sessionCatalog.Count(item => item.SourceAppId == preferredSourceAppId);
        selectionWaitingStatus = preferredSourceAppId.Length == 0 ? "미디어 세션 없음" : matches > 1 && !sessionCatalog.Any(item => item.Key == selectedKey)
            ? "선택한 앱에 여러 세션이 있습니다. 오디오에서 재생할 세션을 선택해 주세요."
            : "선택한 앱의 연결을 기다립니다. 다른 앱으로 자동 전환하지 않습니다.";
        if (selectedKey != "auto" && !items.Any(item => item.Key == selectedKey))
            items.Add(new(selectedKey, preferredSourceAppId + (matches > 1 ? " · 세션 선택 필요" : " · 연결 대기")));
        Sessions = items;
    }
    internal void RestoreSourcePreference(string? sourceAppId)
    {
        sourceAppId ??= "";
        if (preferredSourceAppId == sourceAppId) return;
        preferredSourceAppId = sourceAppId; generation++; timelineGate.Reset(); SetLiveState(new());
        ApplySessionCatalog(sessionCatalog); RequestRefresh();
    }
    internal void Select(string key)
    {
        if (disposed || string.IsNullOrEmpty(key) || key == selectedKey) return;
        string? source = key == "auto" ? "" : sessionCatalog.FirstOrDefault(item => item.Key == key)?.SourceAppId;
        if (source is null)
        {
            // Legacy callers may select a just-ended runtime key; strip only its final numeric index.
            int separator = key.LastIndexOf('#');
            source = separator > 0 && int.TryParse(key.AsSpan(separator + 1), out int index) && index > 0 ? key[..separator] : key;
        }
        preferredSourceAppId = source; selectedKey = key; generation++; timelineGate.Reset(); SetLiveState(new()); UpdateSessionChoices();
        if (key == "auto") SelectCurrent(); else Attach(sessions.GetValueOrDefault(key));
        Changed?.Invoke(this, EventArgs.Empty); RequestRefresh();
    }
    internal void SetDemo(bool value) { demo = value; generation++; activeRead?.Cancel();pendingSeekBefore=null;timelineGate.Reset(); displayClock.Reset(); State = value ? PlaybackState.Demo(demoTrack) : new(); RawState = State; Changed?.Invoke(this, EventArgs.Empty); if (!value) { refreshArtwork = true; RequestRefresh(); } }
    internal void RequestRefresh()
    {
        if (disposed || demo) return; if (refreshing) { refreshAgain = true; return; }
        _ = RefreshAsync();
    }
    async Task RefreshAsync()
    {
        refreshing = true; var session = selected; int epoch = generation, metadataEpoch = MetadataVersion;
        CancellationTokenSource? read=null;
        try
        {
            if (session is null) { var empty = new PlaybackState { Status = selectionWaitingStatus }; bool changed = State != empty; SetLiveState(empty); if (changed) Changed?.Invoke(this, EventArgs.Empty); return; }
            activeReadVersion=metadataEpoch;activeRead=read=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token=read.Token;
            var p = await session.TryGetMediaPropertiesAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(5), token);
            if (disposed || demo || epoch != generation || metadataEpoch != MetadataVersion) return;
            string key = string.Join('\0', session.SourceAppUserModelId, p.Title, p.Artist, p.AlbumTitle); bool trackChanged = key != trackKey;
            if (trackChanged)
            {
                // A polling refresh can discover a new track before its event arrives.
                activeReadVersion=metadataEpoch=Interlocked.Increment(ref metadataVersion);
                InvalidateCurrentTimeline(); Changed?.Invoke(this, EventArgs.Empty);
            }
            if (trackChanged || refreshArtwork)
            {
                refreshArtwork = false; byte[]? nextArtwork = null; string nextArtworkKey = "";
                if (p.Thumbnail is not null)
                {
                    try { using var stream = await p.Thumbnail.OpenReadAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(4), token); if (stream.Size <= 4 * 1024 * 1024) { using var reader = new DataReader(stream); uint length = (uint)stream.Size; await reader.LoadAsync(length).AsTask(token).WaitAsync(TimeSpan.FromSeconds(4),token); var data = new byte[length]; reader.ReadBytes(data); nextArtwork = data; nextArtworkKey = Convert.ToHexString(SHA256.HashData(data)); } }
                    catch (Exception e) when (e is not OperationCanceledException) { log.Write("artwork_unavailable", new { type = e.GetType().Name }); }
                }
                if (disposed || demo || epoch != generation || metadataEpoch != MetadataVersion) return; trackKey = key; artwork = nextArtwork; artworkKey = nextArtworkKey;
            }
            if (disposed || demo || epoch != generation || metadataEpoch != MetadataVersion) return;
            var playback = session.GetPlaybackInfo(); var timeline = session.GetTimelineProperties(); var c = playback.Controls;
            bool freshForTrack = timelineGate.Accept(trackChanged, timeline.LastUpdatedTime);
            string playbackStatus = PlaybackStatus(playback);
            var metadata = PlaybackState.FromMediaTags(session.SourceAppUserModelId, p.Title, p.Artist, p.AlbumTitle) with { Status = playbackStatus, IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, PlaybackRate = playback.PlaybackRate, CanToggle = ToggleCommand(playback) is not null, CanPrevious = c.IsPreviousEnabled, CanNext = c.IsNextEnabled, Artwork = artwork, ArtworkKey = artworkKey, CommandStatus = trackChanged ? "" : State.CommandStatus };
            TryPublishMetadata(PlaybackTimeline.Apply(metadata, timeline.StartTime, timeline.EndTime, timeline.Position, timeline.MinSeekTime, timeline.MaxSeekTime, timeline.LastUpdatedTime, c.IsPlaybackPositionEnabled, freshForTrack), metadataEpoch);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!disposed && !demo && epoch==generation && metadataEpoch==MetadataVersion) { log.Write("media_read_failed", new { type = e.GetType().Name });SetLiveState(new() { Status = "미디어 정보를 읽지 못했습니다 · 다음 갱신에서 다시 확인" }); Changed?.Invoke(this, EventArgs.Empty); } }
        finally { activeRead=null;read?.Cancel();read?.Dispose();refreshing = false; if (refreshAgain) { refreshAgain = false; RequestRefresh(); } }
    }
    internal static bool CommandConfirmed(PlaybackState before, PlaybackState after, bool? expectedPlaying)
    {
        if (before.Source.Length == 0 || before.Source != after.Source) return false;
        if (expectedPlaying is bool expected) return after.IsPlaying == expected;
        return !after.TrackTagsMissing && (before.Title != after.Title || before.Artist != after.Artist || before.AlbumTitle != after.AlbumTitle);
    }
    internal async Task CommandAsync(string command)
    {
        if (disposed || busy || command is not ("toggle" or "previous" or "next")) return;
        if (demo) { if (command is "next" or "previous") { demoTrack++; State = PlaybackState.Demo(demoTrack, State.IsPlaying); } else State = State with { IsPlaying = !State.IsPlaying }; RawState = State; Changed?.Invoke(this, EventArgs.Empty); return; }
        var session = selected; var before = RawState; int epoch = generation;
        long beforeObservation = observationNumber;
        if (session is null || !(command switch { "toggle" => before.CanToggle, "previous" => before.CanPrevious, "next" => before.CanNext, _ => false })) return;
        busy = true; SetCommandStatus("명령 전송 중 · 플레이어 상태 확인 대기"); Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var info = command == "toggle" ? session.GetPlaybackInfo() : null;
            bool? expectedPlaying = info is null ? null : info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            string? action = info is null ? command : ToggleCommand(info);
            if (action is null)
            {
                SetCommandStatus("현재 재생 상태에서 이 명령을 사용할 수 없습니다.");
                return;
            }
            var operation = action switch
            {
                "previous" => session.TrySkipPreviousAsync(),
                "next" => session.TrySkipNextAsync(),
                "play" => session.TryPlayAsync(),
                "pause" => session.TryPauseAsync(),
                _ => session.TryTogglePlayPauseAsync()
            };
            bool accepted = await operation.AsTask(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            if (epoch != generation || disposed) return;
            SetCommandStatus(accepted ? "명령 수락 · 플레이어 상태 확인 대기" : "플레이어가 명령을 수락하지 않았습니다"); Changed?.Invoke(this, EventArgs.Empty);
            if (accepted)
            {
                bool observed = false;
                for (int i = 0; i < 12 && !observed; i++) { await Task.Delay(250, lifetime.Token); if (epoch != generation || demo) return; RequestRefresh(); observed = observationNumber != beforeObservation && CommandConfirmed(before, RawState, expectedPlaying); }
                SetCommandStatus(observed ? "플레이어 상태 변경 확인" : "명령 수락됨 · 상태 변경은 아직 확인되지 않음");
            }
            log.Write("media_command", new { command, accepted, source = before.Source });
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { log.Error("미디어 명령", e); if (!disposed && !demo && epoch == generation) SetCommandStatus("명령 실패 · 플레이어 연결을 확인하세요"); }
        finally { busy = false; if (!disposed) { Changed?.Invoke(this, EventArgs.Empty); RequestRefresh(); } }
    }

    internal async Task SeekAsync(double seconds)
    {
        if (disposed || busy || !CanSeek || !PlaybackTimeline.TryTarget(demo ? State : RawState, seconds, out long targetTicks)) return;
        if (demo)
        {
            State = State with { PositionSeconds = seconds, CommandStatus = "샘플 재생 위치 변경 · 실제 플레이어에는 전송하지 않았습니다" }; RawState = State;
            Changed?.Invoke(this, EventArgs.Empty); return;
        }
        var session = selected; if (session is null) return;
        var before = RawState; int epoch = generation, metadataEpoch = MetadataVersion; string identity = trackKey;
        bool Current() => !disposed && !demo && epoch == generation && metadataEpoch == MetadataVersion && identity == trackKey && ReferenceEquals(session, selected);
        busy = true;
        try
        {
            var timeline = session.GetTimelineProperties(); var controls = session.GetPlaybackInfo().Controls;
            var current = PlaybackTimeline.Apply(before, timeline.StartTime, timeline.EndTime, timeline.Position, timeline.MinSeekTime, timeline.MaxSeekTime, timeline.LastUpdatedTime, controls.IsPlaybackPositionEnabled, timeline.LastUpdatedTime >= before.TimelineUpdated);
            if (!PlaybackTimeline.TryTarget(current, seconds, out targetTicks)) { SetCommandStatus("이 플레이어는 현재 재생 위치를 이동할 수 없습니다"); return; }
            before = current;
            BeginSeekConfirmation(before, seconds);
            // WinRT expects 100 ns ticks on the session timeline, including its nonzero start.
            SetCommandStatus("재생 위치 요청 중 · 플레이어 응답을 기다립니다"); Changed?.Invoke(this, EventArgs.Empty);
            if (!Current() || !CanSeek) return;
            bool accepted = await session.TryChangePlaybackPositionAsync(targetTicks).AsTask(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            if (!accepted) pendingSeekBefore = null;
            if (!Current()) return;
            SetCommandStatus(accepted ? "재생 위치 이동 수락 · 반영 확인 중" : "플레이어가 재생 위치 이동을 수락하지 않았습니다"); Changed?.Invoke(this, EventArgs.Empty);
            bool confirmed = false;
            if (accepted)
            {
                for (int i = 0; i < 12 && !confirmed; i++)
                {
                    RequestRefresh(); await Task.Delay(250, lifetime.Token); if (!Current()) return;
                    confirmed = PlaybackTimeline.Confirms(before, RawState, seconds);
                }
                EndSeekConfirmation();
                SetCommandStatus(confirmed ? "재생 위치 변경 확인" : "이동 요청은 수락됐지만 새 재생 위치는 아직 확인되지 않았습니다");
            }
            log.Write("media_seek", new { seconds, accepted, confirmed, source = before.Source });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { log.Error("재생 위치 이동", error); if (Current()) { EndSeekConfirmation(); SetCommandStatus("재생 위치를 이동하지 못했습니다. 플레이어 연결을 확인해 주세요."); } }
        finally { busy = false; if (!disposed) { Changed?.Invoke(this, EventArgs.Empty); RequestRefresh(); } }
    }
    public void Dispose() { if (disposed) return; disposed = true; poll.Stop(); displayTick.Stop(); displayClock.Reset(); lifetime.Cancel(); ReleaseManager(); Detach(); sessions.Clear(); lifetime.Dispose(); }
}
