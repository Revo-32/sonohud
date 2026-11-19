namespace SonoHUD.App.Media;

internal static class PlaybackTimeline
{
    internal static PlaybackState Apply(PlaybackState state, TimeSpan start, TimeSpan end, TimeSpan position,
        TimeSpan minimum, TimeSpan maximum, DateTimeOffset updated, bool supportsSeek, bool fresh)
    {
        bool valid = fresh && updated != default && start >= TimeSpan.Zero && end > start && position >= start && position <= end;
        bool positionOnly = fresh && updated != default && start >= TimeSpan.Zero && end == TimeSpan.Zero && position > start;
        double? length = valid ? (end - start).TotalSeconds : null;
        double? lower = valid ? Math.Max(0, (minimum - start).TotalSeconds) : null;
        double? upper = valid ? Math.Min(length!.Value, (maximum - start).TotalSeconds) : null;
        bool canSeek = valid && supportsSeek && lower >= 0 && upper > lower;
        return state with
        {
            PositionSeconds = valid || positionOnly ? (position - start).TotalSeconds : null, DurationSeconds = length,
            IsPositionEstimated = false, IsPlaybackRateAssumed = false,
            TimelineUpdated = updated, TimelineStartTicks = valid ? start.Ticks : 0,
            CanSeek = canSeek, SeekMinimumSeconds = canSeek ? lower : null, SeekMaximumSeconds = canSeek ? upper : null
        };
    }

    internal static bool TryTarget(PlaybackState state, double seconds, out long ticks)
    {
        ticks = 0;
        if (!state.CanSeek || !double.IsFinite(seconds) || state.PositionSeconds is null || state.DurationSeconds is not > 0 ||
            state.SeekMinimumSeconds is not double minimum || state.SeekMaximumSeconds is not double maximum || seconds < minimum || seconds > maximum) return false;
        try { ticks = checked(state.TimelineStartTicks + TimeSpan.FromSeconds(seconds).Ticks); return true; }
        catch (OverflowException) { return false; }
    }

    internal static bool Confirms(PlaybackState before, PlaybackState after, double target) =>
        !before.IsPositionEstimated && !after.IsPositionEstimated &&
        before.Source == after.Source && before.Title == after.Title && before.Artist == after.Artist && before.AlbumTitle == after.AlbumTitle &&
        after.TimelineUpdated > before.TimelineUpdated && after.PositionSeconds is double position && Math.Abs(position - target) <= 2;
}
