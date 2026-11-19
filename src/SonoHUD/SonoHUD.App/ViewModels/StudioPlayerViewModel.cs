using System.Windows.Input;
using SonoHUD.App.Media;
using SonoHUD.App.Platform;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel
{
    bool seeking;
    double seekDraft;
    string seekIdentity = "";
    public string TrackAlbum => string.IsNullOrWhiteSpace(Media.State.AlbumTitle) ? "앨범 정보 없음" : Media.State.AlbumTitle;
    public string PlayerSource => Media.State.Source.Length > 0 ? Media.State.Source : Media.PreferredSourceAppId.Length > 0 ? Media.PreferredSourceAppId + " · 연결 대기" : "연결된 플레이어 없음";
    public string PlayerPlaybackStatus => Media.State.IsDemo ? "자체 샘플 · " + (Media.State.IsPlaying ? "재생 중" : "일시정지") : Media.State.Source.Length == 0 ? Media.PreferredSourceAppId.Length > 0 ? "선택한 플레이어 연결 대기" : "연결된 플레이어 없음" : Media.State.IsPlaying ? "재생 중" : Media.State.Status switch { "일시정지" or "정지" or "플레이어 닫힘" or "재생 준비" or "곡 전환 중" => Media.State.Status, _ => "재생 상태 확인 중" };
    public bool PlayerNeedsConnection => Media.State.Source.Length == 0;
    public string PlayerConnectionHint => Media.PreferredSourceAppId.Length > 0 ? "선택한 플레이어의 연결을 기다립니다. 다른 앱으로 자동 전환하지 않습니다. 음악 소스는 오디오에서 변경할 수 있습니다." : "플레이어에서 음악을 재생해 주세요. 음악 소스는 오디오에서 선택할 수 있습니다.";
    public bool CanSeek => !stopped && Media.CanSeek && !Media.Busy;
    public bool ShowSeekControl => Media.CanSeek;
    public double SeekMinimum => Media.State.SeekMinimumSeconds ?? 0;
    public double SeekMaximum => Math.Max(SeekMinimum, Media.State.SeekMaximumSeconds ?? Media.State.DurationSeconds ?? 1);
    public double SeekValue
    {
        get => seeking ? seekDraft : Math.Clamp(Media.State.PositionSeconds ?? 0, SeekMinimum, SeekMaximum);
        set { if (!seeking || !CanSeek || !double.IsFinite(value)) return; seekDraft = Math.Clamp(value, SeekMinimum, SeekMaximum); Notify(nameof(SeekValue)); Notify(nameof(SeekPreviewTime)); }
    }
    public string CurrentTime => PlayerTime(Media.State.PositionSeconds);
    public string TotalTime => PlayerTime(Media.State.DurationSeconds);
    public string SeekPreviewTime => seeking ? "이동할 위치 " + PlayerTime(seekDraft) : "";
    public string SeekHint => Media.Busy ? "플레이어 응답을 기다립니다" : !CanSeek ? Media.State.DurationSeconds is null ? "플레이어가 재생 시간을 제공하지 않았습니다" : "이 플레이어는 재생 위치 이동을 지원하지 않습니다" : Media.State.IsDemo ? "샘플 위치 조절 · 실제 음악은 재생하지 않습니다" : "놓으면 재생 위치를 요청합니다. 플레이어 반영이 늦을 수 있습니다.";
    public string LyricsModeLabel => Media.State.Lyrics.Kind switch { LyricsKind.PlainText => Media.State.Lyrics.IsDemo ? "자체 샘플 가사 · 일반 텍스트" : "일반 가사", LyricsKind.Synchronized => "동기화 가사", _ => "가사 없음" };
    public bool HasPlayerLyrics => Media.State.Lyrics.Kind != LyricsKind.Unavailable;
    public bool HasNoPlayerLyrics => !HasPlayerLyrics;
    public string LyricsNotice => Media.State.Lyrics.IsDemo ? "SonoHUD가 직접 작성한 샘플입니다. 현재 음악의 가사가 아닙니다." : Media.State.Lyrics.Kind == LyricsKind.Unavailable ? "가사가 제공되지 않았습니다. 온라인 검색 상태를 확인해 주세요." : Media.State.Lyrics.SourceLabel;
    public IReadOnlyList<LyricDisplayLine> PlayerLyricsLines
    {
        get
        {
            var lyrics = Media.State.Lyrics;
            if (lyrics.Kind == LyricsKind.PlainText) return lyrics.PlainText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(text => new LyricDisplayLine(text)).ToArray();
            if (lyrics.Kind != LyricsKind.Synchronized) return [];
            int active = -1;
            if (Media.State.PositionSeconds is double position)
                for (int i = 0; i < lyrics.TimedLines.Count; i++) if (lyrics.TimedLines[i].Seconds <= position) active = i;
            return lyrics.TimedLines.Select((line, index) => new LyricDisplayLine(line.Text, index == active)).ToArray();
        }
    }
    public ICommand CommitSeekCommand { get; private set; } = null!;
    internal void InitializePlayer()
    {
        CommitSeekCommand = new RelayCommand(_ => _ = CommitSeekAsync(), _ => CanSeek && seeking);
        foreach (var session in Media.Sessions) MediaSessions.Add(session.Key == "auto" ? session with { Label = "자동 선택" } : session);
        if (MediaSessions.Count == 0) MediaSessions.Add(new("auto", "자동 선택"));
    }
    internal bool BeginSeek()
    {
        if (!CanSeek) return false;
        seeking = true; seekDraft = Math.Clamp(Media.State.PositionSeconds ?? SeekMinimum, SeekMinimum, SeekMaximum); seekIdentity = Media.SeekIdentity;
        Notify(nameof(SeekPreviewTime)); CommandManager.InvalidateRequerySuggested(); return true;
    }
    internal void CancelSeek()
    {
        seeking = false; seekIdentity = ""; Notify(nameof(SeekValue)); Notify(nameof(SeekPreviewTime)); CommandManager.InvalidateRequerySuggested();
    }
    internal async Task CommitSeekAsync()
    {
        if (!seeking) return;
        double requested = seekDraft; bool valid = CanSeek && seekIdentity == Media.SeekIdentity;
        CancelSeek(); if (!valid) return;
        await Media.SeekAsync(requested);
    }
    internal void NotifyPlayer()
    {
        if (seeking && (!CanSeek || seekIdentity != Media.SeekIdentity)) CancelSeek();
        foreach (string property in new[] { nameof(TrackAlbum), nameof(PlayerSource), nameof(PlayerPlaybackStatus), nameof(PlayerNeedsConnection), nameof(PlayerConnectionHint), nameof(CanSeek), nameof(ShowSeekControl), nameof(SeekMinimum), nameof(SeekMaximum), nameof(SeekValue), nameof(CurrentTime), nameof(TotalTime), nameof(SeekPreviewTime), nameof(SeekHint), nameof(LyricsModeLabel), nameof(HasPlayerLyrics), nameof(HasNoPlayerLyrics), nameof(LyricsNotice), nameof(PlayerLyricsLines) }) Notify(property);
    }
    static string PlayerTime(double? seconds)
    {
        if (seconds is not double value || !double.IsFinite(value) || value < 0 || value > TimeSpan.MaxValue.TotalSeconds) return "—";
        var time = TimeSpan.FromSeconds(value);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
