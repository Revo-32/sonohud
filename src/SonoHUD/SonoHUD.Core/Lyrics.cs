using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SonoHUD.Core;

public enum LyricFormat { None, Instrumental, Plain, Lines, Words }
public sealed record LyricWord(string Text, double Start, double? End);
public sealed record LyricLine(string Text, double? Start = null, double? End = null, IReadOnlyList<LyricWord>? Words = null);
public sealed record LyricDocument(LyricFormat Format, IReadOnlyList<LyricLine> Lines, long? RecordId = null, string Warning = "")
{
    public static LyricDocument Empty { get; } = new(LyricFormat.None, []);
    public bool Timed => Format is LyricFormat.Lines or LyricFormat.Words;
}
public sealed record LyricTrack(string Title, string Artist, string Album, double? Duration)
{
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', LyricMatch.Normalize(Title), LyricMatch.Normalize(Artist), LyricMatch.Normalize(Album)))));
}
public sealed record LyricRecord
{
    public long Id { get; init; }
    public string TrackName { get; init; } = "";
    public string ArtistName { get; init; } = "";
    public string AlbumName { get; init; } = "";
    public double Duration { get; init; }
    public bool Instrumental { get; init; }
    public bool HasWordSync { get; init; }
    public string? PlainLyrics { get; init; }
    public string? SyncedLyrics { get; init; }
    public string? Lyricsfile { get; init; }
    public string Label => $"{TrackName} · {ArtistName} · {AlbumName} · {Duration:0}s";
    public LyricRecord Summary() => this with { PlainLyrics = null, SyncedLyrics = null, Lyricsfile = null };
}
public static class LyricMatch
{
    // Punctuation is normalized, never the version words contained in it.
    public static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), @"[\p{P}\p{Z}\s]+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
}
public sealed record LyricsPreference(string TrackKey, long? RecordId = null, int OffsetMilliseconds = 0, string? RecordKey = null);
public sealed record LyricsSettings
{
    public bool OnlineEnabled { get; set; } = true;
    public List<LyricsPreference> Tracks { get; set; } = [];
    public LyricsSettings Sanitize() => this with { Tracks = (Tracks ?? []).Where(x => x is not null && x.TrackKey is { Length:64 } && x.TrackKey.All(Uri.IsHexDigit)).DistinctBy(x=>x.TrackKey).TakeLast(256).Select(x=>x with { RecordId = x.RecordId > 0 ? x.RecordId : null, OffsetMilliseconds = Math.Clamp(x.OffsetMilliseconds,-10000,10000), RecordKey = x.RecordKey is {Length:64} && x.RecordKey.All(Uri.IsHexDigit) ? x.RecordKey : null }).ToList() };
}
public readonly record struct LyricFrame(double? Position, int Current, bool Uncertain)
{
    public static LyricFrame At(LyricDocument document, double? seconds, double offsetSeconds = 0, bool uncertain = false)
    {
        if (!document.Timed || seconds is not double s || !double.IsFinite(s)) return new(null,-1,uncertain);
        double p = s + offsetSeconds; int current = -1;
        for(int i=0;i<document.Lines.Count;i++) if(document.Lines[i].Start <= p) current=i;
        if(current>=0 && document.Lines[current].End is double end && p>=end)
        {
            current=-1;
            for(int i=0;i<document.Lines.Count;i++)if(document.Lines[i].Start<=p && document.Lines[i].End>p)current=i;
        }
        return new(p,current,uncertain);
    }
    public bool Active(LyricDocument doc, int index)
    {
        if(Position is not double p || index<0 || index>=doc.Lines.Count || !doc.Timed) return false;
        var line=doc.Lines[index];
        // Explicit overlaps and equal start times remain active together.
        if(line.Start is not double start || p<start)return false;
        if(line.End is double end)return p<end;
        for(int i=index+1;i<doc.Lines.Count;i++)
            if(doc.Lines[i].Start is double next && next>start)return p<next;
        return p<double.PositiveInfinity;
    }
    public double WordProgress(LyricWord word) => Position is not double p || p<word.Start ? 0 : word.End is double end && end>word.Start ? Math.Clamp((p-word.Start)/(end-word.Start),0,1) : 1;
}
