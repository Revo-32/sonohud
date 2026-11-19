namespace SonoHUD.App.Media;

internal sealed record PlaybackState
{
    internal string Source { get; init; } = "";
    internal string Title { get; init; } = "재생 중인 음악이 없어요";
    internal string Artist { get; init; } = "플레이어를 열거나 데모 모드를 사용하세요";
    internal bool TrackTagsMissing { get; private init; }
    internal static PlaybackState FromMediaTags(string source, string? title, string? artist, string? album) => new()
    {
        Source = source,
        Title = string.IsNullOrWhiteSpace(title) ? "제목 정보 없음" : title,
        Artist = string.IsNullOrWhiteSpace(artist) ? "아티스트 정보 없음" : artist,
        AlbumTitle = album ?? "",
        TrackTagsMissing = string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)
    };
    internal string AlbumTitle { get; init; } = "";
    internal string Status { get; init; } = "세션 없음";
    internal bool IsPlaying { get; init; }
    internal double? PlaybackRate { get; init; }
    internal bool IsPositionEstimated { get; init; }
    internal bool IsPlaybackRateAssumed { get; init; }
    internal bool IsDemo { get; init; }
    internal bool CanToggle { get; init; }
    internal bool CanPrevious { get; init; }
    internal bool CanNext { get; init; }
    internal bool CanSeek { get; init; }
    internal long TimelineStartTicks { get; init; }
    internal double? SeekMinimumSeconds { get; init; }
    internal double? SeekMaximumSeconds { get; init; }
    internal double? PositionSeconds { get; init; }
    internal double? DurationSeconds { get; init; }
    internal byte[]? Artwork { get; init; }
    internal string ArtworkKey { get; init; } = "";
    internal DateTimeOffset TimelineUpdated { get; init; }
    internal string CommandStatus { get; init; } = "";
    internal LyricsContent Lyrics { get; init; } = LyricsContent.Unavailable;
    internal SonoHUD.Core.LyricDocument? PreviewLyrics { get; init; }
    internal SonoHUD.Core.LyricFrame PreviewLyricFrame { get; init; }
    internal double Progress => PositionSeconds.HasValue && DurationSeconds > 0 ? Math.Clamp(PositionSeconds.Value / DurationSeconds.Value, 0, 1) : 0;
    internal static PlaybackState Demo(int track = 0, bool playing = true) => new() { Source = "SonoHUD Demo", Title = track % 2 == 0 ? "Midnight Lines" : "A Place to Begin", Artist = "SonoHUD · 직접 작성한 데모", AlbumTitle = "SonoHUD 자체 샘플", Status = "DEMO · 실제 미디어 재생 아님", IsDemo = true, IsPlaying = playing, CanToggle = true, CanPrevious = true, CanNext = true, CanSeek = true, SeekMinimumSeconds = 0, SeekMaximumSeconds = 213, PositionSeconds = 46, DurationSeconds = 213, ArtworkKey = "demo-" + (track % 2), Lyrics = LyricsContent.Demo(track) };
}
internal sealed record MediaSessionItem(string Key, string Label);
