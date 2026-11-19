using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SonoHUD.Core;

namespace SonoHUD.App.Platform;

internal sealed record SpectrumCaptureStatus(bool Connected, string Message, string DeviceId = "", int SampleRate = 0, int Channels = 0, int Bits = 0, int FftSize = 0, int HopSize = 0, long Analyses = 0, long Ffts = 0, long SilenceSkips = 0, long Discontinuities = 0, double AnalysisMilliseconds = 0, double LastPacketAgeMs = 0);
internal sealed record SpectrumOutput(string Id, string Name);

// System render-endpoint loopback only. No process injection, session extraction,
// volume setter, disk/network PCM output, UI callback or per-packet allocation.
internal sealed class SpectrumCapture : IDisposable
{
    readonly SpectrumSettings settings;
    readonly Thread worker;
    readonly ManualResetEvent stop = new(false);
    SpectrumCaptureStatus status = new(false, "출력 장치에 연결 중…");
    int disposed, restart;
    static int activeWorkers,totalWorkers;
    internal static int ActiveWorkers=>Volatile.Read(ref activeWorkers);
    internal static int TotalWorkers=>Volatile.Read(ref totalWorkers);
    internal SharedSpectrum Shared { get; }
    internal SpectrumCaptureStatus Status => Volatile.Read(ref status);
    internal static double Clock => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal SpectrumCapture(SpectrumSettings settings)
    {
        this.settings = settings.Sanitize(); Shared = new(this.settings.Bands);
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        worker = new Thread(Run) { IsBackground = true, Name = "SonoHUD shared spectrum" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Interlocked.Exchange(ref restart, 1); }
    void SessionChanged(object sender, SessionSwitchEventArgs e) { if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) Interlocked.Exchange(ref restart, 1); }
    void Run()
    {
        Interlocked.Increment(ref activeWorkers);Interlocked.Increment(ref totalWorkers);
        try
        {
            while (!stop.WaitOne(0))
            {
                try { Capture(); }
                catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException or ArgumentException or InvalidCastException)
                {
                    Volatile.Write(ref status, new(false, "출력 장치에 연결할 수 없습니다. 자동으로 다시 연결합니다. (" + error.HResult.ToString("X8") + ")"));
                    Shared.Publish([], new(0, Clock, 0, 0, 0, false));
                }
                if (stop.WaitOne(750)) break;
            }
        }
        finally { Shared.Publish([], new(0, Clock, 0, 0, 0, false));Interlocked.Decrement(ref activeWorkers); }
    }
    unsafe void Capture()
    {
        object? enumeratorObject = null, clientObject = null, captureObject = null; Device? device = null; IntPtr wave = IntPtr.Zero;
        AudioClient? client = null; bool started = false;
        using var ready = new AutoResetEvent(false);
        try
        {
            enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            var enumerator = (Enumerator)enumeratorObject;
            if (settings.DeviceId.Length == 0) enumerator.GetDefaultAudioEndpoint(0, 1, out device); else enumerator.GetDevice(settings.DeviceId, out device);
            device.GetId(out string deviceId);
            var clientId = typeof(AudioClient).GUID; device.Activate(ref clientId, 23, IntPtr.Zero, out clientObject); client = (AudioClient)clientObject;
            client.GetMixFormat(out wave); var format = ReadFormat(wave);
            client.Initialize(0, 0x20000 | 0x40000, 0, 0, wave, IntPtr.Zero); // shared, loopback, event callback
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            var captureId = typeof(CaptureClient).GUID; client.GetService(ref captureId, out captureObject); var capture = (CaptureClient)captureObject;
            var analyzer = new SpectrumAnalyzer(format.SampleRate, format.Channels, settings);
            var scratch = new float[2048 * format.Channels];
            analyzer.Analyzed += a => Shared.Publish(a.Levels, new(a.Sequence, Clock, (double)a.HopSize / a.SampleRate, a.Rms, a.PeakFrequencyHz, true));
            client.Start(); started = true; Interlocked.Exchange(ref restart, 0);
            double lastPacket = Clock, checkAt = 0, silentAt = 0; long discontinuities = 0;
            var handles = new WaitHandle[] { stop, ready };
            while (!stop.WaitOne(0) && Volatile.Read(ref restart) == 0)
            {
                if (WaitHandle.WaitAny(handles, 100) == 0) break;
                capture.GetNextPacketSize(out uint count);
                while (count > 0)
                {
                    capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                    try
                    {
                        if ((flags & 1) != 0) { analyzer.Reset(); discontinuities++; }
                        bool silent = (flags & 2) != 0;
                        for (int frame = 0; frame < frames;)
                        {
                            int chunk = (int)Math.Min(2048, frames - frame), samples = chunk * format.Channels;
                            if (silent) scratch.AsSpan(0, samples).Clear();
                            else
                            {
                                if (data == IntPtr.Zero) throw new InvalidOperationException("PCM 버퍼가 없습니다.");
                                var bytes = new ReadOnlySpan<byte>((byte*)data + frame * format.BlockAlign, chunk * format.BlockAlign);
                                PcmDecoder.Decode(bytes, scratch.AsSpan(0, samples), format);
                            }
                            analyzer.Push(scratch.AsSpan(0, samples)); frame += chunk;
                        }
                    }
                    finally { capture.ReleaseBuffer(frames); }
                    lastPacket = Clock; capture.GetNextPacketSize(out count);
                }
                double now = Clock;
                // Shared loopback may stop delivering packets during genuine silence.
                // Clear the display without restarting a healthy silent endpoint in a loop.
                if (now - lastPacket > .15 && now - silentAt > .1)
                { analyzer.Reset(); Shared.Publish([], new(analyzer.Sequence, now, .02, 0, 0, true)); silentAt = now; }
                if (now >= checkAt)
                {
                    checkAt = now + 1; device.GetState(out uint state); if ((state & 1) == 0) throw new InvalidOperationException("출력 장치가 연결 해제되었습니다.");
                    client.GetCurrentPadding(out _); // Detect invalidation even without packet callbacks.
                    if (settings.DeviceId.Length == 0)
                    {
                        Device? current = null;
                        try { enumerator.GetDefaultAudioEndpoint(0, 1, out current); current.GetId(out string id); if (id != deviceId) break; }
                        finally { Release(current); }
                    }
                    Volatile.Write(ref status, new(true, "시스템 출력의 혼합 오디오 · 주파수 분석 중", deviceId, format.SampleRate, format.Channels, format.Bits, analyzer.FftSize, analyzer.HopSize, analyzer.Sequence, analyzer.FftCount, analyzer.SilenceSkips, discontinuities, analyzer.AnalysisTicks * 1000.0 / Stopwatch.Frequency, (now - lastPacket) * 1000));
                }
            }
        }
        finally
        {
            if (started) try { client?.Stop(); } catch (COMException) { }
            if (wave != IntPtr.Zero) Marshal.FreeCoTaskMem(wave);
            Release(captureObject); Release(clientObject); Release(device); Release(enumeratorObject);
        }
    }
    internal static PcmFormat ReadFormat(IntPtr pointer)
    {
        int tag = (ushort)Marshal.ReadInt16(pointer), channels = (ushort)Marshal.ReadInt16(pointer, 2), rate = Marshal.ReadInt32(pointer, 4);
        int align = (ushort)Marshal.ReadInt16(pointer, 12), bits = (ushort)Marshal.ReadInt16(pointer, 14);
        if (tag == 0xfffe)
        {
            if ((ushort)Marshal.ReadInt16(pointer, 16) < 22) throw new NotSupportedException("잘못된 확장 PCM 형식입니다.");
            Guid subtype = Marshal.PtrToStructure<Guid>(pointer + 24);
            if (subtype == new Guid("00000001-0000-0010-8000-00aa00389b71")) tag = 1;
            else if (subtype == new Guid("00000003-0000-0010-8000-00aa00389b71")) tag = 3;
            else throw new NotSupportedException("PCM/Float 출력만 지원합니다.");
        }
        if (tag is not (1 or 3)) throw new NotSupportedException("PCM/Float 출력만 지원합니다.");
        var format = new PcmFormat(rate, channels, bits, tag == 3, align); format.Validate(); return format;
    }
    internal static IReadOnlyList<SpectrumOutput> Outputs()
    {
        var results = new List<SpectrumOutput> { new("", "Windows 기본 출력 따라가기") };
        object? instance = null; DeviceCollection? collection = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            ((Enumerator)instance).EnumAudioEndpoints(0, 1, out collection); collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                Device? endpoint = null; PropertyStore? properties = null; PropVariant value = default;
                try
                {
                    collection.Item(i, out endpoint); endpoint.GetId(out string id); endpoint.OpenPropertyStore(0, out properties);
                    var key = new PropertyKey { Format = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                    properties.GetValue(ref key, out value); string name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? id : id;
                    results.Add(new(id, name));
                }
                finally { PropVariantClear(ref value); Release(properties); Release(endpoint); }
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException) { }
        finally { Release(collection); Release(instance); }
        return results;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
        stop.Set();
        // Join ensures a replacement never opens a duplicate capture before this stream closes.
        worker.Join(); stop.Dispose();
    }
    static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { internal Guid Format; internal uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal IntPtr Pointer; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Enumerator { void EnumAudioEndpoints(int flow, uint state, out DeviceCollection devices); void GetDefaultAudioEndpoint(int flow, int role, out Device device); void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out Device device); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DeviceCollection { void GetCount(out uint count); void Item(uint index, out Device device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Device { void Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance); void OpenPropertyStore(uint mode, out PropertyStore properties); void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id); void GetState(out uint state); }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface PropertyStore { void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out PropVariant value); }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface AudioClient
    {
        void Initialize(int mode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        void GetBufferSize(out uint frames); void GetStreamLatency(out long latency); void GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format); void GetDevicePeriod(out long normal, out long minimum);
        void Start(); void Stop(); void Reset(); void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface CaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames); void GetNextPacketSize(out uint frames);
    }
}
