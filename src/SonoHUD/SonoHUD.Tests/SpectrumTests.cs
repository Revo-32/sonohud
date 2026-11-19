using System.Diagnostics;
using System.Text.Json;
using SonoHUD.Core;

internal static class SpectrumTests
{
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    internal static void Run(Action<string, Action> check, string output)
    {
        var measurements = new List<object>();
        foreach (int rate in new[] { 44100, 48000, 96000, 192000 })
        foreach (int size in new[] { 2048, 4096 })
        foreach (int overlap in new[] { 50, 75 })
        check($"DSP sine energy/peak {rate}Hz FFT{size} overlap{overlap}", () =>
        {
            foreach (double frequency in new[] { 60.0, 100, 440, 1000, 8000 })
            {
                var analyzer = new SpectrumAnalyzer(rate, 2, new() { AutoGain = false }, size, overlap);
                var pcm = Tone(rate, frequency, 2, rate / 2, .5, false);
                long started = Stopwatch.GetTimestamp(); analyzer.Push(pcm); double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                double error = Math.Abs(analyzer.PeakFrequencyHz - frequency);
                Assert(error < analyzer.BinHz * .55, $"peak {frequency}: {analyzer.PeakFrequencyHz}");
                Assert(Math.Abs(analyzer.TotalPower - .125) < .006, $"RMS energy {analyzer.TotalPower}");
                double mapped = 0; int peakBand = 0; for (int i = 0; i < analyzer.BandPower.Length; i++) { mapped += analyzer.BandPower[i]; if (analyzer.BandPower[i] > analyzer.BandPower[peakBand]) peakBand = i; }
                Assert(mapped <= analyzer.TotalPower * 1.000001, "band energy duplicated");
                var range = analyzer.BandRange(peakBand);
                Assert(frequency >= range.Low - analyzer.BinHz * 2 && frequency <= range.High + analyzer.BinHz * 2, "log peak mapping");
                measurements.Add(new { rate, size = analyzer.FftSize, overlap, frequency, measured = analyzer.PeakFrequencyHz, error, power = analyzer.TotalPower, mapped, elapsedMs = elapsed, analysisHz = (double)rate / analyzer.HopSize, windowMs = 1000.0 * analyzer.FftSize / rate });
            }
        });
        check("DSP mono/stereo/8ch power and opposite-phase preservation", () =>
        {
            double? reference = null;
            foreach (int channels in new[] { 1, 2, 8 })
            {
                var a = new SpectrumAnalyzer(48000, channels); a.Push(Tone(48000, 440, channels, 24000, .5, true));
                reference ??= a.TotalPower; Assert(Math.Abs(a.TotalPower - reference.Value) < 1e-6, "channel cancellation or unnormalized energy");
            }
        });
        check("DSP silence and low signal do not inflate noise", () =>
        {
            var a = new SpectrumAnalyzer(48000, 2); a.Push(new float[48000]);
            Assert(a.Levels.ToArray().All(x => x == 0) && a.FftCount == 0 && a.SilenceSkips > 0, "silence FFT/gate");
            a.Push(Tone(48000, 1000, 2, 48000, .00001, false)); Assert(a.Levels.ToArray().All(x => x == 0), "noise gate");
            a.Push(Tone(48000, 1000, 2, 48000, .001, false)); Assert(Math.Abs(a.PeakFrequencyHz - 1000) < 2, "low-level frequency lost");
        });
        check("DSP sweep follows frequency rather than master level", () =>
        {
            var a = new SpectrumAnalyzer(48000, 1); int matched = 0;
            for (int step = 0; step < 24; step++)
            {
                double f = 60 * Math.Pow(16000.0 / 60, step / 23.0); a.Push(Tone(48000, f, 1, 8192, .4, false));
                if (Math.Abs(a.PeakFrequencyHz - f) < a.BinHz) matched++;
            }
            Assert(matched == 24, "sweep peaks " + matched);
        });
        check("DSP white/pink noise frequency energy distribution", () =>
        {
            var random = new Random(415); var white = new float[48000]; var pink = new float[48000]; double b0 = 0, b1 = 0, b2 = 0;
            for (int i = 0; i < white.Length; i++)
            {
                double w = random.NextDouble() * 2 - 1; white[i] = (float)(w * .25);
                b0 = .99765 * b0 + .0990460 * w; b1 = .96300 * b1 + .2965164 * w; b2 = .57000 * b2 + 1.0526913 * w;
                pink[i] = (float)((b0 + b1 + b2 + w * .1848) * .05);
            }
            static double[] Average(float[] pcm)
            {
                var a = new SpectrumAnalyzer(48000, 1); double[] sum = new double[64]; int n = 0;
                a.Analyzed += x => { for (int i = 0; i < 64; i++) sum[i] += x.BandPower[i]; n++; }; a.Push(pcm);
                for (int i = 0; i < 64; i++) sum[i] /= n; return sum;
            }
            var whitePower = Average(white); var p = Average(pink);
            double whiteRatio = whitePower.Skip(44).Sum() / whitePower.Skip(20).Take(20).Sum();
            double pinkRatio = p.Skip(44).Sum() / p.Skip(20).Take(20).Sum();
            Assert(whiteRatio > 4 && pinkRatio < whiteRatio / 2, $"noise spectral slopes white={whiteRatio},pink={pinkRatio}");
            measurements.Add(new { signal = "noise", whiteRatio, pinkRatio, white = whitePower, pink = p });
        });
        check("DSP transient/clipping/nonfinite signals stay finite and bounded", () =>
        {
            var a = new SpectrumAnalyzer(48000, 1); var x = new float[16000]; x[5000] = 1;
            a.Push(x); Assert(a.Levels.ToArray().All(v => float.IsFinite(v) && v is >= 0 and <= 1), "transient");
            x = Tone(48000, 100, 1, 16000, 3, false); x[0] = float.NaN; x[1] = float.PositiveInfinity; a.Push(x);
            Assert(a.Levels.ToArray().All(v => float.IsFinite(v) && v is >= 0 and <= 1), "clip"); Assert(a.TotalPower <= 1.01, "clip normalization");
            a.Reset(); a.Push(new float[8192]); Assert(a.Levels.ToArray().All(v => v == 0), "reset leakage");
        });
        check("DSP attack/release identical physical response at 60/120/240fps", () =>
        {
            var settings = new SpectrumSettings(); var final = new List<float>();
            foreach (int fps in new[] { 60, 120, 240 })
            {
                var envelope = new SpectrumEnvelope(1); envelope.Advance([1], 0, settings, AnimationProfile.Calm);
                for (int i = 1; i <= fps; i++) envelope.Advance([1], (double)i / fps, settings, AnimationProfile.Calm);
                for (int i = 1; i <= fps / 2; i++) envelope.Advance([0], 1 + (double)i / fps, settings, AnimationProfile.Calm);
                final.Add(envelope.Values[0]); Assert(envelope.Peaks[0] >= envelope.Values[0], "peak decay below value");
            }
            Assert(final.Max() - final.Min() < .00001, "frame-dependent envelope"); measurements.Add(new { signal = "envelope", fps = new[] { 60, 120, 240 }, values = final });
        });
        check("DSP PCM float32/64 integer8/16/24/32 decode and silent packets", () =>
        {
            foreach (var (bits, floating) in new[] { (8, false), (16, false), (24, false), (32, false), (32, true), (64, true) })
            {
                var bytes = bits switch { 8 => new byte[] { 192 }, 16 => BitConverter.GetBytes((short)16384), 24 => new byte[] { 0, 0, 64 }, 32 when floating => BitConverter.GetBytes(.5f), 32 => BitConverter.GetBytes(1073741824), _ => BitConverter.GetBytes(.5) };
                float[] values = new float[1]; var format = new PcmFormat(48000, 1, bits, floating, bits / 8);
                PcmDecoder.Decode(bytes, values, format); Assert(Math.Abs(values[0] - .5) < .00001, "decode " + bits + floating);
                PcmDecoder.Decode(bytes, values, format, true); Assert(values[0] == 0, "silent packet");
            }
        });
        check("DSP bounded latest snapshot interpolation and stale fallback", () =>
        {
            var shared = new SharedSpectrum(2); shared.Publish([0, 1], new(1, 1, .02, 1, 440, true)); shared.Publish([1, 0], new(2, 1.02, .02, 1, 440, true));
            float[] result = new float[2]; shared.CopyTo(result, 1.03); Assert(Math.Abs(result[0] - .5) < .001 && Math.Abs(result[1] - .5) < .001, "interpolation");
            shared.CopyTo(result, 2); Assert(result.All(x => x == 0), "stale freeze");
        });
        check("DSP settings reject nonfinite values by safe correction", () =>
        {
            var s = new SpectrumSettings { Bands = -10, AttackMs = double.NaN, MaximumHz = double.PositiveInfinity, FrameLimit = 999, Style = (SpectrumStyle)999 }.Sanitize();
            Assert(s.Bands == 12 && s.AttackMs == 35 && s.FrameLimit == 60 && s.Style == SpectrumStyle.Preset, "sanitize");
        });
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "spectrum-accuracy.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        var performance=new List<object>();
        foreach(int rate in new[]{48000,192000})foreach(int size in new[]{2048,4096})foreach(int overlap in new[]{50,75})
        {
            var analyzer=new SpectrumAnalyzer(rate,2,new(){AutoGain=false},size,overlap);
            var pcm=Tone(rate,440,2,rate,.5,false);analyzer.Push(pcm);
            long allocated=GC.GetAllocatedBytesForCurrentThread(),ticks=analyzer.AnalysisTicks,seq=analyzer.Sequence;var watch=Stopwatch.StartNew();
            for(int i=0;i<10;i++)analyzer.Push(pcm);
            watch.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            performance.Add(new{rate,baseFft=size,overlap,fft=analyzer.FftSize,hop=analyzer.HopSize,binHz=analyzer.BinHz,windowMs=analyzer.FftSize*1000d/rate,hopMs=analyzer.HopSize*1000d/rate,audioSeconds=10,wallMs=watch.Elapsed.TotalMilliseconds,analysisMs=(analyzer.AnalysisTicks-ticks)*1000d/Stopwatch.Frequency,analyses=analyzer.Sequence-seq,allocatedBytes=bytes});
        }
        File.WriteAllText(Path.Combine(output,"spectrum-benchmark.json"),JsonSerializer.Serialize(performance,new JsonSerializerOptions{WriteIndented=true}));
    }
    static float[] Tone(int rate, double frequency, int channels, int frames, double amplitude, bool anti)
    {
        var pcm = new float[frames * channels];
        for (int i = 0; i < frames; i++) for (int c = 0; c < channels; c++) pcm[i * channels + c] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate) * (anti && c % 2 == 1 ? -1 : 1));
        return pcm;
    }
}
