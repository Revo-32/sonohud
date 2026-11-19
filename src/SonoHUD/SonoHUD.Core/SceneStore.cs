namespace SonoHUD.Core;

public sealed class SceneStore
{
    readonly List<(string Label, SceneDocument State)> undo = [];
    readonly List<(string Label, SceneDocument State)> redo = [];
    SceneDocument? transaction;
    string transactionLabel = "";
    public SceneDocument Document { get; private set; }
    public event EventHandler? Changed;
    public bool IsEditing => transaction is not null;
    public bool CanUndo => undo.Count > 0 && !IsEditing;
    public bool CanRedo => redo.Count > 0 && !IsEditing;
    public string UndoLabel => undo.Count > 0 ? undo[^1].Label : "";
    public int Revision { get; private set; }
    public SceneStore(SceneDocument document) { SceneValidation.Check(document); Document = SceneJson.Clone(document); }
    public Scene Scene(Guid id) => Document.Scenes.Single(s => s.Id == id);
    public void Execute(string label, Action<SceneDocument> change)
    {
        var next = SceneJson.Clone(Document); change(next); SceneValidation.Check(next);
        if (SceneJson.Serialize(next) == SceneJson.Serialize(Document)) return;
        if (!IsEditing) { Push(undo, (label, Document)); redo.Clear(); }
        Document = next; Notify();
    }
    void Notify() { Revision++; Changed?.Invoke(this, EventArgs.Empty); }
    static void Push(List<(string Label, SceneDocument State)> list, (string Label, SceneDocument State) value) { list.Add(value); if (list.Count > 40) list.RemoveAt(0); }
    public void BeginTransaction(string label) { if (IsEditing) throw new InvalidOperationException("이미 편집 중입니다."); transaction = SceneJson.Clone(Document); transactionLabel = label; Changed?.Invoke(this, EventArgs.Empty); }
    public void CommitTransaction() { if (transaction is null) return; var before = transaction; transaction = null; if (SceneJson.Serialize(before) != SceneJson.Serialize(Document)) { Push(undo, (transactionLabel, before)); redo.Clear(); } Changed?.Invoke(this, EventArgs.Empty); }
    public void CancelTransaction() { if (transaction is null) return; Document = transaction; transaction = null; Notify(); }
    public void RestorePreview(SceneDocument snapshot) { if (!IsEditing) throw new InvalidOperationException("편집 범위 밖입니다."); Document = SceneJson.Clone(snapshot); Notify(); }
    public void Undo() { if (!CanUndo) return; var entry = undo[^1]; undo.RemoveAt(undo.Count - 1); Push(redo, (entry.Label, Document)); Document = entry.State; Notify(); }
    public void Redo() { if (!CanRedo) return; var entry = redo[^1]; redo.RemoveAt(redo.Count - 1); Push(undo, (entry.Label, Document)); Document = entry.State; Notify(); }
    public void AddElement(Guid scene, SceneElement element) => Execute("요소 추가", d => d.Scenes.Single(s => s.Id == scene).Elements.Add(element with { Style = element.Style with { } }));
    public void RemoveElements(Guid scene, IEnumerable<Guid> ids) => Execute("요소 삭제", d =>
    {
        var s = d.Scenes.Single(x => x.Id == scene); var remove = ids.ToHashSet();
        if (SceneMath.Flatten(s).Any(f => remove.Contains(f.Element.Id) && f.Locked)) throw new InvalidOperationException("잠금을 먼저 해제하세요.");
        bool added; do { added = false; foreach (var e in s.Elements) if (e.ParentGroup is Guid p && remove.Contains(p)) added |= remove.Add(e.Id); } while (added);
        s.Elements.RemoveAll(e => remove.Contains(e.Id));
    });
    public void UpdateElement(Guid scene, Guid element, string label, Action<SceneElement> change, bool allowLocked = false) => Execute(label, d =>
    {
        var s = d.Scenes.Single(x => x.Id == scene); var e = s.Elements.Single(x => x.Id == element);
        if (!allowLocked && SceneMath.Flatten(s).Single(x => x.Element.Id == element).Locked) throw new InvalidOperationException("잠긴 요소는 편집할 수 없습니다."); change(e);
    });
    public Guid GroupElements(Guid scene, IEnumerable<Guid> ids)
    {
        var groupId = Guid.NewGuid(); Execute("그룹화", d =>
        {
            var s = d.Scenes.Single(x => x.Id == scene); var selected = s.Elements.Where(x => ids.Contains(x.Id)).OrderBy(x => x.ZIndex).ToArray();
            if (selected.Length < 2 || SceneMath.Flatten(s).Any(f => ids.Contains(f.Element.Id) && f.Locked) || selected.Select(x => x.ParentGroup).Distinct().Count() != 1) throw new InvalidOperationException("같은 그룹의 잠금 해제된 요소를 두 개 이상 선택하세요.");
            var bounds = selected.Select(e => SceneMath.BoundsOf(e, SceneMath.Local(e))).ToArray(); var left = bounds.Min(b => b.X); var top = bounds.Min(b => b.Y);
            var group = new SceneElement { Id = groupId, Type = ElementKind.Group, Name = "그룹", X = left, Y = top, Width = bounds.Max(b => b.X + b.Width) - left, Height = bounds.Max(b => b.Y + b.Height) - top, ParentGroup = selected[0].ParentGroup, ZIndex = selected.Max(e => e.ZIndex) };
            foreach (var e in selected) { e.X -= left; e.Y -= top; e.ParentGroup = groupId; }
            s.Elements.Add(group);
        }); return groupId;
    }
    public void UngroupElements(Guid scene, Guid id) => Execute("그룹 해제", d =>
    {
        var s = d.Scenes.Single(x => x.Id == scene); var g = s.Elements.Single(x => x.Id == id);
        if (g.Type != ElementKind.Group || SceneMath.Flatten(s).Single(f => f.Element.Id == id).Locked) throw new InvalidOperationException("잠금 해제된 그룹을 선택하세요.");
        var children = s.Elements.Where(x => x.ParentGroup == id).OrderBy(x => x.ZIndex).ToArray(); var siblings = s.Elements.Where(x => x.ParentGroup == g.ParentGroup).OrderBy(x => x.ZIndex).ToList(); var at = siblings.IndexOf(g); siblings.Remove(g); siblings.InsertRange(at, children);
        foreach (var e in children) { SceneMath.AssignTransform(e, SceneMath.Local(e) * SceneMath.Local(g)); e.ParentGroup = g.ParentGroup; e.Opacity *= g.Opacity; e.Visible &= g.Visible; }
        s.Elements.Remove(g); for (int i = 0; i < siblings.Count; i++) siblings[i].ZIndex = i;
    });
    public void ChangeZOrder(Guid scene, Guid id, int direction) => Execute("레이어 순서", d =>
    {
        var s = d.Scenes.Single(x => x.Id == scene); var e = s.Elements.Single(x => x.Id == id); if (SceneMath.Flatten(s).Single(f => f.Element.Id == id).Locked) throw new InvalidOperationException("잠금을 먼저 해제하세요.");
        var list = s.Elements.Where(x => x.ParentGroup == e.ParentGroup).OrderBy(x => x.ZIndex).ToList(); int at = list.IndexOf(e), next = Math.Clamp(at + direction, 0, list.Count - 1); (list[at], list[next]) = (list[next], list[at]); for (int i = 0; i < list.Count; i++) list[i].ZIndex = i;
    });
    public void ShowScene(Guid id, bool visible) => Execute(visible ? "Scene 표시" : "Scene 숨김", d => d.Scenes.Single(s => s.Id == id).Visible = visible);
    public IReadOnlyList<Guid> DuplicateElements(Guid scene, IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet(); var result = new List<Guid>();
        if (selected.Count == 0) return result;
        Execute("요소 복제", d =>
        {
            var s = d.Scenes.Single(x => x.Id == scene); var originals = s.Elements.ToDictionary(e => e.Id);
            if (selected.Any(id => !originals.ContainsKey(id))) throw new InvalidOperationException("복제할 요소를 다시 선택하세요.");
            if (SceneMath.Flatten(s).Any(f => selected.Contains(f.Element.Id) && f.Locked)) throw new InvalidOperationException("잠금을 먼저 해제하세요.");
            bool HasSelectedAncestor(SceneElement element)
            {
                var parent = element.ParentGroup;
                while (parent is Guid id) { if (selected.Contains(id)) return true; parent = originals[id].ParentGroup; }
                return false;
            }
            var roots = s.Elements.Where(e => selected.Contains(e.Id) && !HasSelectedAncestor(e)).ToArray();
            var rootIds = roots.Select(e => e.Id).ToHashSet(); var copyIds = rootIds.ToHashSet();
            bool added; do { added = false; foreach (var e in s.Elements) if (e.ParentGroup is Guid p && copyIds.Contains(p)) added |= copyIds.Add(e.Id); } while (added);
            var map = copyIds.ToDictionary(id => id, _ => Guid.NewGuid());
            var copies = s.Elements.Where(e => copyIds.Contains(e.Id)).Select(e =>
            {
                var copy = e with { Id = map[e.Id], Style = e.Style with { } };
                if (e.ParentGroup is Guid p && map.TryGetValue(p, out var parent)) copy.ParentGroup = parent;
                if (rootIds.Contains(e.Id))
                {
                    copy.Name += " 복사";
                    copy.X = Math.Clamp(copy.X + 16, -100000, 100000); copy.Y = Math.Clamp(copy.Y + 16, -100000, 100000);
                }
                return copy;
            }).ToDictionary(e => e.Id);
            // Put each copied root just above its source among the same siblings. Descendant
            // transforms and order stay unchanged; selecting both a group and child copies once.
            foreach (var parent in roots.Select(e => e.ParentGroup).Distinct())
            {
                int z = 0;
                foreach (var sibling in s.Elements.Where(e => e.ParentGroup == parent).OrderBy(e => e.ZIndex))
                {
                    sibling.ZIndex = z++;
                    if (rootIds.Contains(sibling.Id)) copies[map[sibling.Id]].ZIndex = z++;
                }
            }
            s.Elements.AddRange(copies.Values); result.AddRange(roots.Select(e => map[e.Id]));
        });
        return result;
    }
    public Guid DuplicateScene(Guid id)
    {
        Guid result = Guid.NewGuid(); Execute("Scene 복제", d =>
        {
            var s = d.Scenes.Single(x => x.Id == id); var copy = SceneJson.Clone(new() { Scenes = [s] }).Scenes[0]; var map = copy.Elements.ToDictionary(e => e.Id, _ => Guid.NewGuid());
            const string suffix = " 복사";
            copy.Id = result; copy.Name = s.Name[..Math.Min(s.Name.Length, 100 - suffix.Length)] + suffix;
            copy.X = Math.Clamp(copy.X + 32, -100000, 100000); copy.Y = Math.Clamp(copy.Y + 32, -100000, 100000);
            foreach (var e in copy.Elements) { e.Id = map[e.Id]; if (e.ParentGroup is Guid p) e.ParentGroup = map[p]; }
            d.Scenes.Add(copy);
        }); return result;
    }
}
