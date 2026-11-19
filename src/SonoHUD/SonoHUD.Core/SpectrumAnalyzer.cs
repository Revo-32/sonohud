using System.Diagnostics;

namespace SonoHUD.Core;

/// <summary>Allocation-free, single-writer STFT. Outputs are valid until the next Push.</summary>
public sealed class SpectrumAnalyzer
{
    readonly double[][] ring;
    readonly double[] window, real, imaginary, powers, twiddleReal, twiddleImaginary;
    readonly int[] reverse;
    readonly double[] lowBins, highBins;
    readonly SpectrumSettings settings;
    readonly double normalization;
    int write, filled, sinceAnalysis;
    double gainDb;
    public int SampleRate { get; }
    public int Channels { get; }
    public int FftSize { get; }
    public int HopSize { get; }
    public double BinHz => (double)SampleRate / FftSize;
    public long Sequence { get; private set; }
    public long AnalysisTicks { get; private set; }
    public long FftCount { get; private set; }
    public long SilenceSkips { get; private set; }
    public double Rms { get; private set; }
    public double PeakFrequencyHz { get; private set; }
    public double TotalPower { get; private set; }
    public ReadOnlySpan<float> Levels => levels;
    public ReadOnlySpan<double> BandPower => bandPower;
    readonly float[] levels;
    readonly double[] bandPower;
    public event Action<SpectrumAnalyzer>? Analyzed;

    public SpectrumAnalyzer(int sampleRate, int channels, SpectrumSettings? options = null, int baseFftSize = 4096, int overlapPercent = 75)
    {
        if (sampleRate is < 8000 or > 384000 || channels is < 1 or > 32 || baseFftSize is not (2048 or 4096) || overlapPercent is not (50 or 75))
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate; Channels = channels; settings = (options ?? new()).Sanitize();
        FftSize = baseFftSize;
        while (FftSize < baseFftSize * sampleRate / 48000.0) FftSize *= 2;
        HopSize = FftSize * (100 - overlapPercent) / 100;
        ring = new double[channels][];
        for (int c = 0; c < channels; c++) ring[c] = new double[FftSize];
        window = new double[FftSize]; real = new double[FftSize]; imaginary = new double[FftSize]; powers = new double[FftSize / 2 + 1];
        twiddleReal = new double[FftSize / 2]; twiddleImaginary = new double[FftSize / 2]; reverse = new int[FftSize];
        double windowEnergy = 0;
        int bits = (int)Math.Log2(FftSize);
        for (int i = 0; i < FftSize; i++)
        {
            window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / FftSize); windowEnergy += window[i] * window[i];
            int value = i, reversed = 0; for (int j = 0; j < bits; j++) { reversed = (reversed << 1) | (value & 1); value >>= 1; } reverse[i] = reversed;
            if (i < FftSize / 2) { twiddleReal[i] = Math.Cos(-2 * Math.PI * i / FftSize); twiddleImaginary[i] = Math.Sin(-2 * Math.PI * i / FftSize); }
        }
        normalization = 1 / (FftSize * windowEnergy * channels);
        levels = new float[settings.Bands]; bandPower = new double[settings.Bands]; lowBins = new double[settings.Bands]; highBins = new double[settings.Bands];
        double upper = Math.Min(settings.MaximumHz, sampleRate / 2.0);
        for (int b = 0; b < settings.Bands; b++)
        {
            lowBins[b] = settings.MinimumHz * Math.Pow(upper / settings.MinimumHz, (double)b / settings.Bands) / BinHz;
            highBins[b] = settings.MinimumHz * Math.Pow(upper / settings.MinimumHz, (b + 1.0) / settings.Bands) / BinHz;
        }
    }
    public (double Low, double High) BandRange(int band) => (lowBins[band] * BinHz, highBins[band] * BinHz);
    public void Reset()
    {
        foreach (var channel in ring) Array.Clear(channel);
        Array.Clear(levels); Array.Clear(bandPower); write = filled = sinceAnalysis = 0; gainDb = 0;
    }
    public void Push(ReadOnlySpan<float> interleaved)
    {
        if (interleaved.Length % Channels != 0) throw new ArgumentException("불완전한 PCM 프레임입니다.");
        for (int index = 0; index < interleaved.Length; index += Channels)
        {
            for (int c = 0; c < Channels; c++) { float v = interleaved[index + c]; ring[c][write] = float.IsFinite(v) ? Math.Clamp(v, -1f, 1f) : 0; }
            write = (write + 1) & (FftSize - 1); filled = Math.Min(filled + 1, FftSize); sinceAnalysis++;
            if (filled == FftSize && sinceAnalysis >= HopSize) { sinceAnalysis = 0; Analyze(); }
        }
    }
    void Analyze()
    {
        long start = Stopwatch.GetTimestamp();
        Array.Clear(powers); Array.Clear(bandPower);
        double energy = 0;
        for (int c = 0; c < Channels; c++) for (int i = 0; i < FftSize; i++) energy += ring[c][i] * ring[c][i];
        Rms = Math.Sqrt(energy / (FftSize * Channels));
        if (Rms < .000126) // -78 dBFS; no adaptive gain on silence or a numerical noise floor.
        {
            Array.Clear(levels); TotalPower = 0; PeakFrequencyHz = 0; SilenceSkips++;
        }
        else
        {
            for (int c = 0; c < Channels; c++)
            {
                for (int i = 0; i < FftSize; i++) { real[reverse[i]] = ring[c][(write + i) & (FftSize - 1)] * window[i]; imaginary[i] = 0; }
                Transform(); FftCount++;
                for (int k = 1; k <= FftSize / 2; k++) powers[k] += (real[k] * real[k] + imaginary[k] * imaginary[k]) * normalization * (k == FftSize / 2 ? 1 : 2);
            }
            int peak = 1; TotalPower = 0;
            for (int k = 1; k < powers.Length; k++) { TotalPower += powers[k]; if (powers[k] > powers[peak]) peak = k; }
            // Log-parabolic interpolation reports sub-bin frequency, not extra resolvable bands.
            double offset = 0;
            if (peak > 1 && peak + 1 < powers.Length)
            {
                double l = Math.Log(Math.Max(1e-30, powers[peak - 1])), m = Math.Log(Math.Max(1e-30, powers[peak])), r = Math.Log(Math.Max(1e-30, powers[peak + 1]));
                double denominator = l - 2 * m + r; if (Math.Abs(denominator) > 1e-15) offset = Math.Clamp(.5 * (l - r) / denominator, -.5, .5);
            }
            PeakFrequencyHz = (peak + offset) * BinHz;
            double maximumDb = -120;
            for (int b = 0; b < levels.Length; b++)
            {
                int first = Math.Max(1, (int)Math.Floor(lowBins[b] - .5)), last = Math.Min(FftSize / 2, (int)Math.Ceiling(highBins[b] + .5));
                double power = 0;
                for (int k = first; k <= last; k++) power += powers[k] * Math.Max(0, Math.Min(highBins[b], k + .5) - Math.Max(lowBins[b], k - .5));
                bandPower[b] = power; maximumDb = Math.Max(maximumDb, 10 * Math.Log10(Math.Max(1e-12, 2 * power)));
            }
            double desired = settings.AutoGain ? Math.Clamp(-maximumDb - 3, 0, 24) : 0;
            // Attenuation adapts quickly, amplification only after seconds: quiet passages stay quiet.
            double seconds = (double)HopSize / SampleRate;
            gainDb += (desired - gainDb) * (1 - Math.Exp(-seconds / (desired < gainDb ? .08 : 5)));
            for (int b = 0; b < levels.Length; b++)
            {
                double db = 10 * Math.Log10(Math.Max(1e-12, 2 * bandPower[b])) + gainDb + settings.SensitivityDb;
                levels[b] = (float)Math.Clamp((db + settings.DynamicRangeDb) / settings.DynamicRangeDb * settings.Intensity, 0, 1);
            }
        }
        Sequence++; AnalysisTicks += Stopwatch.GetTimestamp() - start; Analyzed?.Invoke(this);
    }
    void Transform()
    {
        for (int length = 2; length <= FftSize; length <<= 1)
        {
            int half = length / 2, step = FftSize / length;
            for (int begin = 0; begin < FftSize; begin += length)
            for (int j = 0; j < half; j++)
            {
                int a = begin + j, b = a + half, w = j * step;
                double r = real[b] * twiddleReal[w] - imaginary[b] * twiddleImaginary[w], im = real[b] * twiddleImaginary[w] + imaginary[b] * twiddleReal[w];
                real[b] = real[a] - r; imaginary[b] = imaginary[a] - im; real[a] += r; imaginary[a] += im;
            }
        }
    }
}
