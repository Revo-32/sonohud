using System.Numerics;
using System.Security.Cryptography;
using SonoHUD.Core;

var checks = new List<object>(); int failures = 0;
void Check(string name, Action test) { try { test(); checks.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; checks.Add(new { name, status = "FAIL", error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException) { return; } throw new Exception("invalid operation accepted"); }
SceneStore New() { var s = SceneFactory.Music("test", new()); return new(new() { Scenes = [s] }); }
bool Near(Matrix3x2 a, Matrix3x2 b) => Math.Abs(a.M11 - b.M11) < .001 && Math.Abs(a.M12 - b.M12) < .001 && Math.Abs(a.M21 - b.M21) < .001 && Math.Abs(a.M22 - b.M22) < .001 && Math.Abs(a.M31 - b.M31) < .005 && Math.Abs(a.M32 - b.M32) < .005;
Check("API v1 shared Core assembly retains the RC4 binding identity", () => Assert(typeof(LyricTrack).Assembly.GetName().Version == new Version(0, 7, 3, 0)));
Check("scene JSON roundtrip and schema", () => { var d = New().Document; Assert(SceneJson.Serialize(d) == SceneJson.Serialize(SceneJson.Parse(SceneJson.Serialize(d)))); d.SchemaVersion = 99; Reject(() => SceneValidation.Check(d)); });
Check("invalid coordinates roll back atomically", () => { var s = New(); var before = SceneJson.Serialize(s.Document); Reject(() => s.Execute("bad", d => d.Scenes[0].Width = -1)); Assert(before == SceneJson.Serialize(s.Document)); Assert(!s.CanUndo); });
Check("commands undo redo add move resize style visibility z remove", () => { var s = New(); var scene = s.Document.Scenes[0].Id; var e = new SceneElement { Type = ElementKind.Text }; s.AddElement(scene, e); s.UpdateElement(scene, e.Id, "move", x => x.X = 300); s.UpdateElement(scene, e.Id, "resize", x => x.Width = 222); s.UpdateElement(scene, e.Id, "style", x => x.Style.Foreground = "#FF112233"); s.ShowScene(scene, false); s.ChangeZOrder(scene, e.Id, 1); s.RemoveElements(scene, [e.Id]); Assert(s.Scene(scene).Elements.All(x => x.Id != e.Id)); for (int i = 0; i < 7; i++) s.Undo(); Assert(s.Scene(scene).Elements.Count == 7 && s.Scene(scene).Visible); for (int i = 0; i < 7; i++) s.Redo(); Assert(s.Scene(scene).Elements.Count == 7 && !s.Scene(scene).Visible); });
Check("drag transaction one undo and screen cancel", () => { var s = New(); var id = s.Document.Scenes[0].Id; string original = SceneJson.Serialize(s.Document); s.BeginTransaction("drag"); for (int i = 0; i < 20; i++) s.Execute("move", d => d.Scenes[0].X = i); Assert(!s.CanUndo); s.CommitTransaction(); s.Undo(); Assert(original == SceneJson.Serialize(s.Document)); s.Redo(); var before = SceneJson.Serialize(s.Document); s.BeginTransaction("screen"); s.ShowScene(id, false); s.Execute("size", d => d.Scenes[0].Width = 900); s.CancelTransaction(); Assert(before == SceneJson.Serialize(s.Document)); });
Check("group and ungroup preserve rotated world matrices", () => { var s = New(); var scene = s.Document.Scenes[0]; var ids = scene.Elements.Where(e => e.Type is ElementKind.TrackTitle or ElementKind.ArtistName).Select(e => e.Id).ToArray(); s.UpdateElement(scene.Id, ids[0], "rotate", e => { e.Rotation = 17; e.Scale = 1.2; }); var before = SceneMath.Flatten(s.Scene(scene.Id)).ToDictionary(f => f.Element.Id, f => f.Transform); var g = s.GroupElements(scene.Id, ids); foreach (var id in ids) Assert(Near(before[id], SceneMath.Flatten(s.Scene(scene.Id)).Single(f => f.Element.Id == id).Transform)); s.UpdateElement(scene.Id, g, "group transform", e => { e.Rotation = -33; e.Scale = .8; e.X += 47; }); var transformed = SceneMath.Flatten(s.Scene(scene.Id)).ToDictionary(f => f.Element.Id, f => f.Transform); s.UngroupElements(scene.Id, g); foreach (var id in ids) Assert(Near(transformed[id], SceneMath.Flatten(s.Scene(scene.Id)).Single(f => f.Element.Id == id).Transform)); });
Check("locked parent prevents move remove group and reorder", () => { var s = New(); var scene = s.Document.Scenes[0]; var ids = scene.Elements.Skip(1).Take(2).Select(e => e.Id).ToArray(); var g = s.GroupElements(scene.Id, ids); s.UpdateElement(scene.Id, g, "lock", e => e.Locked = true, true); Reject(() => s.UpdateElement(scene.Id, ids[0], "move", e => e.X++)); Reject(() => s.RemoveElements(scene.Id, ids)); Reject(() => s.GroupElements(scene.Id, ids)); Reject(() => s.ChangeZOrder(scene.Id, ids[0], 1)); Reject(() => s.UngroupElements(scene.Id, g)); });
Check("hierarchy cycles invalid and duplicate ids remapped", () => { var s = New(); var scene = s.Document.Scenes[0]; var g = s.GroupElements(scene.Id, scene.Elements.Skip(1).Take(2).Select(e => e.Id)); Reject(() => s.Execute("cycle", d => d.Scenes[0].Elements.Single(e => e.Id == g).ParentGroup = g)); var copy = s.DuplicateScene(scene.Id); var all = s.Document.Scenes.SelectMany(x => x.Elements).Select(e => e.Id).ToArray(); Assert(all.Distinct().Count() == all.Length); SceneValidation.Check(s.Document); string first = SceneJson.Serialize(new() { Scenes = [s.Scene(scene.Id)] }); s.ShowScene(copy, false); Assert(first == SceneJson.Serialize(new() { Scenes = [s.Scene(scene.Id)] })); });
Check("hit test applies scale rotation parent and visibility", () => { var s = New(); var scene = s.Document.Scenes[0]; var e = scene.Elements.Single(x => x.Type == ElementKind.TrackTitle); s.UpdateElement(scene.Id, e.Id, "transform", x => { x.Rotation = 19; x.Scale = 1.3; }); var f = SceneMath.Flatten(s.Scene(scene.Id)).Single(x => x.Element.Id == e.Id); var p = Vector2.Transform(new(8, 8), f.Transform); Assert(SceneMath.Hit(s.Scene(scene.Id), p.X, p.Y) == e.Id); s.UpdateElement(scene.Id, e.Id, "hide", x => x.Visible = false); Assert(SceneMath.Hit(s.Scene(scene.Id), p.X, p.Y) != e.Id); });
Check("timeline remains unconfirmed until fresh update", () => { var gate = new TimelineGate(); var t = DateTimeOffset.UtcNow; Assert(gate.Accept(true, t)); Assert(!gate.Accept(true, t)); for (int i = 0; i < 5; i++) Assert(!gate.Accept(false, t)); Assert(gate.Accept(false, t.AddSeconds(1))); gate.Reset(); Assert(gate.Accept(true, t)); });
Check("timeline gate retains the latest source timestamp across reordered and empty reads", () => {
    var gate = new TimelineGate();
    var latest = DateTimeOffset.UnixEpoch.AddMinutes(2);
    Assert(gate.Accept(false, latest));
    Assert(!gate.Accept(false, latest.AddSeconds(-4)));
    Assert(!gate.Accept(false, default));
    Assert(!gate.Accept(true, latest.AddSeconds(-1)));
    Assert(!gate.Accept(false, latest));
    Assert(gate.Accept(false, latest.AddMilliseconds(1)));
    Assert(gate.Accept(false, latest.AddMilliseconds(1)));
    gate.Reset();
    Assert(gate.Accept(true, latest.AddDays(-1)));
});
string temp = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "SonoHUD-tests-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(temp);
Check("atomic save backup corrupt recovery preserve original", () => { var repo = new SceneRepository(Path.Combine(temp, "recover")); var d = New().Document; repo.Save(d); d.Scenes[0].Name = "second"; repo.Save(d); Assert(repo.Load().Document.Scenes[0].Name == "second"); File.WriteAllText(repo.FilePath, "broken"); var recovered = repo.Load(); Assert(recovered.Recovered && recovered.Document.Scenes[0].Name != "second"); repo.Save(recovered.Document); Assert(Directory.GetFiles(repo.DirectoryPath, "scenes.corrupt-*.json").Length == 1); Assert(File.ReadAllText(Directory.GetFiles(repo.DirectoryPath, "scenes.corrupt-*.json")[0]) == "broken"); Assert(Directory.GetFiles(repo.DirectoryPath, "*.tmp").Length == 0); });
Check("save error leaves valid primary unchanged", () => { var repo = new SceneRepository(Path.Combine(temp, "save-error")); var d = New().Document; repo.Save(d); string before = File.ReadAllText(repo.FilePath); using (var stream = new FileStream(repo.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) { d.Scenes[0].Name = "failed"; Reject(() => repo.Save(d)); } Assert(File.ReadAllText(repo.FilePath) == before); Assert(Directory.GetFiles(repo.DirectoryPath, "*.tmp").Length == 0); });
Check("legacy RevoMusic migration preserves source and assets", () => { string legacy = Path.Combine(temp, "RevoMusic"), target = Path.Combine(temp, "SonoHUD"); new SceneRepository(legacy).Save(New().Document); Directory.CreateDirectory(Path.Combine(legacy, "assets")); File.WriteAllBytes(Path.Combine(legacy, "assets", "sample.png"), [1, 2, 3]); var hash = SHA256.HashData(File.ReadAllBytes(Path.Combine(legacy, "scenes.json"))); Assert(DataMigration.ImportLegacy(legacy, target) != null); Assert(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(legacy, "scenes.json"))))); Assert(File.ReadAllBytes(Path.Combine(target, "assets", "sample.png")).SequenceEqual(new byte[] { 1, 2, 3 })); Assert(DataMigration.ImportLegacy(legacy, target) == null); });
Check("repeated create delete group history bounded functional", () => { var s = New(); var id = s.Document.Scenes[0].Id; for (int i = 0; i < 30; i++) { var copy = s.DuplicateScene(id); var e = s.Scene(copy).Elements.Skip(1).Take(2).Select(e => e.Id).ToArray(); var g = s.GroupElements(copy, e); s.UngroupElements(copy, g); s.ShowScene(copy, false); s.ShowScene(copy, true); s.Execute("delete scene", d => d.Scenes.RemoveAll(x => x.Id == copy)); } Assert(s.Document.Scenes.Count == 1); for (int i = 0; i < 50; i++) s.Undo(); SceneValidation.Check(s.Document); });
Check("null scene and element fail with a validation error", () =>
{
    Reject(() => SceneJson.Parse("{\"schemaVersion\":1,\"scenes\":[null]}"));
    var document = New().Document; document.Scenes[0].Elements.Add(null!);
    Reject(() => SceneValidation.Check(document));
});
Check("duplicate scene accepts maximum name and coordinates", () =>
{
    var document = New().Document; document.Scenes[0].Name = new string('가', 100);
    document.Scenes[0].X = 100000; document.Scenes[0].Y = 99999;
    var store = new SceneStore(document); string before = SceneJson.Serialize(store.Document);
    var duplicate = store.DuplicateScene(document.Scenes[0].Id);
    Assert(store.Scene(duplicate).Name.Length <= 100 && store.Scene(duplicate).Name.EndsWith(" 복사"));
    SceneValidation.Check(store.Document); store.Undo(); Assert(SceneJson.Serialize(store.Document) == before);
    store.Redo(); Assert(store.Document.Scenes.Any(s => s.Id == duplicate));
});
Check("M7 fresh profile waits for online lyrics opt-in and updates retain prior choices", () =>
{
    var repo = new OverlayRepository(Path.Combine(temp, "release-privacy"));
    var first = repo.Load().Document;
    Assert(first.Settings.Lyrics?.OnlineEnabled == false);
    foreach (bool enabled in new[] { true, false })
    {
        first.Settings.Lyrics = new LyricsSettings { OnlineEnabled = enabled };
        repo.Save(first);
        Assert(repo.Load().Document.Settings.Lyrics?.OnlineEnabled == enabled);
    }
    first.Settings.Lyrics = null;
    repo.Save(first);
    Assert((repo.Load().Document.Settings.Lyrics ?? new LyricsSettings()).OnlineEnabled);
});
Check("M7 unreadable or unsupported settings do not enable external lookup", () =>
{
    foreach (string example in new[] { "broken-pair", "future-version", "unreadable-legacy" })
    {
        string path = Path.Combine(temp, example);
        Directory.CreateDirectory(path);
        var repo = new OverlayRepository(path);
        if (example == "future-version")
            File.WriteAllText(repo.FilePath, OverlayJson.Serialize(new OverlayDocument()).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99"));
        else if (example == "unreadable-legacy")
            File.WriteAllText(Path.Combine(path, "scenes.json"), "{");
        else
        {
            File.WriteAllText(repo.FilePath, "{");
            File.WriteAllText(repo.BackupPath, "{");
        }
        var recovered = repo.Load();
        Assert(recovered.Document.Settings.Lyrics?.OnlineEnabled == false, example);
        Assert(recovered.CanSave == (example != "future-version"), example);
    }
});
Check("missing primary recovers an existing backup", () =>
{
    var repo = new SceneRepository(Path.Combine(temp, "missing-primary")); var document = New().Document;
    string originalName = document.Scenes[0].Name;
    repo.Save(document); document.Scenes[0].Name = "newer"; repo.Save(document); File.Delete(repo.FilePath);
    string backup = File.ReadAllText(repo.BackupPath); var loaded = repo.Load();
    Assert(loaded.Recovered && loaded.HasValidData && loaded.Document.Scenes.Count == 1 && loaded.Document.Scenes[0].Name == originalName);
    repo.Save(loaded.Document); Assert(File.ReadAllText(repo.BackupPath) == backup);
});
Check("read sharing failure never quarantines a valid primary", () =>
{
    var repo = new SceneRepository(Path.Combine(temp, "share-delete")); var document = New().Document;
    repo.Save(document); string original = File.ReadAllText(repo.FilePath);
    using (var held = new FileStream(repo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
    {
        document.Scenes[0].Name = "must not replace";
        Reject(() => repo.Save(document));
    }
    Assert(File.ReadAllText(repo.FilePath) == original);
    Assert(Directory.GetFiles(repo.DirectoryPath, "scenes.corrupt-*.json").Length == 0);
    Assert(Directory.GetFiles(repo.DirectoryPath, "*.tmp").Length == 0);
});
Check("migration recovers valid empty scene settings from backup", () =>
{
    string legacy = Path.Combine(temp, "empty-legacy"), target = Path.Combine(temp, "empty-new");
    var repo = new SceneRepository(legacy); var document = new SceneDocument(); document.Settings.GridSnap = false;
    repo.Save(document); repo.Save(document); File.WriteAllText(repo.FilePath, "broken");
    Assert(DataMigration.ImportLegacy(legacy, target) != null);
    Assert(File.Exists(Path.Combine(target, "scenes.json")));
    var recovered = new SceneRepository(target).Load(); Assert(!recovered.Document.Settings.GridSnap && recovered.Document.Scenes.Count == 0);
});
Check("migration retries already copied identical assets", () =>
{
    string legacy = Path.Combine(temp, "partial-legacy"), target = Path.Combine(temp, "partial-new");
    new SceneRepository(legacy).Save(New().Document);
    Directory.CreateDirectory(Path.Combine(legacy, "assets")); Directory.CreateDirectory(Path.Combine(target, "assets"));
    File.WriteAllBytes(Path.Combine(legacy, "assets", "sample.png"), [1, 2, 3]);
    File.WriteAllBytes(Path.Combine(target, "assets", "sample.png"), [1, 2, 3]);
    Assert(DataMigration.ImportLegacy(legacy, target) != null);
    Assert(new SceneRepository(target).Load().Document.Scenes.Count == 1);
    Assert(File.ReadAllBytes(Path.Combine(target, "assets", "sample.png")).SequenceEqual(new byte[] { 1, 2, 3 }));
});
Check("wide group keeps elements beyond the scene and across monitors", () =>
{
    var store = New(); var scene = store.Document.Scenes[0]; var ids = scene.Elements.Skip(1).Take(2).Select(e => e.Id).ToArray();
    store.UpdateElement(scene.Id, ids[0], "left monitor", e => e.X = -3000);
    store.UpdateElement(scene.Id, ids[1], "right monitor", e => e.X = 4000);
    var before = SceneMath.Flatten(store.Scene(scene.Id)).ToDictionary(f => f.Element.Id, f => f.Transform);
    var group = store.GroupElements(scene.Id, ids); Assert(store.Scene(scene.Id).Elements.Single(e => e.Id == group).Width > 4096);
    store.UngroupElements(scene.Id, group);
    foreach (var id in ids) Assert(Near(before[id], SceneMath.Flatten(store.Scene(scene.Id)).Single(f => f.Element.Id == id).Transform));
    Reject(() => store.UpdateElement(scene.Id, ids[0], "invalid normal width", e => e.Width = 4097));
    Reject(() => store.Execute("invalid scene width", d => d.Scenes[0].Width = 4097));
});
Check("duplicate group subtree remaps ids and is one undo", () =>
{
    var store = New(); var scene = store.Document.Scenes[0]; var childIds = scene.Elements.Skip(1).Take(2).Select(e => e.Id).ToArray();
    var inner = store.GroupElements(scene.Id, childIds);
    var other = store.Scene(scene.Id).Elements.Single(e => e.Type == ElementKind.ArtistName).Id;
    var outer = store.GroupElements(scene.Id, [inner, other]);
    store.UpdateElement(scene.Id, outer, "transform", e => { e.Rotation = 25; e.Scale = 1.3; e.Opacity = .7; });
    string before = SceneJson.Serialize(store.Document); int count = store.Scene(scene.Id).Elements.Count;
    var copies = store.DuplicateElements(scene.Id, [outer, childIds[0]]);
    Assert(copies.Count == 1); var copied = store.Scene(scene.Id).Elements.Single(e => e.Id == copies[0]);
    var original = store.Scene(scene.Id).Elements.Single(e => e.Id == outer);
    Assert(copied.X == original.X + 16 && copied.Y == original.Y + 16 && copied.Scale == original.Scale && copied.Opacity == original.Opacity);
    Assert(store.Scene(scene.Id).Elements.Count == count + 5);
    var copiedInner = store.Scene(scene.Id).Elements.Single(e => e.ParentGroup == copied.Id && e.Type == ElementKind.Group);
    Assert(store.Scene(scene.Id).Elements.Count(e => e.ParentGroup == copiedInner.Id) == 2);
    var copiedText = store.Scene(scene.Id).Elements.Single(e => e.ParentGroup == copiedInner.Id && e.Type == ElementKind.TrackTitle);
    Assert(copiedText.Style == store.Scene(scene.Id).Elements.Single(e => e.Id == childIds[1]).Style);
    Assert(!ReferenceEquals(copiedText.Style, store.Scene(scene.Id).Elements.Single(e => e.Id == childIds[1]).Style));
    string after = SceneJson.Serialize(store.Document); store.Undo(); Assert(SceneJson.Serialize(store.Document) == before);
    store.Redo(); Assert(SceneJson.Serialize(store.Document) == after); SceneValidation.Check(store.Document);
});
Check("duplicate child preserves external parent and locked rejection is atomic", () =>
{
    var store = New(); var scene = store.Document.Scenes[0]; var ids = scene.Elements.Skip(1).Take(2).Select(e => e.Id).ToArray();
    var group = store.GroupElements(scene.Id, ids);
    var copy = store.DuplicateElements(scene.Id, [ids[0]]).Single();
    Assert(store.Scene(scene.Id).Elements.Single(e => e.Id == copy).ParentGroup == group);
    store.UpdateElement(scene.Id, group, "lock", e => e.Locked = true, true);
    string before = SceneJson.Serialize(store.Document); int revision = store.Revision;
    Reject(() => store.DuplicateElements(scene.Id, [ids[0]]));
    Reject(() => store.DuplicateElements(scene.Id, [group]));
    Reject(() => store.DuplicateElements(scene.Id, [Guid.NewGuid()]));
    Assert(SceneJson.Serialize(store.Document) == before && store.Revision == revision);
    Assert(store.DuplicateElements(scene.Id, []).Count == 0 && store.Revision == revision);
});
Check("duplicate over element limit rolls back without consuming undo", () =>
{
    var document = new SceneDocument { Scenes = [new Scene { Elements = Enumerable.Range(0, 200).Select(i => new SceneElement { Name = "item " + i, Type = ElementKind.Text }).ToList() }] };
    var store = new SceneStore(document); string before = SceneJson.Serialize(store.Document);
    Reject(() => store.DuplicateElements(document.Scenes[0].Id, [document.Scenes[0].Elements[0].Id]));
    Assert(before == SceneJson.Serialize(store.Document) && !store.CanUndo);
});
Check("load sharing errors do not silently replace the document with defaults", () =>
{
    var repo = new SceneRepository(Path.Combine(temp, "load-sharing")); repo.Save(New().Document);
    string before = File.ReadAllText(repo.FilePath);
    using (var held = new FileStream(repo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete)) Reject(() => repo.Load());
    Assert(File.ReadAllText(repo.FilePath) == before && repo.Load().HasValidData);
});
Check("migration conflicting assets preserves both originals", () =>
{
    string legacy = Path.Combine(temp, "conflict-legacy"), target = Path.Combine(temp, "conflict-new");
    new SceneRepository(legacy).Save(New().Document);
    Directory.CreateDirectory(Path.Combine(legacy, "assets")); Directory.CreateDirectory(Path.Combine(target, "assets"));
    File.WriteAllBytes(Path.Combine(legacy, "assets", "same.png"), [1, 2, 3]); File.WriteAllBytes(Path.Combine(target, "assets", "same.png"), [3, 2, 1]);
    Reject(() => DataMigration.ImportLegacy(legacy, target)); Assert(!File.Exists(Path.Combine(target, "scenes.json")));
    Assert(File.ReadAllBytes(Path.Combine(legacy, "assets", "same.png")).SequenceEqual(new byte[] { 1, 2, 3 }));
    Assert(File.ReadAllBytes(Path.Combine(target, "assets", "same.png")).SequenceEqual(new byte[] { 3, 2, 1 }));
});
Check("missing optional fields get defaults and damaged data is explicit", () =>
{
    var defaults = SceneJson.Parse("{}"); Assert(defaults.SchemaVersion == 1 && defaults.Settings.GridSnap && defaults.Scenes.Count == 0);
    var repo = new SceneRepository(Path.Combine(temp, "unrecoverable")); Directory.CreateDirectory(repo.DirectoryPath);
    File.WriteAllText(repo.FilePath, "{\"schemaVersion\":99}"); File.WriteAllText(repo.BackupPath, "broken");
    var result = repo.Load(); Assert(result.Recovered && !result.HasValidData && result.Warning != null);
});
Check("fixed seed mixed editing maintains exact undo redo snapshots and saved state", () =>
{
    var store = New(); var scene = store.Document.Scenes[0].Id; var random = new Random(4101);
    var snapshots = new List<string> { SceneJson.Serialize(store.Document) }; var repo = new SceneRepository(Path.Combine(temp, "sequence"));
    for (int step = 0; step < 35; step++)
    {
        var elements = store.Scene(scene).Elements.Where(e => !e.Locked && e.Type != ElementKind.Group).ToArray(); var e = elements[random.Next(elements.Length)];
        switch (step % 7)
        {
            case 0: store.AddElement(scene, new SceneElement { Type = ElementKind.Text, Name = "한글 English 日本語 " + step }); break;
            case 1: store.UpdateElement(scene, e.Id, "move", x => { x.X = random.Next(-4000, 7000); x.Y = random.Next(-2400, 3000); }); break;
            case 2: store.UpdateElement(scene, e.Id, "size", x => { x.Width = random.Next(20, 700); x.Height = random.Next(10, 200); }); break;
            case 3: store.DuplicateElements(scene, [e.Id]); break;
            case 4: store.UpdateElement(scene, e.Id, "style", x => { x.Style.Text = "검증 " + step; x.Style.FontSize = 12 + step; }); break;
            case 5: store.ShowScene(scene, !store.Scene(scene).Visible); break;
            case 6: store.RemoveElements(scene, [e.Id]); break;
        }
        SceneValidation.Check(store.Document); snapshots.Add(SceneJson.Serialize(store.Document));
        repo.Save(store.Document); Assert(SceneJson.Serialize(repo.Load().Document) == snapshots[^1]);
    }
    for (int i = snapshots.Count - 2; i >= 0; i--) { Assert(store.CanUndo); store.Undo(); Assert(SceneJson.Serialize(store.Document) == snapshots[i], "undo " + i); }
    Assert(!store.CanUndo);
    for (int i = 1; i < snapshots.Count; i++) { Assert(store.CanRedo); store.Redo(); Assert(SceneJson.Serialize(store.Document) == snapshots[i], "redo " + i); }
    Assert(!store.CanRedo);
});
Check("cancelled and unchanged transactions preserve redo while a new edit clears it", () =>
{
    var store = New(); var scene = store.Document.Scenes[0].Id;
    store.ShowScene(scene, false); store.Undo(); Assert(store.CanRedo);
    var before = SceneJson.Clone(store.Document); string serialized = SceneJson.Serialize(before);
    store.BeginTransaction("cancelled move"); store.Execute("move", d => d.Scenes[0].X += 300); store.CancelTransaction();
    Assert(SceneJson.Serialize(store.Document) == serialized && store.CanRedo && !store.CanUndo);
    store.BeginTransaction("unchanged preview"); store.Execute("preview", d => d.Scenes[0].X += 400); store.RestorePreview(before); store.CommitTransaction();
    Assert(SceneJson.Serialize(store.Document) == serialized && store.CanRedo && !store.CanUndo);
    Reject(() => store.Execute("invalid", d => d.Scenes[0].Width = -5)); Assert(store.CanRedo);
    store.Redo(); Assert(!store.Scene(scene).Visible); store.Undo();
    store.Execute("new branch", d => d.Scenes[0].Name = "new branch"); Assert(!store.CanRedo);
});
DesktopLayoutTests.Run(Check);
OverlayTests.Run(Check, temp);
TaskbarConfigTests.Run(Check, temp);
OverlayResizeGeometryTests.Run(Check);
PresetDesignTests.Run(Check);
SpectrumTests.Run(Check, temp);
Check("6종 기본 테마와 임의 강조색의 텍스트 대비",()=>{
    foreach(var theme in MusicTheme.Gallery){
        foreach(var surface in new[]{theme.Background,theme.Surface,theme.Raised})
            foreach(var text in new[]{theme.Ink,theme.Secondary,theme.Muted,theme.Accent})
                Assert(MusicTheme.Contrast(text,surface)>=4.5,theme.Name+" "+text+" "+surface);
        foreach(var custom in new[]{"#FFFFFF","#000000","#123456","#FF00FF","invalid"})
            Assert(MusicTheme.Contrast(MusicTheme.Resolve(new(theme.Name,custom),null).Accent,theme.Surface)>=4.5);
    }
});
Check("이전 이름으로 저장한 4개 팔레트 및 사용자 지정 값을 보존",()=>{
    foreach (var (oldName,newName) in new[] { ("Rail Mint","Moss Paper"), ("Graphite Blue","Silverprint"), ("Nocturne","Night Ferry"), ("Studio Amber","Prism RGB"), ("Carbon Signal","Prism RGB"), ("Monochrome","Monochrome"), ("Porcelain","Porcelain") })
    {
        var settings = new AppearanceSettings(oldName, "#356AA4", "#C29A76", true);
        var doc = new OverlayDocument();
        doc.Settings.Appearance = settings;
        doc.Settings.MediaSourceAppId = "chosen-player.exe";
        var oldJson = System.Text.Json.JsonSerializer.Serialize(doc, OverlayJson.Options);
        var restored = OverlayJson.Clone(OverlayJson.Parse(oldJson));
        Assert(restored.Settings.Appearance == (settings with { Theme = newName }) && restored.Settings.MediaSourceAppId == doc.Settings.MediaSourceAppId);
        var theme = MusicTheme.Resolve(settings, null);
        Assert(theme.Name == newName && theme.Light == (newName is "Silverprint" or "Moss Paper" or "Porcelain"));
    }
    Assert(MusicTheme.Resolve(new(),null).Name == "Prism RGB");
});
Check("Studio Amber에서 저장된 민트는 Prism RGB의 조작 강조에만 적용",()=>{
    var configured=new AppearanceSettings("Studio Amber","#87DFC4");
    var theme=MusicTheme.Resolve(configured,null);
    var standard=MusicTheme.Gallery.Single(p=>p.Name=="Prism RGB");
    var config=new OverlayConfig{Preset=PresetKind.LyricsFocus,AccentColor="#FF87DFC4"};
    var scene=OverlayPresets.Create(config,new SpectrumSettings(),theme);
    SceneElement Role(string id)=>scene.Elements.Single(e=>e.Id==OverlayPresets.ElementId(config.Id,id));
    Assert(theme.Accent=="#87DFC4" && Role("sample-label").Style.Foreground==standard.Secondary);
    Assert(Role("progress").Style.Foreground==standard.Progress && Role("lyrics").Style.Background==standard.LyricsWord);
    Assert(new[]{standard.Secondary,standard.Progress,standard.LyricsWord}.All(c=>c!=theme.Accent));
    var auto=MusicTheme.Resolve(configured with{ArtworkAccent=true,Spectrum="#FFFFFF"},"#FF0000");
    Assert(auto.Accent!=theme.Accent && auto.Spectrum=="#FFFFFF" && auto.Progress==standard.Progress);
    Assert(MusicTheme.Resolve(configured with{ArtworkAccent=true},null)==theme);
});
Check("테마별 단어·진행·FFT 대비",()=>{
    foreach(var p in MusicTheme.Gallery){
        Assert(MusicTheme.Contrast(p.Progress,p.ProgressTrack)>=3,p.Name+" progress");
        foreach(var surface in new[]{p.Background,p.Surface,p.Raised}){
            Assert(MusicTheme.Contrast(p.LyricsWord,surface)>=4.5 && MusicTheme.Contrast(p.LyricsContext,surface)>=4.5,p.Name+" lyrics");
            Assert(MusicTheme.Contrast(p.Spectrum,surface)>=3,p.Name+" spectrum");
        }
    }
});
Check("가사 모션은 연속 재생에서만 실행",()=>{
    var a=new LyricFrame(2.99,1,false);var b=new LyricFrame(3,2,false);
    foreach(var profile in Enum.GetValues<AnimationProfile>()){
        Assert((LyricsMotion.Between(profile,true,a,b).Milliseconds>0)==(profile!=AnimationProfile.Off));
        Assert(LyricsMotion.Between(profile,false,a,b)==default);
        Assert(LyricsMotion.Between(profile,true,a,b with{Uncertain=true})==default);
        Assert(LyricsMotion.Between(profile,true,a,b with{Position=7})==default);
    }
});
File.WriteAllText(Path.Combine(temp, "results.json"), System.Text.Json.JsonSerializer.Serialize(new { total = checks.Count, failures, checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count - failures}/{checks.Count} passed"); return failures == 0 ? 0 : 1;
