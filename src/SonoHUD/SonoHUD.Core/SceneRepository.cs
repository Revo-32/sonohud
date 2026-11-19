using System.Text;

namespace SonoHUD.Core;

public sealed record LoadResult(SceneDocument Document, string? Warning, bool Recovered, bool HasValidData = true);
public sealed class SceneRepository(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(DirectoryPath, "scenes.json");
    public string BackupPath => Path.Combine(DirectoryPath, "scenes.backup.json");
    static SceneDocument Read(string path) { var info = new FileInfo(path); if (info.Length > 10 * 1024 * 1024) throw new InvalidDataException("설정 파일이 너무 큽니다."); return SceneJson.Parse(File.ReadAllText(path, Encoding.UTF8)); }
    public LoadResult Load()
    {
        bool missingPrimary = false;
        try { return new(Read(FilePath), null, false); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { missingPrimary = true; }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or ArgumentException) { }
        // An unreadable file can still be valid. Let sharing/access errors reach the caller,
        // so a temporary I/O failure cannot start an empty, subsequently autosaved document.
        try { return new(Read(BackupPath), "설정 파일을 읽지 못해 최근 정상 백업을 복구했습니다. 원본은 저장 시 별도로 보존됩니다.", true); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            if (missingPrimary) return new(new(), null, false, false);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or ArgumentException) { }
        return new(new(), "설정 파일을 읽지 못했습니다. 새 문서로 시작하며 기존 파일은 다음 저장 시 별도로 보존됩니다.", true, false);
    }
    public void Save(SceneDocument document)
    {
        SceneValidation.Check(document); Directory.CreateDirectory(DirectoryPath); string tmp = Path.Combine(DirectoryPath, $"scenes.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { var data = Encoding.UTF8.GetBytes(SceneJson.Serialize(document)); stream.Write(data); stream.Flush(true); }
            if (File.Exists(FilePath))
            {
                bool valid; try { Read(FilePath); valid = true; } catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or ArgumentException) { valid = false; }
                if (valid) File.Replace(tmp, FilePath, BackupPath, true);
                else { File.Move(FilePath, Path.Combine(DirectoryPath, $"scenes.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json")); File.Move(tmp, FilePath); }
            }
            else File.Move(tmp, FilePath);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
