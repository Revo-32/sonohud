namespace SonoHUD.Core;

public readonly record struct SpectrumStamp(long Sequence, double Seconds, double AnalysisInterval, double Rms, double PeakHz, bool Connected);

// Bounded latest-value mailbox: the reader never holds references to writer memory.
// A 96-float copy under this lock cannot queue UI callbacks or grow with a slow consumer.
public sealed class SharedSpectrum(int bands)
{
    readonly object sync = new();
    readonly float[] previous = new float[bands], latest = new float[bands];
    SpectrumStamp stamp;
    double previousSeconds;
    public void Publish(ReadOnlySpan<float> values, SpectrumStamp next)
    {
        lock (sync)
        {
            latest.CopyTo(previous, 0); previousSeconds = stamp.Seconds;
            Array.Clear(latest); values[..Math.Min(values.Length, latest.Length)].CopyTo(latest); stamp = next;
        }
    }
    public SpectrumStamp CopyTo(Span<float> destination, double now, bool interpolate = true)
    {
        lock (sync)
        {
            int count = Math.Min(destination.Length, latest.Length);
            double span = stamp.Seconds - previousSeconds;
            double mix = interpolate && span > 0 && span < .2 ? Math.Clamp((now - stamp.AnalysisInterval - previousSeconds) / span, 0, 1) : 1;
            bool stale = !stamp.Connected || now - stamp.Seconds > .3;
            for (int b = 0; b < count; b++) destination[b] = stale ? 0 : previous[b] + (latest[b] - previous[b]) * (float)mix;
            destination[count..].Clear(); return stamp;
        }
    }
}
