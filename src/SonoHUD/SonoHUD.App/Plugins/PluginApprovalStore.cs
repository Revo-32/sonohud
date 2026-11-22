using System.IO;
using System.Text.Json;

namespace SonoHUD.App.Plugins;

// One writer per profile, including offscreen and login hosts. The OS releases the lease on process death.
internal sealed class PluginApprovalStore : IDisposable
{
    readonly string directory;
    readonly string path;
    readonly string backup;
    FileStream? lease;
    Dictionary<string,string> grants = new(StringComparer.OrdinalIgnoreCase);
    internal string? Error { get; private set; }
    internal bool CanRepair { get; private set; }
    internal bool HasBackup { get; private set; }
    internal bool Granted(string id, string hash) => grants.TryGetValue(id,out var value) && value == hash;
    internal bool Contains(string id) => grants.ContainsKey(id);

    internal PluginApprovalStore(string directory)
    {
        this.directory = directory;
        path = Path.Combine(directory,"approved-v1.json");
        backup = path + ".bak";
        Reload();
    }

    internal static void CheckFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        PluginPackage.CheckLink(File.GetAttributes(folder));
    }

    static Dictionary<string,string> Read(string file)
    {
        PluginPackage.CheckLink(File.GetAttributes(file));
        using var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (stream.Length > 256 * 1024) throw new InvalidDataException("승인 목록 크기 초과");
        using var doc = JsonDocument.Parse(stream,new JsonDocumentOptions {MaxDepth=3});
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("승인 목록 형식 오류");
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateObject())
        {
            string? hash = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
            if (hash is not {Length:64} || !hash.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(item.Name) ||
                !result.TryAdd(item.Name,hash.ToUpperInvariant()))
                throw new InvalidDataException("승인 항목 형식 오류");
        }
        return result;
    }

    internal void Reload()
    {
        Error = null;
        CanRepair = HasBackup = false;
        try
        {
            CheckFolder(directory);
            if (lease == null)
            {
                string file = Path.Combine(directory,"approval-store.lock");
                CheckExistingFile(file);
                lease = new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            }
            try { grants = Read(path); }
            catch (FileNotFoundException)
            {
                // A missing primary is a new profile only when no previous backup exists.
                if (CheckExistingFile(backup)) throw new InvalidDataException("승인 원본이 없습니다.");
                grants = new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception e) when (PluginHost.Recoverable(e))
        {
            grants = new(StringComparer.OrdinalIgnoreCase);
            CanRepair = lease != null && e is JsonException or InvalidDataException;
            if (CanRepair)
            {
                try { _ = Read(backup); HasBackup = true; }
                catch (Exception badBackup) when (PluginHost.Recoverable(badBackup)) { }
            }
            Error = lease == null ? "확장 설정을 열 수 없습니다. 다른 SonoHUD가 같은 프로필을 사용 중이거나 쓰기 권한이 없습니다."
                : CanRepair ? "확장 승인 목록이 손상됐습니다. 자동 실행을 보류했습니다. 승인 목록 복구에서 복구 방법을 확인하세요."
                : "확장 승인 목록을 읽을 수 없습니다. 파일 접근을 확인하고 새로 고침하세요.";
        }
    }

    // GetAttributes reports access errors; File.Exists would turn them into false.
    internal static bool CheckExistingFile(string file)
    {
        try { PluginPackage.CheckLink(File.GetAttributes(file)); return true; }
        catch (FileNotFoundException) { return false; }
    }

    internal void EnsureWritable()
    {
        if (lease == null || Error != null) throw new InvalidOperationException(Error ?? "확장 설정이 닫혔습니다.");
    }

    internal void Set(string id, string? hash)
    {
        EnsureWritable();
        if (hash == null ? !Contains(id) : Granted(id,hash)) return;
        var next = new Dictionary<string,string>(grants,StringComparer.OrdinalIgnoreCase);
        if (hash == null) next.Remove(id); else next[id] = hash;
        Commit(next,false);
    }

    internal void Repair()
    {
        if (!CanRepair || lease == null) throw new InvalidOperationException("승인 목록 복구가 필요하지 않습니다.");
        // Restoring grants from any historical backup always requires the user's confirmation.
        var next = HasBackup ? Read(backup) : new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        Commit(next,true);
        Reload();
    }

    void Commit(Dictionary<string,string> next, bool repair)
    {
        bool exists = CheckExistingFile(path);
        CheckExistingFile(backup);
        // Refuse to overwrite an externally corrupted/changed primary in a running session.
        if (!repair)
        {
            var original = exists ? Read(path) : new Dictionary<string,string>();
            if (original.Count != grants.Count || original.Any(p=>!Granted(p.Key,p.Value)))
                throw new IOException("승인 목록이 변경됐습니다. 새로 고침 후 확인하세요.");
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next);
        if (bytes.Length > 256 * 1024) throw new InvalidDataException("승인 목록 크기 초과");
        // Back up the intended contents after committing. Never use a previous grant set silently on startup.
        string? damaged = repair && exists ? path + ".damaged-" + Guid.NewGuid().ToString("N") : null;
        if (!repair && File.Exists(backup)) File.Delete(backup);
        WriteAtomic(path,bytes,damaged);
        grants = next;
        try { WriteAtomic(backup,bytes); BackupWarning = null; }
        catch (Exception e) when (PluginHost.Recoverable(e))
        { BackupWarning = "승인 변경은 저장했지만 백업 갱신에 실패했습니다."; }
    }

    internal string? BackupWarning { get; private set; }

    internal static void WriteAtomic(string destination, byte[] bytes, string? previous = null)
    {
        bool exists = CheckExistingFile(destination);
        string tmp = Path.Combine(Path.GetDirectoryName(destination)!, ".plugin-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk:true);
            }
            if (exists) File.Replace(tmp,destination,previous);
            else File.Move(tmp,destination);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public void Dispose() { lease?.Dispose(); lease=null; }
}
