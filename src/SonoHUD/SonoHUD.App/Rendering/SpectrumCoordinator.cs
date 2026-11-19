using System.Windows.Media;
using System.Windows.Threading;
using SonoHUD.App.Platform;
using SonoHUD.Core;

namespace SonoHUD.App.Rendering;

internal delegate void SpectrumFrame(ReadOnlySpan<float> values,ReadOnlySpan<float> peaks);

// One capture and one UI clock for all presentation surfaces in the process.
internal sealed class SpectrumCoordinator : IDisposable
{
    SpectrumCapture? capture;
    SpectrumSettings settings = new();
    SpectrumEnvelope envelope = new(64);
    float[] target = new float[64];
    double lastStatus;
    SpectrumFrameClock? clock;
    AnimationProfile profile;
    bool hasVisibleSignal;
    internal event SpectrumFrame? Frame;
    internal event Action<string>? StatusChanged;
    // Kept as a diagnostic compatibility field; no Rendering-event frame pump.
    internal long RenderingEvents => 0;
    internal long SubmittedFrames { get; private set; }
    internal SpectrumCaptureStatus? CaptureStatus => capture?.Status;
    internal int EffectiveLimit { get; private set; } = 60;
    internal double LastSnapshotAgeMs { get; private set; }
    internal void Configure(SpectrumSettings nextSettings, AnimationProfile motion, bool visible, int displayHz)
    {
        var sanitized = nextSettings.Sanitize(); profile = motion;
        bool restart = sanitized.Bands != settings.Bands || sanitized.MinimumHz != settings.MinimumHz || sanitized.MaximumHz != settings.MaximumHz || sanitized.SensitivityDb != settings.SensitivityDb || sanitized.DynamicRangeDb != settings.DynamicRangeDb || sanitized.AutoGain != settings.AutoGain || sanitized.Intensity != settings.Intensity || sanitized.DeviceId != settings.DeviceId;
        settings = sanitized;
        int limit = Math.Min(settings.FrameLimit, Math.Max(30, displayHz));
        if (limit != EffectiveLimit) { clock?.Dispose(); clock = null; }
        EffectiveLimit = limit;
        if (!settings.Enabled || !visible) { Stop(); StatusChanged?.Invoke(settings.Enabled ? "표시할 화면이 없어 분석을 쉬고 있습니다." : "꺼짐"); return; }
        if (restart) Stop();
        if (capture is null)
        {
            target = new float[settings.Bands]; envelope = new(settings.Bands); capture = new(settings);
            StatusChanged?.Invoke("시스템 출력에 연결 중…");
        }
        clock ??= new(Dispatcher.CurrentDispatcher, EffectiveLimit, DrawFrame);
    }
    void DrawFrame()
    {
        if (capture is null) return;
        double now = SpectrumCapture.Clock;
        var stamp = capture.Shared.CopyTo(target, now); envelope.Advance(target, now, settings, profile);
        LastSnapshotAgeMs=stamp.Seconds>0?Math.Max(0,(now-stamp.Seconds)*1000):0;
        bool signal=false;
        foreach(float value in envelope.Values)if(value>.0001f){signal=true;break;}
        if(!signal)foreach(float value in envelope.Peaks)if(value>.0001f){signal=true;break;}
        // A decayed silent vector is drawn once, then only the lightweight clock polls.
        if(signal || hasVisibleSignal){Frame?.Invoke(envelope.Values,envelope.Peaks);SubmittedFrames++;}
        hasVisibleSignal=signal;
        if (now - lastStatus >= 1)
        {
            lastStatus = now;
            StatusChanged?.Invoke(!capture.Status.Connected ? capture.Status.Message : stamp.Rms < .000126 ? "시스템 출력이 조용합니다." : "시스템 출력의 혼합 오디오 · 주파수 분석 중");
        }
    }
    void Stop()
    {
        if (clock is null && capture is null) return;
        clock?.Dispose(); clock = null;
        capture?.Dispose(); capture = null; envelope.Reset(); Array.Clear(target); hasVisibleSignal=false; Frame?.Invoke([],[]);
    }
    public void Dispose() { Stop(); Frame = null; StatusChanged = null; }
}

// One private, drift-corrected clock also works when WPF windows are minimized.
// Only one dispatcher operation may be pending; late frames are discarded.
internal sealed class SpectrumFrameClock : IDisposable
{
    readonly Thread worker;
    int disposed, queued;
    internal SpectrumFrameClock(Dispatcher dispatcher, int rate, Action draw)
    {
        worker = new Thread(() =>
        {
            using var cadence = new HighResolutionCadence(rate);
            while (Volatile.Read(ref disposed) == 0)
            {
                cadence.WaitNext();
                if (Volatile.Read(ref disposed) != 0 || dispatcher.HasShutdownStarted) break;
                if (Interlocked.CompareExchange(ref queued, 1, 0) != 0) continue;
                try { dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    Interlocked.Exchange(ref queued, 0);
                    if (Volatile.Read(ref disposed) == 0) draw();
                })); }
                catch (InvalidOperationException) { break; }
            }
        }) { IsBackground = true, Name = "SonoHUD spectrum frame clock" };
        worker.Start();
    }
    public void Dispose() { Interlocked.Exchange(ref disposed, 1); worker.Join(); }
}
