namespace SonoHUD.Core;
// A media-properties change and a timeline update are separate SMTC events.
public sealed class TimelineGate
{
    DateTimeOffset previous, waitingAfter;
    public void Reset() { previous = default; waitingAfter = default; }
    public bool Accept(bool trackChanged, DateTimeOffset updated)
    {
        if (trackChanged && previous != default) waitingAfter = previous;
        if (updated == default || updated < previous || waitingAfter != default && updated <= waitingAfter) return false;
        waitingAfter = default;
        previous = updated;
        return true;
    }
}
