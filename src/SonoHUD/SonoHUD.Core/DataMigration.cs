using System.Security.Cryptography;

namespace SonoHUD.Core;

// Copy only application-owned settings. The legacy directory is never modified.
public static class DataMigration
{
    public static string? ImportLegacy(string legacy, string destination)
    {
        if (File.Exists(Path.Combine(destination, "scenes.json")) || File.Exists(Path.Combine(destination, "scenes.backup.json")) ||
            (!File.Exists(Path.Combine(legacy, "scenes.json")) && !File.Exists(Path.Combine(legacy, "scenes.backup.json")))) return null;
        var source = new SceneRepository(legacy).Load();
        if (!source.HasValidData) return "이전 RevoMusic 설정을 읽지 못했습니다. 원본을 보존했습니다.";
        Directory.CreateDirectory(destination);
        var assets = Path.Combine(legacy, "assets");
        if (Directory.Exists(assets))
        {
            Directory.CreateDirectory(Path.Combine(destination, "assets"));
            foreach (var file in Directory.EnumerateFiles(assets))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 16 * 1024 * 1024) continue;
                string target = Path.Combine(destination, "assets", Path.GetFileName(file));
                if (File.Exists(target))
                {
                    var existing = new FileInfo(target);
                    if ((existing.Attributes & FileAttributes.ReparsePoint) != 0 || existing.Length != info.Length)
                        throw new IOException("이전 설정의 이미지와 같은 이름의 다른 파일이 있습니다. 두 원본을 보존했습니다: " + info.Name);
                    using var original = File.OpenRead(file); using var copied = File.OpenRead(target);
                    if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(copied)))
                        throw new IOException("이전 설정의 이미지와 같은 이름의 다른 파일이 있습니다. 두 원본을 보존했습니다: " + info.Name);
                    continue; // A previous attempt may have copied assets before saving the document.
                }
                File.Copy(file, target, false);
            }
        }
        new SceneRepository(destination).Save(source.Document);
        return "이전 RevoMusic 설정을 SonoHUD로 가져왔습니다. 이전 폴더와 원본은 그대로 보존했습니다.";
    }
}
