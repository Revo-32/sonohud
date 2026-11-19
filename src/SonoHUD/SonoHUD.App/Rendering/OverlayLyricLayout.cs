using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using SonoHUD.App.Views;
using SonoHUD.Core;

namespace SonoHUD.App.Rendering;

internal enum LyricRole { Previous, Current, Next, Neutral }
internal sealed record LyricVisualRow(string Text, int Start, double Top, double Height, double Size, LyricRole Role);

// The preview and desktop use identical line breaks, word ranges and page boundaries.
internal sealed class OverlayLyricLayout
{
    internal const double PastOpacity = .68;
    readonly SceneElement element;
    readonly LyricDocument document;
    readonly int current, next;
    readonly bool hasPosition;
    readonly double? trackEnd;
    readonly string status;
    readonly List<(int Start, string Text)> wrapped = [];
    readonly List<double> pageStarts = [];
    readonly double contextHeight, rowHeight, fontSize;
    readonly int capacity;
    internal const double ReadingSeconds = 1.25;
    internal LyricLine Line { get; }
    internal int PageCount => pageStarts.Count;
    internal OverlayLyricLayout(SceneElement element, LyricDocument document, LyricFrame frame, string status, double? trackEnd = null)
    {
        this.element = element;
        this.document = document;
        this.status = status;
        this.trackEnd = trackEnd;
        current = frame.Current;
        hasPosition = frame.Position.HasValue;
        next = current >= 0 ? current + 1 : NextLine(document, frame.Position);
        Line = current >= 0 ? document.Lines[current] : !document.Timed && document.Lines.Count > 0 ? document.Lines[0] :
            new LyricLine(document.Timed ? frame.Position.HasValue ? "간주" : "재생 위치 기다리는 중" : status.Length > 0 ? status : "가사를 기다립니다");
        contextHeight = element.Height >= 82 ? Math.Min(22, element.Height * .19) : 0;
        double available = Math.Max(1, element.Height - contextHeight * 2);
        fontSize = Math.Min(element.Style.FontSize, available * .78);
        var run = new RunStyle(TypefaceFor(LyricRole.Current), fontSize);
        var source = new LineSource(Line.Text, run);
        var paragraph = new ParagraphStyle(run);
        using var formatter = TextFormatter.Create();
        double tallest = Format("Ag한あ", LyricRole.Current, fontSize, Brushes.White).Height;
        TextLineBreak? continuation = null;
        try
        {
            for (int start = 0; start < Line.Text.Length;)
            {
                using var visualLine = formatter.FormatLine(source, start, Math.Max(1, element.Width - 2), paragraph, continuation);
                continuation?.Dispose();
                continuation = visualLine.GetTextLineBreak();
                int length = Math.Clamp(visualLine.Length, 1, Line.Text.Length - start);
                wrapped.Add((start, Line.Text.Substring(start, length)));
                tallest = Math.Max(tallest, visualLine.Height);
                start += length;
            }
        }
        finally { continuation?.Dispose(); }
        if (wrapped.Count == 0) wrapped.Add((0, ""));
        rowHeight = Math.Min(available, Math.Ceiling(tallest));
        capacity = Math.Max(1, (int)Math.Floor(available / rowHeight));
        // Give a short orphaned ending the context rows before forcing a page turn.
        if (wrapped.Count == capacity + 1 && wrapped[^1].Text.Trim().Length <= 16 &&
            wrapped.Count * rowHeight <= element.Height)
        {
            capacity = wrapped.Count;
            contextHeight = 0;
        }
        double startTime = Line.Start ?? 0;
        double? nextStart = document.Lines.Skip(Math.Max(0,current + 1)).FirstOrDefault(l => l.Start > startTime)?.Start;
        double endTime = Line.End ?? nextStart ?? (trackEnd > startTime ? trackEnd : null)
            ?? Line.Words?.LastOrDefault()?.End ?? startTime + Math.Ceiling((double)wrapped.Count / capacity) * 3;
        if (nextStart.HasValue) endTime = Math.Min(endTime,nextStart.Value);
        if (trackEnd > startTime) endTime = Math.Min(endTime,trackEnd.Value);
        for (int i = 0; i < wrapped.Count; i += capacity)
        {
            int offset = wrapped[i].Start;
            double time = startTime + (endTime - startTime) * offset / Math.Max(1, Line.Text.Length);
            int wordStart = 0;
            for (int w = 0; Line.Words is { } words && w < words.Count; w++)
            {
                var word = words[w];
                if (offset < wordStart + word.Text.Length)
                {
                    double fraction = (offset - wordStart) / (double)word.Text.Length;
                    double until = word.End ?? (w + 1 < words.Count ? words[w+1].Start : endTime);
                    time = word.Start + fraction * Math.Max(0, until - word.Start);
                    break;
                }
                wordStart += word.Text.Length;
            }
            pageStarts.Add(i == 0 ? startTime : Math.Max(pageStarts[^1], time));
        }
        // Show the text early enough to read. Word highlighting still runs on the source clock.
        // Reserve an interval for every page, even when several source onsets coincide.
        double dwell = Math.Min(ReadingSeconds, Math.Max(0,endTime-startTime) / pageStarts.Count);
        for (int page=1; page<pageStarts.Count; page++)
        {
            double latest = endTime - (pageStarts.Count-page)*dwell;
            pageStarts[page] = Math.Max(pageStarts[page-1]+dwell, Math.Min(pageStarts[page],latest));
        }
    }
    static int NextLine(LyricDocument doc, double? position)
    {
        if (!position.HasValue) return 0;
        for (int i = 0; i < doc.Lines.Count; i++) if (doc.Lines[i].Start > position) return i;
        return doc.Lines.Count;
    }
    internal bool Matches(LyricDocument doc, LyricFrame frame, string message, double? duration = null) =>
        trackEnd == duration && ReferenceEquals(document,doc) && current == frame.Current && status == message &&
        (current >= 0 || !doc.Timed || (frame.Position.HasValue == hasPosition &&
        (!frame.Position.HasValue || (next == 0 || doc.Lines[next-1].Start <= frame.Position) && (next == doc.Lines.Count || doc.Lines[next].Start > frame.Position))));
    internal int PageAt(double? position)
    {
        if (position is not double time || pageStarts.Count == 1) return 0;
        int low = 0, high = pageStarts.Count;
        while (low < high)
        {
            int middle = low + (high-low)/2;
            if (pageStarts[middle] <= time) low = middle+1;
            else high = middle;
        }
        return Math.Max(0, low-1);
    }
    internal IEnumerable<LyricVisualRow> Rows(int page)
    {
        bool timed = current >= 0;
        int previous = timed ? current - 1 : next - 1;
        double small = Math.Min(15, element.Style.FontSize * .61);
        int first = page * capacity, count = Math.Min(capacity, wrapped.Count - first);
        // Keep the preceding phrase on screen when a small final fragment appears early.
        int carry = Math.Min(first, capacity-count);
        if (contextHeight > 0 && first > carry)
            yield return new(wrapped[first-carry-1].Text, 0, 0, contextHeight, small, LyricRole.Previous);
        else if (contextHeight > 0 && previous >= 0)
            yield return new(document.Lines[previous].Text, 0, 0, contextHeight, small, LyricRole.Previous);
        first -= carry;
        count += carry;
        double y = (element.Height - count * rowHeight) / 2;
        for (int i = first; i < first + count; i++, y += rowHeight)
            yield return new(wrapped[i].Text, wrapped[i].Start, y, rowHeight, fontSize, timed ? LyricRole.Current : LyricRole.Neutral);
        int following = !document.Timed ? 1 : next;
        if (contextHeight > 0 && first+count < wrapped.Count)
            yield return new(wrapped[first+count].Text, 0, element.Height-contextHeight, contextHeight, small, LyricRole.Next);
        else if (contextHeight > 0 && following < document.Lines.Count)
            yield return new(document.Lines[following].Text, 0, element.Height-contextHeight, contextHeight, small, LyricRole.Next);
    }
    Typeface TypefaceFor(LyricRole role) => new(FontCatalog.Resolve(element.Style.FontFamily), FontStyles.Normal,
        role is LyricRole.Current or LyricRole.Neutral ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
    FormattedText Format(string s, LyricRole role, double size, Brush brush) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TypefaceFor(role), size, brush, 1) { Trimming = TextTrimming.None };
    internal FormattedText Text(LyricVisualRow row, Brush? ink = null)
    {
        bool context = row.Role is LyricRole.Previous or LyricRole.Next;
        string color = context ? element.Style.ContextForeground ?? element.Style.Foreground : element.Style.Foreground;
        var text = Format(row.Text.TrimEnd(), row.Role, row.Size, ink ?? ElementDrawing.Brush(color));
        if (context) { text.MaxTextWidth = Math.Max(1,element.Width); text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        return text;
    }

    sealed class LineSource(string value, TextRunProperties properties) : TextSource
    {
        public override TextRun GetTextRun(int index) => index < value.Length ? new TextCharacters(value, index, value.Length-index, properties) : new TextEndOfParagraph(1);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(limit, new(properties.CultureInfo, new CharacterBufferRange(value, 0, Math.Min(limit,value.Length))));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }
    sealed class RunStyle(Typeface face, double size) : TextRunProperties
    {
        public override Typeface Typeface => face;
        public override double FontRenderingEmSize => size;
        public override double FontHintingEmSize => size;
        public override CultureInfo CultureInfo => CultureInfo.CurrentUICulture;
        public override Brush ForegroundBrush => Brushes.White;
        public override Brush? BackgroundBrush => null;
        public override TextDecorationCollection? TextDecorations => null;
        public override TextEffectCollection? TextEffects => null;
    }
    sealed class ParagraphStyle(TextRunProperties properties) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => false;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => TextWrapping.Wrap;
        public override TextMarkerProperties? TextMarkerProperties => null;
        public override double Indent => 0;
    }
}
