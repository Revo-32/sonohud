using System.Diagnostics;

namespace SonoHUD.App.Media;

// SMTC.Position is a snapshot at LastUpdatedTime, not a continuously advancing clock.
// This is display-only estimation: the service retains untouched raw observations for seeks.
internal sealed class PlaybackDisplayClock
{
    internal const double MaximumSnapshotAgeSeconds = 15;
    internal const double MaximumObservationGapSeconds = 6;
    readonly Func<TimeSpan> monotonicNow;
    readonly Func<DateTimeOffset> utcNow;
    PlaybackState? observation;
    DateTimeOffset acceptedUpdated;
    string identity = "";
    double anchorAt, observedAt, sourceAt, sourceAge, anchorPosition, rate;
    bool hasPosition, advancing, validTimestamp, suspended, staleBlocked;

    internal PlaybackDisplayClock(Func<TimeSpan>? monotonicNow = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.monotonicNow = monotonicNow ?? (() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal void Reset()
    {
        observation = null; acceptedUpdated = default; identity = ""; hasPosition = advancing = validTimestamp = suspended = staleBlocked = false;
    }

    internal PlaybackState Observe(PlaybackState raw, string trackIdentity)
    {
        double now = monotonicNow().TotalSeconds;
        double previousPosition = PositionAt(now);
        bool changedIdentity = observation is null || identity != trackIdentity;
        bool changedSnapshot = changedIdentity || raw.TimelineUpdated != observation!.TimelineUpdated || raw.PositionSeconds != observation.PositionSeconds || raw.DurationSeconds != observation.DurationSeconds || raw.TimelineStartTicks != observation.TimelineStartTicks;
        bool changedPlayback = observation is null || raw.IsPlaying != observation.IsPlaying || raw.PlaybackRate != observation.PlaybackRate || suspended;
        bool regressedTimestamp = !changedIdentity && raw.TimelineUpdated != default && raw.TimelineUpdated < acceptedUpdated;
        bool positionValid = raw.PositionSeconds is double position && double.IsFinite(position) && position >= 0 && (raw.DurationSeconds is null || raw.DurationSeconds is double duration && double.IsFinite(duration) && duration > 0 && position <= duration);
        bool rateValid = raw.PlaybackRate is null || raw.PlaybackRate is double r && double.IsFinite(r) && r > 0;
        // A missing rate is a bounded 1x display assumption; an explicit invalid rate is not.
        double nextRate = rateValid ? raw.PlaybackRate ?? 1 : 0;

        if (changedIdentity || !positionValid)
        {
            hasPosition = false; advancing = false;
            if (changedIdentity) acceptedUpdated = default;
        }
        if (positionValid && regressedTimestamp)
        {
            // An older source timestamp cannot restart the freshness budget or rewind the display.
            anchorPosition = previousPosition; anchorAt = now; advancing = false; suspended = staleBlocked = true;
        }
        else if (positionValid && changedSnapshot)
        {
            double age = raw.TimelineUpdated == default ? double.PositiveInfinity : (utcNow() - raw.TimelineUpdated).TotalSeconds;
            acceptedUpdated = raw.TimelineUpdated;
            validTimestamp = double.IsFinite(age) && age >= -1 && age <= MaximumSnapshotAgeSeconds;
            staleBlocked = !validTimestamp;
            sourceAge = Math.Max(0, age); sourceAt = now; anchorAt = now;
            advancing = raw.IsPlaying && !raw.IsDemo && rateValid && validTimestamp;
            rate = nextRate;
            anchorPosition = raw.PositionSeconds!.Value + (advancing ? sourceAge * rate : 0);
            anchorPosition = Math.Clamp(anchorPosition, 0, raw.DurationSeconds??double.MaxValue);
            hasPosition = true; suspended = false;
        }
        else if (positionValid && (staleBlocked || now - observedAt > MaximumObservationGapSeconds || sourceAge + now - sourceAt >= MaximumSnapshotAgeSeconds))
        {
            // Repeating an obsolete snapshot after a gap cannot resurrect its display clock.
            anchorPosition = previousPosition; anchorAt = now; advancing = false; staleBlocked = true;
        }
        else if (positionValid && changedPlayback)
        {
            // Pause/resume/rate transitions share the current display position when the player
            // has not published a new timeline. Never add the paused interval to a resumed track.
            anchorPosition = previousPosition; anchorAt = now; rate = nextRate;
            advancing = raw.IsPlaying && !raw.IsDemo && rateValid && validTimestamp;
            suspended = false;
        }
        observedAt = now; observation = raw; identity = trackIdentity;
        return Project(raw);
    }

    internal void Suspend()
    {
        double now = monotonicNow().TotalSeconds;
        anchorPosition = PositionAt(now); anchorAt = now; advancing = false; suspended = true;
    }
    internal bool IsUncertain => !hasPosition || staleBlocked || suspended || advancing &&
        (monotonicNow().TotalSeconds-observedAt>MaximumObservationGapSeconds || sourceAge+monotonicNow().TotalSeconds-sourceAt>=MaximumSnapshotAgeSeconds);

    double PositionAt(double now)
    {
        if (!hasPosition) return 0;
        double elapsed = 0;
        if (advancing)
        {
            double snapshotBudget = MaximumSnapshotAgeSeconds - (sourceAge + anchorAt - sourceAt);
            double observationBudget = observedAt + MaximumObservationGapSeconds - anchorAt;
            elapsed = Math.Max(0, Math.Min(Math.Max(0, now - anchorAt), Math.Min(snapshotBudget, observationBudget)));
        }
        double position = anchorPosition + elapsed * rate;
        return Math.Clamp(position, 0, observation?.DurationSeconds ?? double.MaxValue);
    }

    internal double? ProjectPosition(PlaybackState raw)
    {
        if (!hasPosition || observation is null || raw.PositionSeconds is null) return raw.PositionSeconds;
        double position = PositionAt(monotonicNow().TotalSeconds);
        return Math.Abs(position - raw.PositionSeconds.Value) > .000001 ? position : raw.PositionSeconds;
    }

    internal PlaybackState Project(PlaybackState raw)
    {
        double? position = ProjectPosition(raw);
        return position != raw.PositionSeconds ? raw with { PositionSeconds = position, IsPositionEstimated = true, IsPlaybackRateAssumed = raw.PlaybackRate is null } : raw;
    }
}
