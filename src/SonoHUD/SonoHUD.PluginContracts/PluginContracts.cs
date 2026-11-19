using SonoHUD.Core;

namespace SonoHUD.PluginContracts;

public sealed record PluginContext(string SettingsDirectory, Version HostVersion);
public interface ISonoHudPlugin : IDisposable
{
    void Initialize(PluginContext context);
}

public sealed record LyricsLookup(LyricRecord? Match, IReadOnlyList<LyricRecord> Candidates);
public interface ILyricsProvider
{
    Task<LyricsLookup> LookupAsync(LyricTrack track, LyricsPreference? preference, bool listCandidates, CancellationToken cancellationToken);
    Task<LyricRecord?> SelectAsync(long recordId, CancellationToken cancellationToken);
}

// Presentable provider errors should not include the current track or raw response body.
public class LyricsRequestException(string message, DateTimeOffset? retryAt = null) : Exception(message)
{
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
