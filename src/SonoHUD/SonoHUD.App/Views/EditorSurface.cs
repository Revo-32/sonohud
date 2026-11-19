using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SonoHUD.Core;
using SonoHUD.App.Rendering;
using SonoHUD.App.ViewModels;
using SonoHUD.App.Platform;
using System.Windows.Automation.Peers;

namespace SonoHUD.App.Views;

internal sealed class EditorSurface : FrameworkElement
{
    internal MainViewModel? ViewModel { get; set; }
    internal bool OnScreen { get; set; }
    internal double Zoom { get; private set; } = 1;
    internal System.Windows.Point Origin { get; private set; }
    internal double ScreenZoom { get; set; } = 1;
    internal System.Windows.Point ScreenOrigin { get; set; }
    internal bool IsDragging => dragging;
    DesktopPoint gestureOrigin; double gestureScale;
    bool dragging, resizing, ownTransaction; int moveCount; System.Windows.Point start; SceneDocument? startDocument; Scene? startScene; Guid[] dragIds = [];
    internal EditorSurface() { Focusable = true; ClipToBounds = true; Cursor = Cursors.Arrow; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var vm = ViewModel; var scene = vm?.SelectedScene; if (!OnScreen) dc.DrawRectangle(ElementDrawing.Brush("#FF10141B"), null, new(0, 0, ActualWidth, ActualHeight)); if (scene is null) return;
        if (OnScreen) { Zoom = ScreenZoom; Origin = ScreenOrigin; }
        else if (!dragging)
        {
            var bounds = DesktopLayout.Union(new[] { DesktopLayout.Content(scene) }.Concat(SceneMath.Flatten(scene).Where(f => vm!.Selection.Contains(f.Element.Id)).Select(f => f.Bounds)));
            Zoom = Math.Max(.001, Math.Min(Math.Max(1, ActualWidth - 56) / bounds.Width, Math.Max(1, ActualHeight - 56) / bounds.Height));
            Origin = new((ActualWidth - bounds.Width * Zoom) / 2 - bounds.X * Zoom, (ActualHeight - bounds.Height * Zoom) / 2 - bounds.Y * Zoom);
        }
        dc.PushTransform(new TranslateTransform(Origin.X, Origin.Y)); dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        if (!OnScreen)
        {
            dc.DrawRectangle(ElementDrawing.Brush("#FF242C37"), new Pen(ElementDrawing.Brush("#FF3C4858"), 1 / Zoom), new(0, 0, scene.Width, scene.Height));
            if (vm!.GridSnap && Zoom > .3) for (double x = 0; x < scene.Width; x += 32) for (double y = 0; y < scene.Height; y += 32) dc.DrawEllipse(ElementDrawing.Brush("#554D5A6B"), null, new(x, y), .6 / Zoom, .6 / Zoom);
            foreach (var frame in SceneMath.Flatten(scene))
            {
                if (!frame.Visible || frame.Element.Type == ElementKind.Group) continue; var m = frame.Transform;
                dc.PushTransform(new MatrixTransform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32)); dc.PushOpacity(frame.Opacity); ElementDrawing.Draw(dc, frame.Element, vm.Media.State); dc.Pop(); dc.Pop();
            }
        }
        else
        {
            // Alpha-zero areas pass through the layered window. Only our editing areas receive input.
            var input = ElementDrawing.Brush("#01000000");
            dc.DrawRectangle(input, new Pen(ElementDrawing.Brush("#886ED8C5"), 1 / Zoom), new(0, 0, scene.Width, scene.Height));
            foreach (var f in SceneMath.Flatten(scene).Where(f => f.Element.Type != ElementKind.Group && (f.Visible || vm!.Selection.Contains(f.Element.Id))))
            { var b = f.Bounds; dc.DrawRectangle(input, null, new(b.X, b.Y, b.Width, b.Height)); }
        }
        foreach (var frame in SceneMath.Flatten(scene).Where(f => vm!.Selection.Contains(f.Element.Id)))
        {
            var b = frame.Bounds; var rect = new Rect(b.X, b.Y, b.Width, b.Height); var pen = new Pen(ElementDrawing.Brush(frame.Locked ? "#FFA1AAB8" : "#FF6ED8C5"), 1.5 / Zoom); dc.DrawRectangle(null, pen, rect);
            if (!frame.Locked) { double r = 5 / Zoom; dc.DrawRectangle(Brushes.White, pen, new(b.X + b.Width - r, b.Y + b.Height - r, r * 2, r * 2)); }
        }
        dc.Pop(); dc.Pop();
    }
    System.Windows.Point Local(System.Windows.Point p) => new((p.X - Origin.X) / Zoom, (p.Y - Origin.Y) / Zoom);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); var vm = ViewModel; var scene = vm?.SelectedScene; if (scene is null) return; Focus(); var p = Local(e.GetPosition(this)); resizing = false;
        if (vm!.SelectedElement is { } chosen)
        {
            var f = SceneMath.Flatten(scene).Single(f => f.Element.Id == chosen.Id); var b = f.Bounds;
            if (!f.Locked && Math.Abs(p.X - b.X - b.Width) < 10 / Zoom && Math.Abs(p.Y - b.Y - b.Height) < 10 / Zoom) resizing = true;
        }
        if (!resizing)
        {
            var hit = SceneMath.Hit(scene, p.X, p.Y);
            if (hit is null) { vm.Select([]); InvalidateVisual(); return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { var set = vm.Selection.ToHashSet(); if (!set.Add(hit.Value)) set.Remove(hit.Value); vm.Select(set); return; }
            if (!vm.Selection.Contains(hit.Value) && !HasSelectedAncestor(scene, hit.Value, vm.Selection)) vm.Select([hit.Value]);
        }
        startDocument = SceneJson.Clone(vm.Store.Document); startScene = startDocument.Scenes.Single(s => s.Id == scene.Id); start = p;
        if (OnScreen) { var monitors = Native.DesktopMonitors(); gestureOrigin = DesktopLayout.VisibleOrigin(scene, monitors); gestureScale = DesktopLayout.Resolve(scene, monitors).Scale; Native.GetCursorPos(out var pointer); start = new((pointer.X - gestureOrigin.X) / gestureScale, (pointer.Y - gestureOrigin.Y) / gestureScale); }
        dragIds = vm.Selection.Where(id => !HasSelectedAncestor(startScene, id, vm.Selection)).ToArray();
        ownTransaction = !vm.Store.IsEditing; if (ownTransaction) vm.Store.BeginTransaction(resizing ? "크기 변경" : "요소 이동"); dragging = true; vm.PointerGestureActive = true; CaptureMouse(); e.Handled = true;
        moveCount = 0; vm.Log.Write("editor_gesture_start", new { OnScreen, resizing, start.X, start.Y, ids = dragIds });
    }
    static bool HasSelectedAncestor(Scene scene, Guid id, IReadOnlySet<Guid> selected)
    { var e = scene.Elements.Single(e => e.Id == id); while (e.ParentGroup is Guid p) { if (selected.Contains(p)) return true; e = scene.Elements.Single(e => e.Id == p); } return false; }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!dragging || startScene is null || ViewModel is not { } vm) return; var p = Local(e.GetPosition(this));
        if (OnScreen) { Native.GetCursorPos(out var pointer); p = new((pointer.X - gestureOrigin.X) / gestureScale, (pointer.Y - gestureOrigin.Y) / gestureScale); }
        var delta = new Vector2((float)(p.X - start.X), (float)(p.Y - start.Y));
        moveCount++;
        vm.Run(() => vm.Store.Execute(resizing ? "크기 변경" : "요소 이동", d =>
        {
            var scene = d.Scenes.Single(s => s.Id == startScene.Id);
            foreach (var id in dragIds)
            {
                var original = startScene.Elements.Single(e => e.Id == id); var item = scene.Elements.Single(e => e.Id == id); var originalFrame = SceneMath.Flatten(startScene).Single(f => f.Element.Id == id); if (originalFrame.Locked) continue;
                Matrix3x2 parent = SceneMath.ParentTransform(startScene, original); Matrix3x2.Invert(parent, out var inverse); var local = Vector2.TransformNormal(delta, inverse);
                double Snap(double v) => vm.GridSnap ? Math.Round(v / 8) * 8 : v;
                if (resizing)
                {
                    if (original.Type == ElementKind.Group)
                    {
                        var b = originalFrame.Bounds;
                        double projected = (delta.X * b.Width + delta.Y * b.Height) / Math.Max(1, b.Width * b.Width + b.Height * b.Height);
                        item.Scale = Math.Clamp(original.Scale * (1 + projected), .05, 20);
                    }
                    else { Matrix3x2.Invert(originalFrame.Transform, out var inv); var sizeDelta = Vector2.TransformNormal(delta, inv); item.Width = Math.Clamp(Snap(original.Width + sizeDelta.X), 8, 4096); item.Height = Math.Clamp(Snap(original.Height + sizeDelta.Y), 8, 4096); }
                }
                else { item.X = Snap(original.X + local.X); item.Y = Snap(original.Y + local.Y); }
            }
        })); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); EndDrag(false); e.Handled = true; }
    void EndDrag(bool cancel)
    {
        if (!dragging || ViewModel is not { } vm) return; dragging = false; vm.PointerGestureActive = false; ReleaseMouseCapture();
        vm.Log.Write("editor_gesture_end", new { OnScreen, cancel, moveCount, captured = Mouse.Captured?.GetType().Name, foreground = Native.GetForegroundWindow().ToInt64() });
        if (ownTransaction) { if (cancel) vm.Store.CancelTransaction(); else vm.Store.CommitTransaction(); }
        else if (cancel && startDocument is not null) vm.Store.RestorePreview(startDocument);
        startDocument = null; startScene = null;
        InvalidateVisual();
    }
    internal bool CancelGesture() { if (!dragging) return false; EndDrag(true); return true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); if (dragging) EndDrag(true); }
    protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);
    sealed class SurfacePeer(EditorSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.OnScreen ? "화면 요소 편집 · Esc 취소" : "Scene Canvas · 요소 선택 및 이동";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(EditorSurface);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (ViewModel is not { } vm) return;
        if (e.Key == Key.Escape && dragging) { EndDrag(true); e.Handled = true; }
        else if (e.Key == Key.Delete && vm.DeleteElementCommand.CanExecute(null)) { vm.DeleteElementCommand.Execute(null); e.Handled = true; }
    }
}
