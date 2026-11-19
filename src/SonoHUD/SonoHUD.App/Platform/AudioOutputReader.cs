using System.Runtime.InteropServices;

namespace SonoHUD.App.Platform;

// Read-only subset of the independently authored M2 Windows SDK declarations.
// Each call owns/releases its COM references on one worker thread. No volume setters are invoked.
internal static class AudioOutputReader
{
    internal sealed record State(string Text, double Level);
    internal static State Read()
    {
        object? enumerator = null; Device? device = null; object? endpoint = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            ((Enumerator)enumerator!).GetDefaultAudioEndpoint(0, 1, out device);
            var id = typeof(Endpoint).GUID; device.Activate(ref id, 23, IntPtr.Zero, out endpoint);
            ((Endpoint)endpoint).GetMasterVolumeLevelScalar(out float level); ((Endpoint)endpoint).GetMute(out bool mute);
            return new($"기본 출력 음량 {level * 100:0}%" + (mute ? " · 음소거 중" : " · 소리 켜짐") + " · 읽기 전용",level*100);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException) { return new("출력 장치를 확인할 수 없습니다. Windows 소리 설정을 확인해 주세요.",0); }
        finally { Release(endpoint);Release(device);Release(enumerator); }
    }
    static void Release(object? value) { if(value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Enumerator { void EnumAudioEndpoints(int flow,uint mask,out IntPtr devices);void GetDefaultAudioEndpoint(int flow,int role,out Device device); }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Device { void Activate(ref Guid iid,uint context,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)] out object instance); }
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Endpoint
    {
        void RegisterControlChangeNotify(IntPtr callback);void UnregisterControlChangeNotify(IntPtr callback);void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float db,ref Guid context);void SetMasterVolumeLevelScalar(float value,ref Guid context);void GetMasterVolumeLevel(out float db);void GetMasterVolumeLevelScalar(out float value);
        void SetChannelVolumeLevel(uint channel,float db,ref Guid context);void SetChannelVolumeLevelScalar(uint channel,float value,ref Guid context);void GetChannelVolumeLevel(uint channel,out float db);void GetChannelVolumeLevelScalar(uint channel,out float value);
        void SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);void GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
    }
}
