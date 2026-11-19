namespace SonoHUD.App.Media;

// A future provider may supply either plain text or timestamped lines; neither implies the other.
internal enum LyricsKind { Unavailable, PlainText, Synchronized }
internal sealed record TimedLyricLine(double Seconds, string Text);
internal sealed record LyricsContent
{
    internal LyricsKind Kind { get; init; }
    internal bool IsDemo { get; init; }
    internal string PlainText { get; init; } = "";
    internal IReadOnlyList<TimedLyricLine> TimedLines { get; init; } = [];
    internal string SourceLabel { get; init; } = "";
    internal static LyricsContent Unavailable { get; } = new();
    internal static LyricsContent Demo(int track) => new()
    {
        Kind = LyricsKind.PlainText, IsDemo = true, SourceLabel = "SonoHUD가 직접 작성한 샘플",
        PlainText = track % 2 == 0
            ? "조용한 밤의 선을 따라\n오늘의 리듬을 놓아두고\n작은 빛이 머무는 곳\n나만의 공간을 채워요\n잠시 숨을 고른 뒤에\n다음 장면으로 걸어가요"
            : "새로운 아침이 다가오면\n빈 페이지를 열어 두어요\n한 걸음씩 이어지는 길\n서두르지 않아도 좋아요\n익숙한 리듬을 따라서\n다시 시작할 수 있어요"
    };
}

internal sealed record LyricDisplayLine(string Text, bool IsCurrent = false);
