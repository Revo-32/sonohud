using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SonoHUD.Core;

internal static class OverlayTests
{
    static void Assert(bool value, string message = "overlay assertion failed") { if (!value) throw new Exception(message); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or JsonException or IOException or NotSupportedException or UnauthorizedAccessException) { return; }
        throw new Exception("invalid operation accepted");
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void AssertArchive(string archive)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(archive, "manifest.json")));
        Assert(manifest.RootElement.GetProperty("format").GetString() == "SonoHUD.ImmutableBackup");
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string path = Path.Combine(archive, entry.GetProperty("path").GetString()!);
            Assert(new FileInfo(path).Length == entry.GetProperty("length").GetInt64(), "archive length");
            Assert(Hash(path) == entry.GetProperty("sha256").GetString(), "archive hash");
        }
    }
    static Scene LegacyScene(string name, bool active, bool visible)
    {
        var scene = SceneFactory.Music("legacy-monitor", new()); scene.Name = name; scene.Active = active; scene.Visible = visible; return scene;
    }

    public static void Run(Action<string, Action> check, string root)
    {
        string TestDirectory(string name) => Path.Combine(root, "overlay", name);
        check("overlay schema 2 roundtrip rejects legacy future and missing identity header", () =>
        {
            var document = new OverlayDocument(); string json = OverlayJson.Serialize(document);
            Assert(OverlayJson.Serialize(OverlayJson.Clone(document)) == json);
            Reject(() => OverlayJson.Parse(SceneJson.Serialize(new())));
            Reject(() => OverlayJson.Parse(json.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3")));
            Reject(() => OverlayJson.Parse("{}"));
            Reject(() => OverlayJson.Parse("{\"format\":\"SonoHUD.Overlay\",\"schemaVersion\":2,\"overlay\":null}"));
            var defaults = OverlayJson.Parse("{\"format\":\"SonoHUD.Overlay\",\"schemaVersion\":2,\"overlay\":{}}");
            Assert(defaults.Overlay.Preset == PresetKind.MusicCard && !defaults.Overlay.Enabled && defaults.Settings.HotkeyFunction == 9);
        });
        check("overlay input boundaries are validated without changing source", () =>
        {
            var original = new OverlayDocument(); string before = OverlayJson.Serialize(original);
            Action<OverlayDocument>[] invalid =
            [
                d => d.Overlay.Id = Guid.Empty, d => d.Overlay.Preset = (PresetKind)99, d => d.Overlay.Theme = (OverlayTheme)99,
                d => d.Overlay.Animation = (AnimationProfile)99, d => d.Overlay.Scale = .749, d => d.Overlay.Scale = 1.501,
                d => d.Overlay.Scale = double.NaN, d => d.Overlay.BackgroundOpacity = -.01, d => d.Overlay.BackgroundOpacity = 1.01,
                d => d.Overlay.X = double.PositiveInfinity, d => d.Overlay.Y = -100001, d => d.Overlay.AccentColor = "not-a-color",
                d => d.Overlay.FontFamily = "", d => d.Overlay.MonitorId = null!, d => d.Settings = null!, d => d.Overlay = null!,
                d => d.Settings.HotkeyFunction = 7, d => d.Settings.DefaultAnimation = (AnimationProfile)99,
                d => d.Overlay.TextScale = .899, d => d.Overlay.TextScale = 1.151, d => d.Overlay.TextScale = double.NaN,
                d => d.Overlay.TitleWeight = 350, d => d.Overlay.TitleWeight = 800,
                d => d.Settings.MediaSourceAppId = null!, d => d.Settings.MediaSourceAppId = new string('a', 513)
            ];
            foreach (var mutate in invalid) { var copy = OverlayJson.Clone(original); mutate(copy); Reject(() => OverlayValidation.Check(copy)); }
            Assert(OverlayJson.Serialize(original) == before);
        });
        check("all preset theme option size motion combinations stay within bounds without content overlap", () =>
        {
            int combinations = 0;
            foreach (var preset in Enum.GetValues<PresetKind>())
            foreach (var theme in Enum.GetValues<OverlayTheme>())
            foreach (var scale in new[] { .75, 1, 1.5 })
            foreach (var motion in Enum.GetValues<AnimationProfile>())
            for (int options = 0; options < 16; options++)
            {
                var config = new OverlayConfig { Preset = preset, Theme = theme, Scale = scale, Animation = motion, ShowArtwork = (options & 1) != 0, ShowArtist = (options & 2) != 0, ShowProgress = (options & 4) != 0, ShowControls = (options & 8) != 0 };
                var scene = OverlayPresets.Create(config); var dimensions = OverlayPresets.Dimensions(preset);
                Assert(scene.Width == dimensions.Width * scale && scene.Height == dimensions.Height * scale);
                var frames = SceneMath.Flatten(scene).Where(frame => frame.Visible && frame.Element.Type is not ElementKind.Group and not ElementKind.BackgroundPanel).ToArray();
                foreach (var frame in frames)
                {
                    var b = frame.Bounds;
                    Assert(b.X >= -.01 && b.Y >= -.01 && b.X + b.Width <= scene.Width + .01 && b.Y + b.Height <= scene.Height + .01, $"out of bounds: {preset}/{frame.Element.Name}");
                }
                for (int i = 0; i < frames.Length; i++)
                for (int j = i + 1; j < frames.Length; j++)
                    Assert(!DesktopLayout.Intersects(frames[i].Bounds, frames[j].Bounds), $"content overlap: {preset}/{frames[i].Element.Name}/{frames[j].Element.Name}");
                Assert(frames.Any(f => f.Element.Type == ElementKind.AlbumArtwork) == config.ShowArtwork);
                Assert(frames.Any(f => f.Element.Type == ElementKind.ArtistName) == config.ShowArtist);
                Assert(frames.Any(f => f.Element.Type == ElementKind.ProgressBar) == config.ShowProgress);
                Assert(frames.All(f => f.Element.Type is not (ElementKind.PlaybackControls or ElementKind.PlaybackSource or ElementKind.PlaybackStatus or ElementKind.AlbumTitle)), "overlay contains album/controls/source/status");
                Assert(frames.Where(f => f.Element.Type == ElementKind.TrackTitle).All(f => f.Element.Marquee) && frames.Where(f => f.Element.Type != ElementKind.TrackTitle).All(f => !f.Element.Marquee), "marquee scope differs from title-only contract");
                Assert(frames.Count(f => f.Element.Type == ElementKind.PlaybackTime) == 1, "missing or duplicated live playback time");
                Assert(frames.Count(f => f.Element.Type == ElementKind.Spectrum) == 1, "missing or duplicated optional frequency spectrum slot");
                if (config.ShowProgress)
                {
                    var progress = frames.Single(f => f.Element.Type == ElementKind.ProgressBar).Bounds;
                    Assert(scene.Height - progress.Y - progress.Height >= 8 * scale - .01, "progress bottom padding disappeared after scaling");
                }
                combinations++;
            }
            Assert(combinations == Enum.GetValues<PresetKind>().Length * 384);
        });
        check("preset projection is single scene with stable role IDs and independent mutable output", () =>
        {
            var document = new OverlayDocument(); document.Overlay.Enabled = true; document.Overlay.MonitorId = "left"; document.Overlay.X = -80;
            string before = OverlayJson.Serialize(document); Guid title = OverlayPresets.ElementId(document.Overlay.Id, "title");
            foreach (var preset in Enum.GetValues<PresetKind>())
            {
                var copy = OverlayJson.Clone(document); copy.Overlay.Preset = preset;
                var projected = OverlayPresets.Render(copy); var scene = projected.Scenes.Single();
                Assert(projected.SchemaVersion == 1 && scene.Id == document.Overlay.Id && scene.Visible && scene.X == -80 && scene.MonitorId == "left");
                Assert(scene.Elements.Any(e => e.Id == title && e.Type == ElementKind.TrackTitle));
                Assert(scene.Elements.Select(e => e.Id).Distinct().Count() == scene.Elements.Count);
                Assert(scene.UserSettings["preset"] == preset.ToString());
                Assert(!scene.Elements.Single(e => e.Type == ElementKind.Group).Locked);
                projected.Settings.HotkeyFunction = 12; scene.Elements.Single(e => e.Id == title).Style.FontFamily = "changed";
            }
            Assert(OverlayJson.Serialize(document) == before);
            var configs = Enum.GetValues<PresetKind>().Select(preset => OverlayPresets.Dimensions(preset)).Distinct().Count();
            Assert(configs == Enum.GetValues<PresetKind>().Length);
        });
        check("background transparency does not fade lyrics and online source is labeled", () =>
        {
            var config = new OverlayConfig { Preset = PresetKind.LyricsFocus, BackgroundOpacity = 0, Animation = AnimationProfile.Cinematic };
            var scene = OverlayPresets.Create(config); var frames = SceneMath.Flatten(scene);
            Assert(scene.Elements.Single(e => e.Id == OverlayPresets.ElementId(config.Id, "background")).Style.Background.StartsWith("#00", StringComparison.Ordinal));
            Assert(frames.Where(f => f.Element.Type is ElementKind.TrackTitle or ElementKind.LyricsText).All(f => f.Opacity == 1));
            Assert(scene.Elements.Single(e => e.Id == OverlayPresets.ElementId(config.Id, "sample-label")).Style.Text.Contains("가사"));
            Assert(scene.Elements.Single(e => e.Type == ElementKind.LyricsText).Style.Text == "오늘의 리듬을 따라\n나만의 공간에 머물러요");
        });
        check("light theme darkens accent without changing persisted selection and dark theme retains it", () =>
        {
            Assert(new OverlayConfig().AccentColor == "#FFE4483D");
            foreach (string selected in new[] { "#FF87DFC4", "#80FF80C0", "#FEDCBA" })
            {
                var config = new OverlayConfig { AccentColor = selected };
                var dark = OverlayPresets.Create(config);
                Assert(dark.Elements.Single(e => e.Type == ElementKind.ProgressBar).Style.Foreground == selected);
                config.Theme = OverlayTheme.Light;
                var light = OverlayPresets.Create(config);
                string shaded = light.Elements.Single(e => e.Type == ElementKind.ProgressBar).Style.Foreground;
                Assert(config.AccentColor == selected && shaded.Length == 9);
                Assert(shaded[1..3] == (selected.Length == 9 ? selected[1..3] : "FF"));
                string rgb = selected[^6..];
                for (int channel = 0; channel < 3; channel++)
                    Assert(Math.Abs(Convert.ToInt32(shaded.Substring(3 + channel * 2, 2), 16) - Convert.ToInt32(rgb.Substring(channel * 2, 2), 16) * .55) <= .5);
            }
        });
        check("fresh overlay repository saves only schema 2 and restores every setting", () =>
        {
            string path = TestDirectory("fresh"); var repo = new OverlayRepository(path); var loaded = repo.Load();
            Assert(loaded.CanSave && loaded.Warning is null && loaded.ArchivePath is null && !Directory.Exists(path));
            var document = loaded.Document; document.Overlay.Preset = PresetKind.Cinematic; document.Overlay.Theme = OverlayTheme.Light;
            document.Overlay.Enabled = true; document.Overlay.Scale = 1.5; document.Overlay.BackgroundOpacity = .42; document.Overlay.X = -2460; document.Overlay.Y = -1400;
            document.Overlay.AccentColor = "#FF123456"; document.Overlay.FontFamily = "Malgun Gothic"; document.Overlay.Animation = AnimationProfile.Cinematic;
            document.Overlay.ShowArtwork = false; document.Overlay.ShowArtist = false; document.Overlay.ShowProgress = false; document.Overlay.ShowControls = false;
            document.Settings.HotkeyFunction = 12; repo.Save(document);
            Assert(OverlayJson.Serialize(repo.Load().Document) == OverlayJson.Serialize(document));
            Assert(!File.Exists(Path.Combine(path, "scenes.json")) && !File.Exists(Path.Combine(path, "scenes.backup.json")));
            document.Overlay.Preset = PresetKind.Compact; repo.Save(document); Assert(File.Exists(repo.BackupPath));
            Assert(Directory.GetFiles(path, "*.tmp").Length == 0);
        });
        check("legacy migration archives all originals and deterministically chooses first active visible scene", () =>
        {
            string path = TestDirectory("legacy-selection"); var legacyRepo = new SceneRepository(path);
            var hidden = LegacyScene("hidden", false, true); var active = LegacyScene("active hidden", true, false);
            var selected = LegacyScene("selected", true, true); var later = LegacyScene("later visible", true, true);
            selected.X = -1400; selected.Y = 44; selected.Width = 900; selected.ClickThrough = false;
            var legacy = new SceneDocument { Scenes = [hidden, active, selected, later] }; legacy.Settings.HotkeyFunction = 11;
            legacyRepo.Save(legacy); legacy.Scenes[0].Name = "updated"; legacyRepo.Save(legacy);
            Directory.CreateDirectory(Path.Combine(path, "assets", "nested"));
            File.WriteAllBytes(Path.Combine(path, "assets", "sample.png"), [1, 2, 3, 4]);
            File.WriteAllBytes(Path.Combine(path, "assets", "nested", "other.bin"), [5, 6, 7]);
            var sourcePaths = new[] { legacyRepo.FilePath, legacyRepo.BackupPath, Path.Combine(path, "assets", "sample.png"), Path.Combine(path, "assets", "nested", "other.bin") };
            var originals = sourcePaths.ToDictionary(file => file, file => (Hash: Hash(file), LastWrite: File.GetLastWriteTimeUtc(file)));
            var repo = new OverlayRepository(path); var result = repo.Load();
            Assert(result.CanSave && result.ArchivePath != null && result.Warning != null);
            Assert(result.Warning!.Contains("4개 중 ‘selected’의 위치") && !result.Warning.Contains(selected.Id.ToString()));
            Assert(result.Document.Overlay.Id == selected.Id && result.Document.Overlay.Preset == PresetKind.MusicCard && result.Document.Overlay.Scale == 1.5);
            Assert(result.Document.Overlay.X == -1400 && result.Document.Overlay.Y == 44 && !result.Document.Overlay.ClickThrough && result.Document.Settings.HotkeyFunction == 11);
            foreach (var file in sourcePaths) Assert(Hash(file) == originals[file].Hash && File.GetLastWriteTimeUtc(file) == originals[file].LastWrite);
            AssertArchive(result.ArchivePath!);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ArchivePath!, "manifest.json")));
            Assert(metadata.RootElement.GetProperty("migration").GetProperty("selectedSceneId").GetGuid() == selected.Id);
            var restored = new SceneRepository(result.ArchivePath!).Load(); Assert(SceneJson.Serialize(restored.Document) == SceneJson.Serialize(legacy));
            Assert(Hash(Path.Combine(result.ArchivePath!, "assets", "sample.png")) == originals[sourcePaths[2]].Hash);
            string manifestHash = Hash(Path.Combine(result.ArchivePath!, "manifest.json"));
            int archives = Directory.GetDirectories(Path.Combine(path, "backups", "M4.3")).Length;
            var reloaded = repo.Load(); Assert(reloaded.Document.Overlay.Id == selected.Id && reloaded.ArchivePath is null);
            Assert(Directory.GetDirectories(Path.Combine(path, "backups", "M4.3")).Length == archives && Hash(Path.Combine(result.ArchivePath!, "manifest.json")) == manifestHash);
        });
        check("legacy selection falls back to active first file item then defaults", () =>
        {
            for (int variant = 0; variant < 3; variant++)
            {
                string path = TestDirectory("legacy-fallback-" + variant); var a = LegacyScene("first", false, false); var b = LegacyScene("second", variant == 0, false);
                var legacy = new SceneDocument { Scenes = variant == 2 ? [] : [a, b] }; legacy.Settings.GridSnap = false;
                new SceneRepository(path).Save(legacy); var result = new OverlayRepository(path).Load();
                Assert(result.CanSave && !result.Document.Settings.GridSnap && !result.Document.Overlay.Enabled);
                if (variant < 2) Assert(result.Document.Overlay.Id == (variant == 0 ? b.Id : a.Id));
                else Assert(result.Document.Overlay.Preset == PresetKind.MusicCard);
                AssertArchive(result.ArchivePath!);
            }
        });
        check("legacy corrupt primary uses backup and unreadable content still preserves raw bytes", () =>
        {
            foreach (bool backupValid in new[] { true, false })
            {
                string path = TestDirectory("legacy-corrupt-" + backupValid); Directory.CreateDirectory(path);
                var scene = LegacyScene("from backup", true, true); scene.Width = 100;
                File.WriteAllText(Path.Combine(path, "scenes.json"), "{broken-primary");
                File.WriteAllText(Path.Combine(path, "scenes.backup.json"), backupValid ? SceneJson.Serialize(new() { Scenes = [scene] }) : "broken-backup");
                var result = new OverlayRepository(path).Load(); Assert(result.CanSave && result.ArchivePath != null);
                if (backupValid) Assert(result.Document.Overlay.Id == scene.Id && result.Document.Overlay.Scale == .75);
                else Assert(!result.Document.Overlay.Enabled);
                Assert(File.ReadAllText(Path.Combine(result.ArchivePath!, "scenes.json")) == "{broken-primary");
                AssertArchive(result.ArchivePath!);
            }
        });
        check("legacy archive failure blocks migration writes until source can be safely read", () =>
        {
            string path = TestDirectory("legacy-archive-locked"); new SceneRepository(path).Save(new() { Scenes = [LegacyScene("locked asset", true, true)] });
            Directory.CreateDirectory(Path.Combine(path, "assets")); string asset = Path.Combine(path, "assets", "locked.png"); File.WriteAllBytes(asset, [1, 2, 3]);
            string before = Hash(Path.Combine(path, "scenes.json")); var repo = new OverlayRepository(path);
            using (var held = new FileStream(asset, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var blocked = repo.Load(); Assert(!blocked.CanSave && blocked.Warning != null && !File.Exists(repo.FilePath));
                Reject(() => repo.Save(blocked.Document));
            }
            Assert(Hash(Path.Combine(path, "scenes.json")) == before && !File.Exists(repo.FilePath));
            var retried = repo.Load(); Assert(retried.CanSave && File.Exists(repo.FilePath)); AssertArchive(retried.ArchivePath!);
        });
        check("schema 2 corruption uses its backup and keeps immutable copies before replacement", () =>
        {
            string path = TestDirectory("new-corrupt"); var repo = new OverlayRepository(path); var document = new OverlayDocument();
            repo.Save(document); string original = OverlayJson.Serialize(document); document.Overlay.Preset = PresetKind.Cinematic; repo.Save(document);
            File.WriteAllText(repo.FilePath, "broken-v2"); string backupBefore = Hash(repo.BackupPath);
            var result = repo.Load(); Assert(result.CanSave && OverlayJson.Serialize(result.Document) == original && result.ArchivePath != null);
            Assert(File.ReadAllText(Path.Combine(result.ArchivePath!, "settings.v2.json")) == "broken-v2");
            AssertArchive(result.ArchivePath!); string archiveHash = Hash(Path.Combine(result.ArchivePath!, "manifest.json"));
            repo.Save(result.Document); Assert(Hash(repo.BackupPath) == backupBefore);
            Assert(Hash(Path.Combine(result.ArchivePath!, "manifest.json")) == archiveHash && OverlayJson.Serialize(repo.Load().Document) == original);
        });
        check("missing schema 2 primary recovers backup without selecting legacy again", () =>
        {
            string path = TestDirectory("missing-v2-primary"); var repo = new OverlayRepository(path); var document = new OverlayDocument();
            document.Overlay.Preset = PresetKind.Compact; repo.Save(document); repo.Save(document); File.Delete(repo.FilePath);
            new SceneRepository(path).Save(new() { Scenes = [LegacyScene("must not migrate again", true, true)] });
            var result = repo.Load(); Assert(result.Document.Overlay.Id == document.Overlay.Id && result.Document.Overlay.Preset == PresetKind.Compact);
            AssertArchive(result.ArchivePath!); repo.Save(result.Document); Assert(File.Exists(repo.FilePath));
        });
        check("bad schema 2 data does not silently roll back to unrelated legacy scenes", () =>
        {
            string path = TestDirectory("both-v2-corrupt"); var legacy = LegacyScene("old unrelated", true, true); new SceneRepository(path).Save(new() { Scenes = [legacy] });
            var repo = new OverlayRepository(path); File.WriteAllText(repo.FilePath, "broken-current"); File.WriteAllText(repo.BackupPath, "broken-backup");
            var result = repo.Load(); Assert(result.CanSave && result.Document.Overlay.Id != legacy.Id && !result.Document.Overlay.Enabled && result.ArchivePath != null);
            AssertArchive(result.ArchivePath!); Assert(File.ReadAllText(Path.Combine(result.ArchivePath!, "settings.v2.backup.json")) == "broken-backup");
        });
        check("unsupported schema is archived and cannot be overwritten by an older application", () =>
        {
            string path = TestDirectory("future"); var repo = new OverlayRepository(path); repo.Save(new());
            File.WriteAllText(repo.FilePath, File.ReadAllText(repo.FilePath).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3"));
            string before = Hash(repo.FilePath); var result = repo.Load(); Assert(!result.CanSave && result.ArchivePath != null);
            Reject(() => repo.Save(new())); Assert(Hash(repo.FilePath) == before); AssertArchive(result.ArchivePath!);
        });
        check("schema 2 I O errors block loading defaults from overwriting original settings", () =>
        {
            string path = TestDirectory("new-load-locked"); var repo = new OverlayRepository(path); var document = new OverlayDocument(); document.Overlay.Preset = PresetKind.Cinematic; repo.Save(document);
            string before = Hash(repo.FilePath);
            using (var held = new FileStream(repo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                var blocked = repo.Load(); Assert(!blocked.CanSave && blocked.ArchivePath is null); Reject(() => repo.Save(blocked.Document));
            }
            Reject(() => repo.Save(new())); Assert(Hash(repo.FilePath) == before);
            var reloaded = repo.Load(); Assert(reloaded.CanSave && reloaded.Document.Overlay.Preset == PresetKind.Cinematic);
        });
        check("schema 2 save sharing failure preserves current backup and clears temporary files", () =>
        {
            string path = TestDirectory("new-save-locked"); var repo = new OverlayRepository(path); var document = new OverlayDocument(); repo.Save(document); repo.Save(document);
            string original = Hash(repo.FilePath), backup = Hash(repo.BackupPath); document.Overlay.Scale = 1.5;
            using (var held = new FileStream(repo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete)) Reject(() => repo.Save(document));
            Assert(Hash(repo.FilePath) == original && Hash(repo.BackupPath) == backup && Directory.GetFiles(path, "*.tmp").Length == 0);
            repo.Save(document); Assert(repo.Load().Document.Overlay.Scale == 1.5);
        });
        check("UTF8 BOM settings and legacy bytes load without changing original encoding", () =>
        {
            string path = TestDirectory("legacy-bom"); Directory.CreateDirectory(path); var legacy = new SceneDocument { Scenes = [LegacyScene("BOM", true, true)] };
            byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(SceneJson.Serialize(legacy))]; string source = Path.Combine(path, "scenes.json"); File.WriteAllBytes(source, bytes);
            var repo = new OverlayRepository(path); var result = repo.Load(); Assert(result.CanSave && result.Document.Overlay.Id == legacy.Scenes[0].Id);
            Assert(File.ReadAllBytes(source).SequenceEqual(bytes) && File.ReadAllBytes(Path.Combine(result.ArchivePath!, "scenes.json")).SequenceEqual(bytes));
            var current = File.ReadAllBytes(repo.FilePath); File.WriteAllBytes(repo.FilePath, [0xEF, 0xBB, 0xBF, .. current]);
            Assert(repo.Load().CanSave && repo.Load().Document.Overlay.Id == legacy.Scenes[0].Id);
        });
    }
}

