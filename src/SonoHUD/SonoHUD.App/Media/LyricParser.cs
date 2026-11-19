using System.Globalization;
using System.Text.RegularExpressions;
using SonoHUD.Core;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace SonoHUD.App.Media;

internal static class LyricParser
{
    internal const int MaximumText = 262144, MaximumLines = 2000;
    static readonly Regex stamp = new(@"\[(\d{1,3}):([0-5]\d)(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static LyricDocument Parse(LyricRecord record)
    {
        if(record.Instrumental) return new(LyricFormat.Instrumental,[],record.Id);
        string warning="";
        if(record.Lyricsfile is { Length:>0 } yaml)
        {
            try { var parsed=Yaml(yaml, record.HasWordSync); if(parsed.Format!=LyricFormat.None)return parsed with {RecordId=record.Id}; }
            catch(Exception e) when(e is YamlException or InvalidDataException or ArgumentException or FormatException or OverflowException)
            { warning="Lyricsfile을 읽지 못해 줄 가사 또는 일반 가사로 전환했습니다."; }
        }
        var lines=Lrc(record.SyncedLyrics);
        if(lines.Count>0)return new(LyricFormat.Lines,lines,record.Id,warning);
        return Plain(record.PlainLyrics) with {RecordId=record.Id,Warning=warning};
    }
    internal static LyricDocument Plain(string? text)
    {
        if(string.IsNullOrWhiteSpace(text) || text.Length>MaximumText)return LyricDocument.Empty;
        var lines=text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Take(MaximumLines).Select(x=>new LyricLine(x[..Math.Min(x.Length,2048)])).ToArray();
        return new(LyricFormat.Plain,lines);
    }
    internal static IReadOnlyList<LyricLine> Lrc(string? text)
    {
        if(string.IsNullOrWhiteSpace(text) || text.Length>MaximumText)return [];
        var lines=new List<LyricLine>();
        var offsetMatch=Regex.Match(text,@"\[offset:([+-]?\d{1,6})\]",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
        double offset=offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value,out int ms) ? Math.Clamp(ms,-60000,60000)/1000d : 0;
        foreach(string line in text.Split('\n').Take(MaximumLines))
        {
            var stamps=stamp.Matches(line); if(stamps.Count==0)continue;
            string words=stamp.Replace(line,"").Trim(); if(words.Length>2048)words=words[..2048];
            foreach(Match match in stamps.Cast<Match>().Take(16))
            {
                double time=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture)*60+int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);
                if(match.Groups[3].Success)time+=double.Parse("0."+match.Groups[3].Value,CultureInfo.InvariantCulture);
                lines.Add(new(words,Math.Max(0,time-offset)));
                if(lines.Count>=MaximumLines)break;
            }
            if(lines.Count>=MaximumLines)break;
        }
        return lines.Distinct().OrderBy(x=>x.Start).ToArray();
    }
    internal static LyricDocument Yaml(string text,bool allowWords)
    {
        if(text.Length>MaximumText)throw new InvalidDataException("Lyricsfile 크기 제한");
        // Token pass rejects aliases, anchors, tags and deep trees before constructing a graph.
        var parser=new Parser(new StringReader(text)); int depth=0,count=0,documents=0;
        while(parser.MoveNext())
        {
            if(++count>40000)throw new InvalidDataException("Lyricsfile 노드 제한");
            var item=parser.Current;
            if(item is AnchorAlias || item is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))throw new InvalidDataException("Lyricsfile 태그·별칭 제한");
            if(item is MappingStart or SequenceStart && ++depth>12)throw new InvalidDataException("Lyricsfile 깊이 제한");
            if(item is MappingEnd or SequenceEnd)depth--;
            if(item is DocumentStart && ++documents>1)throw new InvalidDataException("복수 문서 제한");
        }
        var stream=new YamlStream(); stream.Load(new StringReader(text));
        if(stream.Documents.Count!=1 || stream.Documents[0].RootNode is not YamlMappingNode root || Value(root,"version")!="1.0")throw new InvalidDataException("지원하지 않는 Lyricsfile 버전");
        var lines=new List<LyricLine>(); bool wordSync=false; int discarded=0;
        if(Node(root,"metadata") is YamlMappingNode meta)
        {
            if(Value(meta,"instrumental")=="true")return new(LyricFormat.Instrumental,[]);
            if(Value(meta,"offset_ms") is string offset && offset!="0")discarded++; // Draft has no defined offset direction.
        }
        if(Node(root,"lines") is YamlSequenceNode sequence)
        {
            foreach(var node in sequence.Children.Take(MaximumLines))
            {
                if(node is not YamlMappingNode line || Value(line,"text") is not string words || words.Length>2048 || Time(line,"start_ms") is not double start){discarded++;continue;}
                double? end=Time(line,"end_ms"); if(end<start){discarded++;end=null;}
                var parts=new List<LyricWord>();
                if(allowWords && Node(line,"words") is YamlSequenceNode wordNodes && wordNodes.Children.Count<=256)
                    foreach(var part in wordNodes.Children)
                    {
                        if(part is not YamlMappingNode word || Value(word,"text") is not string value || Time(word,"start_ms") is not double at || at<start){parts.Clear();break;}
                        double? until=Time(word,"end_ms");
                        if(until<at || end is double limit && (at>limit || until>limit)){parts.Clear();break;}
                        parts.Add(new(value,at,until));
                    }
                if(string.Concat(parts.Select(x=>x.Text))!=words || !parts.Select(x=>x.Start).SequenceEqual(parts.Select(x=>x.Start).Order()))parts.Clear();
                if(parts.Count>0)wordSync=true;
                lines.Add(new(words,start,end,parts));
            }
        }
        string warning=discarded>0?"일부 지원되지 않는 타이밍 정보는 적용하지 않았습니다.":"";
        if(lines.Count>0)return new(wordSync?LyricFormat.Words:LyricFormat.Lines,lines.OrderBy(x=>x.Start).ToArray(),Warning:warning);
        return Plain(Value(root,"plain")) with {Warning=warning};
    }
    static YamlNode? Node(YamlMappingNode node,string name)=>node.Children.TryGetValue(new YamlScalarNode(name),out var value)?value:null;
    static string? Value(YamlMappingNode node,string name)=>(Node(node,name) as YamlScalarNode)?.Value;
    static double? Time(YamlMappingNode node,string name)=>long.TryParse(Value(node,name),NumberStyles.Integer,CultureInfo.InvariantCulture,out long value) && value is >=0 and <=86400000 ? value/1000d : null;
}
