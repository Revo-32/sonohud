using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SonoHUD.App.Platform;

// A private waitable timer: no global timeBeginPeriod and no spin/busy wait.
internal sealed class HighResolutionCadence : IDisposable
{
    readonly SafeWaitHandle timer;
    readonly double interval;
    double next;
    internal HighResolutionCadence(double framesPerSecond = 60)
    {
        interval=Stopwatch.Frequency/framesPerSecond;next=Stopwatch.GetTimestamp();
        timer=CreateWaitableTimerExW(IntPtr.Zero,null,2,0x1F0003);
        if(timer.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    internal void WaitNext()
    {
        next+=interval;double now=Stopwatch.GetTimestamp();
        if(next<=now) {next=now;return;}
        long due=-Math.Max(1,(long)((next-now)*TimeSpan.TicksPerSecond/Stopwatch.Frequency));
        if(!SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if(WaitForSingleObject(timer,1000)!=0)throw new InvalidOperationException("프레임 시계 응답이 없습니다.");
    }
    public void Dispose()=>timer.Dispose();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long dueTime,int period,IntPtr completion,IntPtr arg,[MarshalAs(UnmanagedType.Bool)]bool resume);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(SafeWaitHandle handle,uint milliseconds);
}
