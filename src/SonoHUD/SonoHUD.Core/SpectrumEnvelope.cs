namespace SonoHUD.Core;

// One visual vector, shared by all views. A constant target has the same exact
// exponential response at 60/120/240Hz; no per-frame fixed coefficients.
public sealed class SpectrumEnvelope(int bands)
{
    readonly float[] values = new float[bands], peaks = new float[bands];
    readonly double[] heldUntil = new double[bands];
    double previous = double.NaN;
    public ReadOnlySpan<float> Values => values;
    public ReadOnlySpan<float> Peaks => peaks;
    public void Reset() { Array.Clear(values); Array.Clear(peaks); Array.Clear(heldUntil); previous = double.NaN; }
    public void Advance(ReadOnlySpan<float> target, double seconds, SpectrumSettings settings, AnimationProfile profile)
    {
        if (!double.IsFinite(seconds)) return;
        double dt = double.IsFinite(previous) ? Math.Clamp(seconds - previous, 0, .25) : 0; previous = seconds;
        double attack = settings.AttackMs / 1000, release = settings.ReleaseMs / 1000;
        if (profile == AnimationProfile.Dynamic) { attack *= .55; release *= .7; }
        else if (profile == AnimationProfile.Cinematic) { attack *= 1.4; release *= 1.6; }
        float attackMix = (float)(1 - Math.Exp(-dt / Math.Max(.001, attack)));
        float releaseMix = (float)(1 - Math.Exp(-dt / Math.Max(.001, release)));
        for (int b = 0; b < values.Length; b++)
        {
            float next = b < target.Length && float.IsFinite(target[b]) ? Math.Clamp(target[b], 0, 1) : 0;
            values[b] += (next - values[b]) * (next > values[b] ? attackMix : releaseMix);
            if (values[b] < .0001 && next == 0) values[b] = 0;
            if (values[b] >= peaks[b]) { peaks[b] = values[b]; heldUntil[b] = seconds + .12; }
            else if (seconds > heldUntil[b]) peaks[b] = Math.Max(values[b], peaks[b] - (float)(Math.Min(dt,seconds-heldUntil[b]) * .9));
        }
    }
}
