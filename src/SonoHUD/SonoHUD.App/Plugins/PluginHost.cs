using System.IO;
using SonoHUD.PluginContracts;

namespace SonoHUD.App.Plugins;

internal sealed record PluginRow(string SourcePath,PluginManifest? Manifest,string Hash,string State,string Detail,bool Approved=false)
{
    public string DisplayName => Manifest?.Name ?? Path.GetFileName(SourcePath);
    public string Description => $"{DisplayName}   {Manifest?.Version}  ·  {State}";
}

internal sealed class PluginHost : IDisposable
{
    sealed record Running(PluginManifest Manifest,string Hash,PluginLoadContext Context,ISonoHudPlugin Instance,PluginRunGuard Guard);
    readonly Dictionary<string,Running> running=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,(string State,string Detail)> issues=new(StringComparer.OrdinalIgnoreCase);
    readonly PluginApprovalStore approvals;
    readonly Guid session=Guid.NewGuid();
    internal string DirectoryPath {get;}
    internal string SettingsPath {get;}
    internal string StoreNotice=>approvals.Error??approvals.BackupWarning??"";
    internal bool CanManage=>approvals.Error==null;
    internal bool CanRepair=>approvals.CanRepair;
    internal bool HasBackup=>approvals.HasBackup;
    internal List<PluginRow> Rows {get;private set;}=[];
    internal ILyricsProvider? Provider=>running.Values.Select(x=>x.Instance).OfType<ILyricsProvider>().FirstOrDefault();
    internal PluginManifest? LyricsManifest=>running.Values.FirstOrDefault(x=>x.Instance is ILyricsProvider)?.Manifest;
    internal event Action? Changed;

    internal PluginHost(string profile)
    {
        DirectoryPath=Path.Combine(profile,"Plugins");
        SettingsPath=Path.Combine(profile,"PluginSettings");
        approvals=new(SettingsPath);
        Refresh(true);
    }

    internal static bool Recoverable(Exception e)=>e is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    string PluginSettings(string id)
    {
        var path=Path.Combine(SettingsPath,id);
        PluginApprovalStore.CheckFolder(SettingsPath); PluginApprovalStore.CheckFolder(path);
        return path;
    }

    PluginRow Describe(string path,PluginPackage package)
    {
        var manifest=package.Manifest;
        bool granted=approvals.Granted(manifest.Id,package.Hash);
        bool approved=approvals.Contains(manifest.Id);
        string state,detail="";
        if (!manifest.Compatible) state=manifest.MaxApi<1?"업데이트 필요":"호환되지 않음";
        else if (!CanManage) {state="자동 복원 보류";detail=StoreNotice;}
        else if (approved && !granted) state="변경됨 · 다시 승인 필요";
        else if (granted && running.TryGetValue(manifest.Id,out var active) && active.Hash==package.Hash)
        {state="활성화";detail=active.Guard.Detail;}
        else if (issues.TryGetValue(manifest.Id,out var issue)) (state,detail)=issue;
        else state="설치됨 · 비활성화";
        return new(path,manifest,package.Hash,state,detail,approved);
    }

    internal void Refresh(bool restore=false)
    {
        var rows=new List<PluginRow>();
        try
        {
            if(Directory.Exists(DirectoryPath))
            {
                PluginPackage.CheckLink(File.GetAttributes(DirectoryPath));
                foreach(var path in Directory.EnumerateFileSystemEntries(DirectoryPath).Order(StringComparer.OrdinalIgnoreCase))
                {
                    try {rows.Add(Describe(path,PluginPackage.Read(path)));}
                    catch(Exception e) when(Recoverable(e)) {rows.Add(new(path,null,"","로딩 실패",e.GetType().Name));}
                }
            }
        }
        catch(Exception e) when(Recoverable(e)) {rows.Add(new(DirectoryPath,null,"","읽기 실패",e.GetType().Name));}
        var duplicates=rows.Where(r=>r.Manifest!=null).GroupBy(r=>r.Manifest!.Id,StringComparer.OrdinalIgnoreCase)
            .Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<rows.Count;i++)
            if(rows[i].Manifest is {} m && duplicates.Contains(m.Id)) rows[i]=rows[i] with {State="중복 ID",Detail="중복 파일을 정리한 뒤 새로 고침하세요."};
        Rows=rows;
        foreach(var pair in running.ToArray())
            if(!Rows.Any(r=>r.Manifest?.Id==pair.Key && r.Hash==pair.Value.Hash && r.State=="활성화")) Stop(pair.Key);
        if(restore && CanManage)
        {
            foreach(var row in rows)
            {
                var m=row.Manifest;
                if(m is null || !m.Compatible || duplicates.Contains(m.Id) || running.ContainsKey(m.Id) || !approvals.Granted(m.Id,row.Hash)) continue;
                try {Start(row,false);}
                catch(Exception e) when(Recoverable(e)) {RecordFailure(m.Id,e);}
            }
            Refresh();return;
        }
        Changed?.Invoke();
    }
    internal void Rescan()
    {
        bool retryStore=approvals.Error!=null;
        approvals.Reload();
        Refresh(retryStore && CanManage);
    }
    internal void RepairApprovals() {approvals.Repair();Refresh(true);}
    internal PluginPackage Inspect(string file)=>PluginPackage.Read(file);
    internal void Install(string file,string approvedHash)
    {
        approvals.EnsureWritable();
        var p=PluginPackage.Read(file);
        if(p.Hash!=approvedHash || !p.Manifest.Compatible)throw new InvalidDataException("파일 변경 또는 호환성 오류");
        PluginApprovalStore.CheckFolder(DirectoryPath);
        if(Rows.Any(r=>r.Manifest?.Id==p.Manifest.Id)) throw new IOException("같은 ID의 확장을 먼저 제거하세요. 설정은 보존됩니다.");
        string target=Path.Combine(DirectoryPath,p.Manifest.Id);
        string stage=Path.Combine(SettingsPath,"install-"+Guid.NewGuid().ToString("N"));
        PluginApprovalStore.CheckFolder(SettingsPath); PluginApprovalStore.CheckFolder(stage);
        try
        {
            foreach(var part in p.Files)File.WriteAllBytes(Path.Combine(stage,part.Key),part.Value);
            Directory.Move(stage,target);
        }
        finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
        Refresh();
    }
    void Start(PluginRow requested,bool manual)
    {
        var p=PluginPackage.Read(requested.SourcePath);
        if(p.Hash!=requested.Hash || p.Manifest.Id!=requested.Manifest?.Id || !p.Manifest.Compatible)
            throw new InvalidDataException("승인을 기다리는 동안 확장 파일이 변경됐습니다.");
        if(p.Manifest.Lyrics && Provider is not null)throw new InvalidOperationException("활성 가사 확장을 먼저 끄세요.");
        string settings=PluginSettings(p.Manifest.Id);
        var guard=PluginRunGuard.Begin(settings,p.Hash,session,manual);
        var ctx=new PluginLoadContext(p);
        ISonoHudPlugin? instance=null;
        try
        {
            var assembly=ctx.LoadEntry();
            var type=assembly.GetType(p.Manifest.Entry,true)!;
            if(!typeof(ISonoHudPlugin).IsAssignableFrom(type) || typeof(ILyricsProvider).IsAssignableFrom(type)!=p.Manifest.Lyrics)
                throw new InvalidDataException("확장 선언과 구현이 일치하지 않습니다.");
            instance=(ISonoHudPlugin)Activator.CreateInstance(type)!;
            instance.Initialize(new(settings,typeof(PluginHost).Assembly.GetName().Version!));
            guard.Write("running");
            running.Add(p.Manifest.Id,new(p.Manifest,p.Hash,ctx,instance,guard));
            issues.Remove(p.Manifest.Id);
        }
        catch
        {
            try {instance?.Dispose();}
            finally {ctx.Unload();guard.Write("failed");}
            throw;
        }
    }

    void RecordFailure(string id,Exception e)
    {
        issues[id]=e is PluginRecoveryException recovery?(recovery.State,recovery.Message):("로딩 실패","확장 시작 실패 · "+e.GetType().Name);
    }
    internal void Enable(PluginRow row)
    {
        approvals.EnsureWritable();
        Refresh();
        if(row.Manifest is not {} m || !Rows.Any(r=>r.Hash==row.Hash && r.SourcePath==row.SourcePath && r.Manifest?.Compatible==true && r.State!="중복 ID") || running.ContainsKey(m.Id))
            throw new InvalidOperationException("확장 목록을 다시 확인하세요.");
        if(m.Lyrics && Provider is not null)throw new InvalidOperationException("활성 가사 확장을 먼저 끄세요.");
        approvals.Set(m.Id,row.Hash);
        try {Start(row,true);}
        catch(Exception e) when(Recoverable(e))
        {
            RecordFailure(m.Id,e);
            throw new InvalidOperationException(issues[m.Id].Detail,e);
        }
        finally {Refresh();}
    }
    internal void Disable(string id)
    {
        approvals.Set(id,null);
        Stop(id);issues.Remove(id);Refresh();
    }
    void Stop(string id)
    {
        if(!running.Remove(id,out var active))return;
        try
        {
            Changed?.Invoke(); // Cancel outstanding calls before releasing the provider.
        }
        finally
        {
            try
            {
                try {active.Guard.Write("stopping");} catch(Exception e) when(Recoverable(e)) {issues[id]=("종료 경고","확장 종료 기록을 저장하지 못했습니다.");}
                active.Instance.Dispose();
            }
            catch(Exception e) when(Recoverable(e)) {issues[id]=("종료 오류",e.GetType().Name);}
            finally
            {
                active.Context.Unload();
                try {active.Guard.Close();} catch(Exception e) when(Recoverable(e)) {issues[id]=("종료 경고","이전 실행의 잠금 기록이 남았습니다.");}
            }
        }
    }
    internal void Remove(PluginRow row)
    {
        approvals.EnsureWritable();
        var source=Path.GetFullPath(row.SourcePath);
        if(!string.Equals(Path.GetDirectoryName(source),Path.GetFullPath(DirectoryPath),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("확장 폴더 항목을 선택하세요.");
        if(row.Manifest is {} m) Disable(m.Id);
        string removed=Path.Combine(SettingsPath,"removed");
        PluginApprovalStore.CheckFolder(SettingsPath); PluginApprovalStore.CheckFolder(removed);
        string dest=Path.Combine(removed,Guid.NewGuid().ToString("N"));
        if(Directory.Exists(source)) Directory.Move(source,dest); else File.Move(source,dest);
        Refresh();
    }
    internal string SettingsFile(string id)=>Path.Combine(PluginSettings(id),"lyrics-preferences.json");
    internal void OpenFolder()
    {
        PluginApprovalStore.CheckFolder(DirectoryPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DirectoryPath){UseShellExecute=true});
    }
    public void Dispose()
    {
        Changed=null;
        try {foreach(var id in running.Keys.ToArray())Stop(id);}
        finally {approvals.Dispose();}
    }
}
