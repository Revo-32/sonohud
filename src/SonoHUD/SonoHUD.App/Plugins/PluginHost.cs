using System.IO;
using System.Text.Json;
using SonoHUD.PluginContracts;

namespace SonoHUD.App.Plugins;

internal sealed record PluginRow(string SourcePath, PluginManifest? Manifest, string Hash, string State, string Detail)
{
    public string DisplayName => Manifest?.Name ?? Path.GetFileName(SourcePath);
    public string Description => $"{DisplayName}   {Manifest?.Version}  ·  {State}";
}

internal sealed class PluginHost : IDisposable
{
    sealed record Running(PluginManifest Manifest,string Hash,PluginLoadContext Context,ISonoHudPlugin Instance,string Marker);
    readonly Dictionary<string,Running> running = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> errors = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> approvals;
    readonly string statePath;
    internal string DirectoryPath {get;}
    internal string SettingsPath {get;}
    internal List<PluginRow> Rows {get;private set;}=[];
    internal ILyricsProvider? Provider=>running.Values.Select(x=>x.Instance).OfType<ILyricsProvider>().FirstOrDefault();
    internal PluginManifest? LyricsManifest=>running.Values.FirstOrDefault(x=>x.Instance is ILyricsProvider)?.Manifest;
    internal event Action? Changed;

    internal PluginHost(string profile)
    {
        DirectoryPath=Path.Combine(profile,"Plugins");
        SettingsPath=Path.Combine(profile,"PluginSettings");
        statePath=Path.Combine(SettingsPath,"approved-v1.json");
        approvals=new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if(Directory.Exists(SettingsPath)) PluginPackage.CheckLink(File.GetAttributes(SettingsPath));
            if(File.Exists(statePath))
            {
                PluginPackage.CheckLink(File.GetAttributes(statePath));
                if(new FileInfo(statePath).Length>256*1024) throw new InvalidDataException("확장 승인 목록 크기 초과");
                var stored=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(statePath));
                if(stored is not null) foreach(var item in stored) approvals[item.Key]=item.Value;
            }
        }
        catch(Exception e) when (Recoverable(e)) { approvals.Clear(); }
        Refresh(true);
    }
    internal static bool Recoverable(Exception e)=>e is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    string PluginSettings(string id)
    {
        var path=Path.Combine(SettingsPath,id);
        EnsureFolder(SettingsPath); EnsureFolder(path);
        return path;
    }
    static void EnsureFolder(string path)
    {
        Directory.CreateDirectory(path);
        PluginPackage.CheckLink(File.GetAttributes(path));
    }
    void SaveApprovals()
    {
        EnsureFolder(SettingsPath);
        string temp=Path.Combine(SettingsPath,$".approval-{Guid.NewGuid():N}.tmp");
        try {File.WriteAllText(temp,JsonSerializer.Serialize(approvals)); File.Move(temp,statePath,true);}
        finally {File.Delete(temp);}
    }
    internal void Refresh(bool restore=false)
    {
        var rows = new List<PluginRow>();
        try
        {
            if(Directory.Exists(DirectoryPath))
            {
                PluginPackage.CheckLink(File.GetAttributes(DirectoryPath));
                foreach(var path in Directory.EnumerateFileSystemEntries(DirectoryPath).Order(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var p=PluginPackage.Read(path);
                        var m=p.Manifest;
                        bool approved=approvals.TryGetValue(m.Id,out var hash) && hash==p.Hash;
                        string state=!m.Compatible?(m.MaxApi<1?"업데이트 필요":"호환되지 않음"):
                            errors.ContainsKey(m.Id)?"로딩 실패":running.TryGetValue(m.Id,out var r) && r.Hash==p.Hash?"활성화":
                            approvals.ContainsKey(m.Id)&&!approved?"변경됨 · 다시 승인 필요":"설치됨 · 비활성화";
                        rows.Add(new(path,m,p.Hash,state,errors.GetValueOrDefault(m.Id)??""));
                    }
                    catch(Exception e) when(Recoverable(e)) {rows.Add(new(path,null,"","로딩 실패",e.GetType().Name));}
                }
            }
        }
        catch(Exception e) when(Recoverable(e)) {rows.Add(new(DirectoryPath,null,"","읽기 실패",e.GetType().Name));}
        var duplicates=rows.Where(r=>r.Manifest!=null).GroupBy(r=>r.Manifest!.Id,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<rows.Count;i++)
            if(rows[i].Manifest is {} m && duplicates.Contains(m.Id)) rows[i]=rows[i] with{State="중복 ID",Detail="중복 파일을 정리한 뒤 새로 고침하세요."};
        Rows=rows;
        foreach(var pair in running.ToArray())
            if(!Rows.Any(r=>r.Manifest?.Id==pair.Key && r.Hash==pair.Value.Hash && r.State=="활성화")) Stop(pair.Key);
        if(restore)
        {
            foreach(var row in rows.ToArray())
            {
                var m=row.Manifest;
                if(m is null || !m.Compatible || duplicates.Contains(m.Id) || !approvals.TryGetValue(m.Id,out var hash) || hash!=row.Hash)continue;
                try
                {
                    string marker=Path.Combine(PluginSettings(m.Id),"running.lock");
                    if(File.Exists(marker))
                    {
                        approvals.Remove(m.Id); SaveApprovals();
                        errors[m.Id]="이전 실행이 정상 종료되지 않아 자동 활성화를 중지했습니다.";
                        continue;
                    }
                    Start(row);
                }
                catch(Exception e) when(Recoverable(e)) {errors[m.Id]="확장 시작 실패 · "+e.GetType().Name;}
            }
            Refresh();return;
        }
        Changed?.Invoke();
    }
    internal PluginPackage Inspect(string file)=>PluginPackage.Read(file);
    internal void Install(string file,string approvedHash)
    {
        var p=PluginPackage.Read(file);
        if (p.Hash!=approvedHash || !p.Manifest.Compatible)throw new InvalidDataException("파일 변경 또는 호환성 오류");
        EnsureFolder(DirectoryPath);
        if(Rows.Any(r=>r.Manifest?.Id==p.Manifest.Id)) throw new IOException("같은 ID의 확장을 먼저 제거하세요. 설정은 보존됩니다.");
        string target=Path.Combine(DirectoryPath,p.Manifest.Id);
        string stage=Path.Combine(SettingsPath,"install-"+Guid.NewGuid().ToString("N"));
        EnsureFolder(SettingsPath); EnsureFolder(stage);
        try
        {
            foreach(var part in p.Files)File.WriteAllBytes(Path.Combine(stage,part.Key),part.Value);
            Directory.Move(stage,target);
        }
        finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
        Refresh();
    }
    void Start(PluginRow requested)
    {
        var p=PluginPackage.Read(requested.SourcePath);
        if(p.Hash!=requested.Hash || p.Manifest.Id!=requested.Manifest?.Id || !p.Manifest.Compatible)
            throw new InvalidDataException("승인을 기다리는 동안 확장 파일이 변경됐습니다.");
        if(p.Manifest.Lyrics && Provider is not null)throw new InvalidOperationException("활성 가사 확장을 먼저 끄세요.");
        string settings=PluginSettings(p.Manifest.Id);
        string marker=Path.Combine(settings,"running.lock");
        File.WriteAllText(marker,p.Hash);
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
            running.Add(p.Manifest.Id,new(p.Manifest,p.Hash,ctx,instance,marker));
            errors.Remove(p.Manifest.Id);
        }
        catch
        {
            try {instance?.Dispose();} finally {ctx.Unload(); File.Delete(marker);}
            throw;
        }
    }
    internal void Enable(PluginRow row)
    {
        Refresh();
        if(row.Manifest is not {} m || !Rows.Any(r=>r.Hash==row.Hash && r.SourcePath==row.SourcePath && r.Manifest?.Compatible==true && r.State!="중복 ID") || running.ContainsKey(m.Id))
            throw new InvalidOperationException("확장 목록을 다시 확인하세요.");
        approvals.Remove(m.Id); SaveApprovals();
        try {Start(row); approvals[m.Id]=row.Hash; SaveApprovals();}
        catch(Exception e) when(Recoverable(e))
        {
            approvals.Remove(m.Id); Stop(m.Id);
            errors[m.Id]="활성화 실패 · "+e.GetType().Name;
            throw new InvalidOperationException(errors[m.Id],e);
        }
        finally {Refresh();}
    }
    internal void Disable(string id)
    {
        approvals.Remove(id); SaveApprovals();
        Stop(id); errors.Remove(id); Refresh();
    }
    void Stop(string id)
    {
        if(!running.Remove(id,out var active))return;
        Changed?.Invoke(); // Cancel outstanding requests before releasing the provider.
        try {active.Instance.Dispose();} catch(Exception e) when(Recoverable(e)){errors[id]="종료 오류 · "+e.GetType().Name;}
        finally
        {
            active.Context.Unload();
            try{File.Delete(active.Marker);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    internal void Remove(PluginRow row)
    {
        var source=Path.GetFullPath(row.SourcePath);
        if(!string.Equals(Path.GetDirectoryName(source),Path.GetFullPath(DirectoryPath),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("확장 폴더 항목을 선택하세요.");
        if(row.Manifest is {} m) Disable(m.Id);
        string removed=Path.Combine(SettingsPath,"removed"); EnsureFolder(SettingsPath);EnsureFolder(removed);
        string dest=Path.Combine(removed,Guid.NewGuid().ToString("N"));
        if(Directory.Exists(source)) Directory.Move(source,dest); else File.Move(source,dest);
        Refresh();
    }
    internal string SettingsFile(string id)=>Path.Combine(PluginSettings(id),"lyrics-preferences.json");
    internal void OpenFolder()
    {
        EnsureFolder(DirectoryPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DirectoryPath){UseShellExecute=true});
    }
    public void Dispose(){Changed=null;foreach(var id in running.Keys.ToArray())Stop(id);}
}
