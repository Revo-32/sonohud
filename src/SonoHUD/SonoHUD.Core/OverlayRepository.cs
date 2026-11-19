using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SonoHUD.Core;

public sealed record OverlayLoadResult(OverlayDocument Document, string? Warning, bool CanSave, string? ArchivePath = null);

public sealed class OverlayRepository(string directory)
{
    const long MaximumSettingsBytes = 10 * 1024 * 1024;
    string? blockedReason;
    string? lastArchivePath;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(DirectoryPath, "settings.v2.json");
    public string BackupPath => Path.Combine(DirectoryPath, "settings.v2.backup.json");

    public OverlayLoadResult Load()
    {
        blockedReason = null;
        lastArchivePath = null;
        try
        {
            var primary = ReadOptional(FilePath);
            if (primary is not null)
            {
                try { return new(OverlayJson.Parse(Decode(primary)), null, true); }
                catch (NotSupportedException error)
                {
                    lastArchivePath = Archive("unsupported-v2", CurrentFiles(primary, ReadOptional(BackupPath)), false);
                    return Blocked(error.Message + " 설정을 저장하지 않습니다. 지원하는 버전으로 열어 주세요.");
                }
                catch (Exception error) when (IsContentError(error)) { }
            }
            var backup = ReadOptional(BackupPath);
            if (primary is null && backup is null) return MigrateLegacyOrDefault();

            // Keep the unreadable bytes before returning a usable recovered/default document.
            // This snapshot never replaces the rolling backup or an existing archive.
            lastArchivePath = Archive("recovery-v2", CurrentFiles(primary, backup), false);
            if (backup is not null)
            {
                try
                {
                    var recovered = OverlayJson.Parse(Decode(backup));
                    return new(recovered, "오버레이 설정을 최근 정상 백업에서 복구했습니다. 이전 파일은 별도 보관했습니다.", true, lastArchivePath);
                }
                catch (NotSupportedException error) { return Blocked(error.Message + " 설정을 저장하지 않습니다."); }
                catch (Exception error) when (IsContentError(error)) { }
            }
            return new(OfflineDefault(), "오버레이 설정을 읽지 못해 기본 Music Card로 시작했습니다. 손상된 원본과 백업은 별도 보관했습니다.", true, lastArchivePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Blocked("설정을 읽거나 안전하게 백업하지 못했습니다. 원본을 변경하지 않고 임시 기본 프리셋으로 시작합니다. 파일 접근 상태를 확인한 뒤 다시 시작하세요. " + error.Message);
        }
    }

    // Without readable settings there is no verified permission to send track metadata.
    static OverlayDocument OfflineDefault() => new() { Settings = new() { Lyrics = new() { OnlineEnabled = false } } };

    OverlayLoadResult Blocked(string warning)
    {
        blockedReason = warning;
        return new(OfflineDefault(), warning, false, lastArchivePath);
    }

    public void Save(OverlayDocument document)
    {
        OverlayValidation.Check(document);
        if (blockedReason is not null) throw new IOException("원본 설정 보호를 위해 저장을 보류합니다. 설정을 다시 읽은 뒤 저장하세요.");
        Directory.CreateDirectory(DirectoryPath);
        var primary = ReadOptional(FilePath);
        bool validPrimary = false;
        if (primary is not null)
        {
            try { OverlayJson.Parse(Decode(primary)); validPrimary = true; }
            catch (Exception error) when (IsContentError(error))
            {
                lastArchivePath = Archive("replaced-invalid-v2", CurrentFiles(primary, ReadOptional(BackupPath)), false);
            }
            // Unsupported versions and temporary I/O errors deliberately propagate.
        }
        string temporary = Path.Combine(DirectoryPath, $"settings.v2.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteNew(temporary, Encoding.UTF8.GetBytes(OverlayJson.Serialize(document)));
            if (primary is null) File.Move(temporary, FilePath);
            else File.Replace(temporary, FilePath, validPrimary ? BackupPath : null, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    OverlayLoadResult MigrateLegacyOrDefault()
    {
        var primary = ReadOptional(Path.Combine(DirectoryPath, "scenes.json"));
        var backup = ReadOptional(Path.Combine(DirectoryPath, "scenes.backup.json"));
        if (primary is null && backup is null)
        {
            var firstRun = OfflineDefault();
            firstRun.Settings.Spectrum = new() { Enabled = true, Home = true };
            return new(firstRun, null, true);
        }

        SceneDocument? legacy = null;
        string sourceFile = "";
        foreach (var candidate in new[] { (Name: "scenes.json", Data: primary), (Name: "scenes.backup.json", Data: backup) })
        {
            if (candidate.Data is null) continue;
            try { legacy = SceneJson.Parse(Decode(candidate.Data)); sourceFile = candidate.Name; break; }
            catch (Exception error) when (IsContentError(error)) { }
        }

        Scene? selected = legacy?.Scenes.FirstOrDefault(scene => scene.Active && scene.Visible)
            ?? legacy?.Scenes.FirstOrDefault(scene => scene.Active)
            ?? legacy?.Scenes.FirstOrDefault();
        var document = ConvertLegacy(legacy, selected);
        var files = new Dictionary<string, byte[]>();
        if (primary is not null) files["scenes.json"] = primary;
        if (backup is not null) files["scenes.backup.json"] = backup;
        lastArchivePath = Archive("legacy-v1", files, true, new
        {
            selectionRule = "first-active-visible-else-first-active-else-first-in-file-v1",
            sourceFile,
            selectedSceneId = selected?.Id,
            selectedSceneName = selected?.Name,
            originalSceneCount = legacy?.Scenes.Count,
            targetPreset = PresetKind.MusicCard.ToString(),
            document.Overlay.MonitorId,
            document.Overlay.X,
            document.Overlay.Y,
            document.Overlay.Scale,
            sourceReadable = legacy is not null
        });
        // The archive is fully copied, hashed and sealed with its manifest before any v2 write.
        if (ReadOptional(FilePath) is not null || ReadOptional(BackupPath) is not null)
            throw new IOException("변환 중 새 설정 파일이 생겼습니다. 원본을 보호하기 위해 다시 시작해야 합니다.");
        Save(document);
        string warning = selected is null
            ? "이전 설정과 이미지를 보존하고 기본 Music Card로 전환했습니다. 원본은 안전하게 백업했습니다."
            : $"이전 오버레이 {legacy!.Scenes.Count}개 중 ‘{selected.Name}’의 위치를 가져와 Music Card를 적용했습니다. 모든 이전 배치와 이미지는 원본 및 백업에 보존했습니다.";
        return new(document, warning, true, lastArchivePath);
    }

    static OverlayDocument ConvertLegacy(SceneDocument? legacy, Scene? selected)
    {
        var document = legacy is null ? OfflineDefault() : new OverlayDocument { Settings = legacy.Settings with { } };
        if (document.Settings.DefaultMonitorId is null || document.Settings.DefaultMonitorId.Length > 256) document.Settings.DefaultMonitorId = "";
        if (selected is null) return document;
        var frames = SceneMath.Flatten(selected);
        bool Shown(ElementKind kind) => frames.Any(frame => frame.Visible && frame.Element.Type == kind);
        document.Overlay = new()
        {
            Id = selected.Id,
            Enabled = selected.Active && selected.Visible,
            Preset = PresetKind.MusicCard,
            MonitorId = selected.MonitorId is { Length: <= 256 } ? selected.MonitorId : "",
            X = selected.X, Y = selected.Y,
            Scale = Math.Clamp(selected.Width / OverlayPresets.Dimensions(PresetKind.MusicCard).Width, .75, 1.5),
            ClickThrough = selected.ClickThrough,
            Animation = selected.AnimationProfile,
            FontFamily = selected.Elements.FirstOrDefault(element => element.Type == ElementKind.TrackTitle)?.Style.FontFamily ?? DesignTokens.FontFamily,
            ShowArtwork = Shown(ElementKind.AlbumArtwork), ShowArtist = Shown(ElementKind.ArtistName),
            ShowProgress = Shown(ElementKind.ProgressBar), ShowControls = Shown(ElementKind.PlaybackControls)
        };
        OverlayValidation.Check(document);
        return document;
    }

    static Dictionary<string, byte[]> CurrentFiles(byte[]? primary, byte[]? backup)
    {
        var files = new Dictionary<string, byte[]>();
        if (primary is not null) files["settings.v2.json"] = primary;
        if (backup is not null) files["settings.v2.backup.json"] = backup;
        return files;
    }

    sealed record ArchivedFile(string Path, long Length, string Sha256);
    string Archive(string kind, IReadOnlyDictionary<string, byte[]> files, bool includeLegacyAssets, object? migration = null)
    {
        string root = Path.Combine(DirectoryPath, "backups", "M4.3");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("백업 폴더의 링크는 사용할 수 없습니다.");
        string destination = Path.Combine(root, $"{kind}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        var manifest = new List<ArchivedFile>();
        foreach (var file in files)
        {
            string target = Path.Combine(destination, file.Key);
            WriteNew(target, file.Value);
            byte[] expected = SHA256.HashData(file.Value);
            using var copied = File.OpenRead(target);
            if (!SHA256.HashData(copied).SequenceEqual(expected)) throw new IOException("설정 백업 검증에 실패했습니다.");
            manifest.Add(new(file.Key, file.Value.LongLength, Convert.ToHexString(expected)));
        }
        if (includeLegacyAssets)
        {
            string assets = Path.Combine(DirectoryPath, "assets");
            FileAttributes attributes;
            try { attributes = File.GetAttributes(assets); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { attributes = 0; }
            if (attributes != 0)
            {
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("이전 이미지 폴더를 안전하게 백업할 수 없습니다.");
                CopyAssets(assets, Path.Combine(destination, "assets"), "assets");
            }
        }
        WriteNew(Path.Combine(destination, "manifest.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            format = "SonoHUD.ImmutableBackup", schemaVersion = 1, createdUtc = DateTimeOffset.UtcNow,
            kind, files = manifest, migration
        }, OverlayJson.Options)));
        return destination;

        void CopyAssets(string source, string targetDirectory, string relativeDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (var item in new DirectoryInfo(source).EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("이전 이미지의 링크는 따라가지 않습니다. 원본을 보존했습니다: " + item.Name);
                string target = Path.Combine(targetDirectory, item.Name), relative = relativeDirectory + "/" + item.Name;
                if ((item.Attributes & FileAttributes.Directory) != 0) { CopyAssets(item.FullName, target, relative); continue; }
                byte[] expected;
                long length;
                using (var original = new FileStream(item.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    length = original.Length;
                    using (var copy = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                    { original.CopyTo(copy); copy.Flush(true); }
                    original.Position = 0; expected = SHA256.HashData(original);
                }
                using var archived = File.OpenRead(target);
                if (archived.Length != length || !SHA256.HashData(archived).SequenceEqual(expected)) throw new IOException("이미지 백업 검증에 실패했습니다: " + item.Name);
                manifest.Add(new(relative, length, Convert.ToHexString(expected)));
            }
        }
    }

    static bool IsContentError(Exception error) => error is JsonException or InvalidDataException or ArgumentException;
    static string Decode(byte[] data) => Encoding.UTF8.GetString(data.AsSpan(data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0));
    static byte[]? ReadOptional(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumSettingsBytes) throw new IOException("설정 파일이 너무 커서 자동 처리를 보류합니다.");
            using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }
    static void WriteNew(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(data); stream.Flush(true);
    }
}
