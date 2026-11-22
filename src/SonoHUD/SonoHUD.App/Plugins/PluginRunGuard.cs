using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SonoHUD.App.Plugins;

internal sealed class PluginRecoveryException(string state, string detail) : Exception(detail)
{
    internal string State { get; } = state;
}

internal sealed class PluginRunGuard
{
    sealed record Record(string Hash,Guid Session,int Pid,long ProcessStartTicks,string Phase,int Failures,DateTimeOffset PhaseUtc);
    static readonly long StartTicks = GetStartTicks();
    const int MaxFailures = 3;
    readonly string file;
    Record current;
    internal string Detail { get; }

    PluginRunGuard(string file,Record current,string detail)
    {
        this.file=file; this.current=current; Detail=detail;
        Write("initializing");
    }
    static long GetStartTicks()
    {
        using var process=Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime().Ticks;
    }

    internal static PluginRunGuard Begin(string settings,string hash,Guid session,bool manual)
    {
        string file=Path.Combine(settings,"running.lock");
        string detail="";
        int failures=0;
        if(PluginApprovalStore.CheckExistingFile(file))
        {
            Record? previous=null;
            // v1.0.0 wrote only the package hash, without a process owner.
            using (var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length<=8192)
                {
                    try { previous=JsonSerializer.Deserialize<Record>(stream); }
                    catch(JsonException) { }
                }
            }
            if(previous is {Session:var owner} && owner!=Guid.Empty && IsLive(previous))
                throw new PluginRecoveryException("다른 실행에서 사용 중","다른 실행이 이 확장을 사용 중입니다. 해당 실행을 종료한 뒤 다시 시도하세요.");
            detail="이전 실행의 잠금 기록을 복구했습니다.";
            if(previous?.Hash==hash && !manual)
            {
                bool startupInterrupted=previous.Phase is "initializing" or "stopping" or "failed";
                var age=DateTimeOffset.UtcNow-previous.PhaseUtc;
                bool quickRestart=previous.Phase=="running" && age>=TimeSpan.Zero && age<TimeSpan.FromSeconds(90);
                if(startupInterrupted || quickRestart) failures=Math.Clamp(previous.Failures,0,MaxFailures)+1;
                if(previous.Phase=="paused" || failures>=MaxFailures)
                {
                    if(previous.Phase!="paused")
                        PluginApprovalStore.WriteAtomic(file,JsonSerializer.SerializeToUtf8Bytes(previous with { Phase="paused",Pid=0,Failures=MaxFailures }));
                    throw new PluginRecoveryException("자동 복원 일시 중지","같은 확장 사용 직후 정상 종료 기록이 3회 연속 누락됐습니다. 승인 기록은 보관 중입니다. 활성화로 다시 시도하거나 비활성화를 선택하세요.");
                }
            }
        }
        return new(file,new(hash,session,Environment.ProcessId,StartTicks,"initializing",failures,DateTimeOffset.UtcNow),detail);
    }

    static bool IsLive(Record record)
    {
        if(record.Phase=="failed" || record.Pid<=0 || record.ProcessStartTicks<=0) return false;
        try
        {
            using var process=Process.GetProcessById(record.Pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==record.ProcessStartTicks;
        }
        catch(ArgumentException) {return false;}
        catch(InvalidOperationException) {return false;}
        catch(System.ComponentModel.Win32Exception)
        {
            // Windows refused the process query. Defer rather than overwrite a possible live owner's marker.
            return true;
        }
    }

    internal void Write(string phase)
    {
        current=current with {Phase=phase,PhaseUtc=DateTimeOffset.UtcNow};
        PluginApprovalStore.WriteAtomic(file,JsonSerializer.SerializeToUtf8Bytes(current));
    }
    internal void Close()=>File.Delete(file);
}
