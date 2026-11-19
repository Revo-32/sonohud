using System.Numerics;
using SonoHUD.Core;

internal static class DesktopLayoutTests
{
    static readonly DesktopMonitor[] Monitors =
    [
        new("main", new(0, 0, 3440, 1440), new(0, 0, 3440, 1400), 1),
        new("left", new(-2560, 0, 2560, 1440), new(-2560, 0, 2560, 1400), 1.25),
        new("top", new(400, -1920, 1080, 1920), new(400, -1920, 1080, 1880), 1.5)
    ];
    static void Assert(bool value, string message = "desktop geometry mismatch") { if (!value) throw new Exception(message); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .02;
    static void Point(DesktopPoint actual, double x, double y) => Assert(Near(actual.X, x) && Near(actual.Y, y), $"point: {actual}, expected ({x}, {y})");
    static void Rectangle(Bounds actual, double x, double y, double width, double height) => Assert(Near(actual.X, x) && Near(actual.Y, y) && Near(actual.Width, width) && Near(actual.Height, height), $"bounds: {actual}");

    public static void Run(Action<string, Action> check)
    {
        check("desktop union includes negative monitor origins", () =>
        {
            Rectangle(DesktopLayout.Union(Monitors.Select(m => m.Bounds)), -2560, -1920, 6000, 3360);
            Assert(DesktopLayout.Nearest(new(-1, 300), Monitors).Id == "left");
            Assert(DesktopLayout.Nearest(new(0, 300), Monitors).Id == "main");
            Assert(DesktopLayout.Nearest(new(700, -1), Monitors).Id == "top");
        });
        check("monitor relative DIPs resolve to physical pixels at mixed DPI", () =>
        {
            var scene = new Scene { MonitorId = "left", X = 80, Y = 64 };
            Point(DesktopLayout.Origin(scene, DesktopLayout.Resolve(scene, Monitors)), -2460, 80);
            scene.MonitorId = "top"; Point(DesktopLayout.Origin(scene, DesktopLayout.Resolve(scene, Monitors)), 520, -1824);
            scene.MonitorId = "disconnected"; Assert(DesktopLayout.Resolve(scene, Monitors).Id == "main");
        });
        check("scene move keeps grabbed local point anchored across DPI transitions", () =>
        {
            var scene = new Scene { MonitorId = "main", X = 100, Y = 200 };
            var origin = DesktopLayout.Origin(scene, Monitors[0]); var startPointer = new DesktopPoint(180, 240);
            foreach (var target in new[] { new DesktopPoint(-1000, 500), new DesktopPoint(900, -1300), new DesktopPoint(0, 300), new DesktopPoint(700, -1) })
            {
                DesktopLayout.Move(scene, origin, startPointer, target, 1, Monitors);
                var monitor = DesktopLayout.Resolve(scene, Monitors); var moved = DesktopLayout.Origin(scene, monitor);
                Point(new(moved.X + 80 * monitor.Scale, moved.Y + 40 * monitor.Scale), target.X, target.Y);
            }
            DesktopLayout.Move(scene, origin, startPointer, startPointer, 1, Monitors);
            Assert(scene.MonitorId == "main" && Near(scene.X, 100) && Near(scene.Y, 200));
        });
        check("scene boundary spanning positions are not clamped to one monitor", () =>
        {
            var scene = new Scene { MonitorId = "main", X = -100, Y = 100 };
            Point(DesktopLayout.VisibleOrigin(scene, Monitors), -100, 100);
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), -100, 100, 560, 240);
            scene.X = 3300; Point(DesktopLayout.VisibleOrigin(scene, Monitors), 3300, 100);
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), 3300, 100, 140, 240);
        });
        check("unreachable scene is recovered to work area without mutating saved coordinates", () =>
        {
            var scene = new Scene { MonitorId = "main", X = 100000, Y = 100000 };
            Point(DesktopLayout.VisibleOrigin(scene, Monitors), 2880, 1160);
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), 2880, 1160, 560, 240);
            Assert(scene.X == 100000 && scene.Y == 100000);
            scene.X = 2000; scene.Y = -1000; // Inside the virtual bounding box, outside every physical display.
            var recovered = DesktopLayout.VisibleOrigin(scene, Monitors);
            Assert(Monitors.Any(m => DesktopLayout.Intersects(new(recovered.X, recovered.Y, scene.Width, scene.Height), m.Bounds)));
        });
        check("visible elements preserve placement when the base scene is offscreen", () =>
        {
            var scene = new Scene { MonitorId = "main", X = 5000, Y = 100, Elements = [new() { Type = ElementKind.Text, X = -2000, Width = 180, Height = 40 }] };
            Point(DesktopLayout.VisibleOrigin(scene, Monitors), 5000, 100);
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), 3000, 100, 440, 240);
            scene.Elements[0].Visible = false;
            Point(DesktopLayout.VisibleOrigin(scene, Monitors), 2880, 100);
        });
        check("window content expands for visible elements outside base scene", () =>
        {
            var scene = new Scene { MonitorId = "main", X = 0, Y = 100, Elements =
                [new() { Type = ElementKind.Text, X = -1200, Width = 200, Height = 40 }, new() { Type = ElementKind.Text, X = 3000, Width = 180, Height = 40 }, new() { Type = ElementKind.Text, X = -100000, Visible = false }] };
            Rectangle(DesktopLayout.Content(scene), -1200, 0, 4380, 240);
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), -1200, 100, 4380, 240);
            scene.Elements[0].X = -5000;
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), -2560, 100, 5740, 240);
        });
        check("content bounds use transformed group children and skip group frame", () =>
        {
            var group = new SceneElement { Type = ElementKind.Group, X = -1600, Y = 200, Width = 10000, Height = 10000, Scale = 1.25, Rotation = 90 };
            var child = new SceneElement { Type = ElementKind.Text, ParentGroup = group.Id, X = 32, Y = 16, Width = 100, Height = 40 };
            var scene = new Scene { MonitorId = "main", X = 100, Y = 100, Elements = [group, child] };
            SceneValidation.Check(new() { Scenes = [scene] });
            Rectangle(DesktopLayout.Content(scene), -1670, 0, 2230, 365);
            Rectangle(DesktopLayout.WindowBounds(scene, Monitors), -1570, 100, 2230, 365);
            group.Visible = false; Rectangle(DesktopLayout.Content(scene), 0, 0, 560, 240);
        });
        check("offscreen elements do not fake visibility through the desktop bounding box", () =>
        {
            var scene = new Scene { MonitorId = "main", X = 5000, Y = -1000, Elements =
                [new() { Type = ElementKind.Text, X = -9000, Width = 100, Height = 40 }] };
            var recovered = DesktopLayout.VisibleOrigin(scene, Monitors);
            Assert(!Near(recovered.X, scene.X) || !Near(recovered.Y, scene.Y));
        });
        check("cross monitor scene move preserves group local transform through undo redo and JSON", () =>
        {
            var scene = SceneFactory.Music("main", new()); var store = new SceneStore(new() { Scenes = [scene] });
            var ids = scene.Elements.Skip(1).Take(2).Select(e => e.Id).ToArray(); var group = store.GroupElements(scene.Id, ids);
            var original = SceneJson.Serialize(store.Document);
            var matrices = SceneMath.Flatten(store.Scene(scene.Id)).ToDictionary(f => f.Element.Id, f => f.Transform);
            var origin = DesktopLayout.Origin(store.Scene(scene.Id), Monitors[0]); var pointer = new DesktopPoint(origin.X + 80, origin.Y + 40);
            store.Execute("cross monitor", d => DesktopLayout.Move(d.Scenes[0], origin, pointer, new(-1600, 600), 1, Monitors));
            string moved = SceneJson.Serialize(store.Document);
            Assert(SceneMath.Flatten(store.Scene(scene.Id)).All(f => matrices[f.Element.Id] == f.Transform));
            store.Undo(); Assert(SceneJson.Serialize(store.Document) == original);
            store.Redo(); Assert(SceneJson.Serialize(store.Document) == moved);
            var restored = SceneJson.Parse(moved); Assert(restored.Scenes[0].MonitorId == "left");
            var movedOrigin = DesktopLayout.Origin(restored.Scenes[0], Monitors[1]);
            Point(new(movedOrigin.X + 80 * 1.25, movedOrigin.Y + 40 * 1.25), -1600, 600);
            store.UngroupElements(scene.Id, group);
            foreach (var id in ids)
            {
                Matrix3x2 matrix = SceneMath.Flatten(store.Scene(scene.Id)).Single(f => f.Element.Id == id).Transform;
                Assert(Near(matrix.M31, matrices[id].M31) && Near(matrix.M32, matrices[id].M32));
            }
        });
    }
}
