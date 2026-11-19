namespace SonoHUD.Core;

// Saved coordinates are monitor-relative DIPs; all desktop geometry is physical pixels.
public readonly record struct DesktopPoint(double X, double Y);
public sealed record DesktopMonitor(string Id, Bounds Bounds, Bounds Work, double Scale);
public static class DesktopLayout
{
    public static Bounds Union(IEnumerable<Bounds> values)
    {
        var all = values.ToArray(); if (all.Length == 0) return new(0, 0, 1, 1);
        double x = all.Min(b => b.X), y = all.Min(b => b.Y);
        return new(x, y, all.Max(b => b.X + b.Width) - x, all.Max(b => b.Y + b.Height) - y);
    }
    public static bool Intersects(Bounds a, Bounds b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    public static DesktopMonitor Resolve(Scene scene, IReadOnlyList<DesktopMonitor> monitors) => monitors.FirstOrDefault(m => m.Id == scene.MonitorId) ?? monitors[0];
    public static DesktopMonitor Nearest(DesktopPoint point, IReadOnlyList<DesktopMonitor> monitors) => monitors.MinBy(m => Distance(point, m.Bounds))!;
    static double Distance(DesktopPoint p, Bounds r) { double x = p.X - Math.Clamp(p.X, r.X, r.X + r.Width - 1), y = p.Y - Math.Clamp(p.Y, r.Y, r.Y + r.Height - 1); return x * x + y * y; }
    public static DesktopPoint Origin(Scene scene, DesktopMonitor monitor) => new(monitor.Bounds.X + scene.X * monitor.Scale, monitor.Bounds.Y + scene.Y * monitor.Scale);
    public static Bounds Content(Scene scene) => Union(new[] { new Bounds(0, 0, scene.Width, scene.Height) }.Concat(SceneMath.Flatten(scene).Where(f => f.Visible && f.Element.Type != ElementKind.Group).Select(f => f.Bounds)));
    public static DesktopPoint VisibleOrigin(Scene scene, IReadOnlyList<DesktopMonitor> monitors)
    {
        var m = Resolve(scene, monitors); var p = Origin(scene, m);
        // Keep boundary-spanning placements, including an offscreen base with visible children.
        // Test individual rectangles: their union can cross a display even if no content does.
        var baseRect = new Bounds(p.X, p.Y, scene.Width * m.Scale, scene.Height * m.Scale);
        var rectangles = new[] { baseRect }.Concat(SceneMath.Flatten(scene)
            .Where(f => f.Visible && f.Element.Type != ElementKind.Group)
            .Select(f => new Bounds(p.X + f.Bounds.X * m.Scale, p.Y + f.Bounds.Y * m.Scale, f.Bounds.Width * m.Scale, f.Bounds.Height * m.Scale)));
        if (rectangles.Any(r => monitors.Any(v => Intersects(r, v.Bounds)))) return p;
        var nearest = Nearest(p, monitors);
        return new(Math.Clamp(p.X, nearest.Work.X, Math.Max(nearest.Work.X, nearest.Work.X + nearest.Work.Width - baseRect.Width)), Math.Clamp(p.Y, nearest.Work.Y, Math.Max(nearest.Work.Y, nearest.Work.Y + nearest.Work.Height - baseRect.Height)));
    }
    public static Bounds WindowBounds(Scene scene, IReadOnlyList<DesktopMonitor> monitors)
    {
        var m = Resolve(scene, monitors); var p = VisibleOrigin(scene, monitors); var c = Content(scene); var desktop = Union(monitors.Select(v => v.Bounds));
        double left = Math.Max(desktop.X, Math.Floor(p.X + c.X * m.Scale)), top = Math.Max(desktop.Y, Math.Floor(p.Y + c.Y * m.Scale));
        double right = Math.Min(desktop.X + desktop.Width, Math.Ceiling(p.X + (c.X + c.Width) * m.Scale)), bottom = Math.Min(desktop.Y + desktop.Height, Math.Ceiling(p.Y + (c.Y + c.Height) * m.Scale));
        return new(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
    public static void Move(Scene scene, DesktopPoint startOrigin, DesktopPoint startPointer, DesktopPoint pointer, double startScale, IReadOnlyList<DesktopMonitor> monitors)
    {
        var target = Nearest(pointer, monitors);
        // Preserve the grabbed local DIP under the pointer, including when the destination DPI changes.
        double localX = (startPointer.X - startOrigin.X) / startScale, localY = (startPointer.Y - startOrigin.Y) / startScale;
        scene.MonitorId = target.Id;
        scene.X = (pointer.X - localX * target.Scale - target.Bounds.X) / target.Scale;
        scene.Y = (pointer.Y - localY * target.Scale - target.Bounds.Y) / target.Scale;
    }
}
