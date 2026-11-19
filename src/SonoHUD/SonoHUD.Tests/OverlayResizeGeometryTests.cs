using SonoHUD.Core;

internal static class OverlayResizeGeometryTests
{
    static readonly DesktopMonitor Secondary = new("secondary", new(-1920, -1080, 1920, 1080), new(-1920, -1080, 1920, 1040), 1.25);
    static void Assert(bool condition, string message = "resize geometry mismatch") { if (!condition) throw new Exception(message); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .00001;
    static bool Left(OverlayResizeCorner corner) => corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.BottomLeft;
    static bool Top(OverlayResizeCorner corner) => corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.TopRight;
    static DesktopPoint Handle(DesktopPoint origin, double width, double height, OverlayResizeCorner corner) => new(origin.X + (Left(corner) ? 0 : width), origin.Y + (Top(corner) ? 0 : height));
    static DesktopPoint Anchor(DesktopPoint origin, double width, double height, OverlayResizeCorner corner) => new(origin.X + (Left(corner) ? width : 0), origin.Y + (Top(corner) ? height : 0));
    static void SamePoint(DesktopPoint a, DesktopPoint b) => Assert(Near(a.X, b.X) && Near(a.Y, b.Y), $"point mismatch: {a} != {b}");

    public static void Run(Action<string, Action> check)
    {
        check("resize all four corners keep the opposite anchor and aspect ratio", () =>
        {
            var origin = new DesktopPoint(-1600, -900);
            foreach (var corner in Enum.GetValues<OverlayResizeCorner>())
            {
                var start = Handle(origin, 575, 225, corner);
                var pointer = new DesktopPoint(start.X + (Left(corner) ? -115 : 115), start.Y + (Top(corner) ? -45 : 45));
                var result = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, pointer, corner, Secondary);
                Assert(Near(result.Scale, 1.2) && Near(result.Width, 552) && Near(result.Height, 216));
                SamePoint(Anchor(result.Origin, result.Width * 1.25, result.Height * 1.25, corner), Anchor(origin, 575, 225, corner));
            }
        });
        check("resize minimum prevents inversion when dragging past the fixed corner", () =>
        {
            var origin = new DesktopPoint(-1600, -900);
            foreach (var corner in Enum.GetValues<OverlayResizeCorner>())
            {
                var start = Handle(origin, 575, 225, corner);
                var pointer = new DesktopPoint(start.X + (Left(corner) ? 2000 : -2000), start.Y + (Top(corner) ? 1500 : -1500));
                var result = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, pointer, corner, Secondary);
                Assert(Near(result.Scale, .75) && result.Width > 0 && result.Height > 0);
                SamePoint(Anchor(result.Origin, result.Width * 1.25, result.Height * 1.25, corner), Anchor(origin, 575, 225, corner));
            }
        });
        check("resize maximum remains 150 percent on a large display", () =>
        {
            var monitor = new DesktopMonitor("large", new(-5000, -4000, 10000, 8000), new(-5000, -4000, 10000, 8000), 2);
            foreach (var corner in Enum.GetValues<OverlayResizeCorner>())
            {
                var origin = new DesktopPoint(-500, -300); var start = Handle(origin, 920, 360, corner);
                var pointer = new DesktopPoint(start.X + (Left(corner) ? -4000 : 4000), start.Y + (Top(corner) ? -3000 : 3000));
                var result = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, pointer, corner, monitor);
                Assert(Near(result.Scale, 1.5));
                SamePoint(Anchor(result.Origin, result.Width * 2, result.Height * 2, corner), Anchor(origin, 920, 360, corner));
            }
        });
        check("resize crossing a monitor boundary retains the initial DPI and work-area limit", () =>
        {
            var origin = new DesktopPoint(-600, -500); var start = new DesktopPoint(-25, -275);
            var result = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, new DesktopPoint(3000, 500), OverlayResizeCorner.BottomRight, Secondary);
            SamePoint(result.Origin, origin);
            Assert(Near(result.Origin.X + result.Width * Secondary.Scale, 0));
            Assert(Near(result.Scale, 600d / 575) && Near(result.Width / result.Height, 460d / 180));
        });
        check("resize preserves an existing boundary-spanning placement without further outward growth", () =>
        {
            var origin = new DesktopPoint(-2020, -900); var start = origin;
            var result = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, new DesktopPoint(-2200, -1000), OverlayResizeCorner.TopLeft, Secondary);
            SamePoint(result.Origin, origin); Assert(Near(result.Scale, 1));
            var smaller = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, new DesktopPoint(-1905, -855), OverlayResizeCorner.TopLeft, Secondary);
            Assert(Near(smaller.Scale, .8) && smaller.Origin.X > -1920);
            SamePoint(Anchor(smaller.Origin, smaller.Width * 1.25, smaller.Height * 1.25, OverlayResizeCorner.TopLeft), new(-1445, -675));
        });
        check("resize starts without a jump for every preset scale and corner even with a handle grab offset", () =>
        {
            foreach (var preset in Enum.GetValues<PresetKind>())
            foreach (var scale in new[] { .75, 1, 1.5 })
            foreach (var corner in Enum.GetValues<OverlayResizeCorner>())
            {
                var size = OverlayPresets.Dimensions(preset); var origin = new DesktopPoint(-1500, -880);
                var handle = Handle(origin, size.Width * scale * 1.25, size.Height * scale * 1.25, corner);
                var grabbed = new DesktopPoint(handle.X + 4, handle.Y - 3);
                var result = OverlayResizeGeometry.Calculate(origin, scale, size.Width, size.Height, grabbed, grabbed, corner, Secondary);
                SamePoint(result.Origin, origin);
                Assert(Near(result.Scale, scale) && Near(result.Width, size.Width * scale) && Near(result.Height, size.Height * scale));
            }
        });
        check("resize repeated samples use the original snapshot instead of accumulating scale drift", () =>
        {
            var origin = new DesktopPoint(-1600, -900); var start = new DesktopPoint(-1025, -675);
            OverlayResizeResult result = default;
            for (int i = 1; i <= 100; i++) result = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start,
                new DesktopPoint(start.X + 115 * i / 100d, start.Y + 45 * i / 100d), OverlayResizeCorner.BottomRight, Secondary);
            Assert(Near(result.Scale, 1.2));
            var restored = OverlayResizeGeometry.Calculate(origin, 1, 460, 180, start, start, OverlayResizeCorner.BottomRight, Secondary);
            Assert(Near(restored.Scale, 1)); SamePoint(restored.Origin, origin);
        });
        check("resize keeps the minimum readable scale when a monitor is smaller than the overlay", () =>
        {
            var monitor = new DesktopMonitor("small", new(0, 0, 200, 100), new(0, 0, 200, 100), 1);
            var result = OverlayResizeGeometry.Calculate(new(0, 0), 1, 380, 96, new(380, 96), new(0, 0), OverlayResizeCorner.BottomRight, monitor);
            Assert(Near(result.Scale, .75) && Near(result.Width, 285) && Near(result.Height, 72));
        });
        check("resize rejects nonfinite or invalid geometry before returning a placement", () =>
        {
            void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("invalid resize accepted"); }
            Reject(() => OverlayResizeGeometry.Calculate(new(0, 0), 1, 460, 180, new(0, 0), new(double.NaN, 4), OverlayResizeCorner.BottomRight, Secondary));
            Reject(() => OverlayResizeGeometry.Calculate(new(0, 0), .5, 460, 180, new(0, 0), new(1, 1), OverlayResizeCorner.BottomRight, Secondary));
            Reject(() => OverlayResizeGeometry.Calculate(new(0, 0), 1, 0, 180, new(0, 0), new(1, 1), OverlayResizeCorner.BottomRight, Secondary));
            Reject(() => OverlayResizeGeometry.Calculate(new(0, 0), 1, 460, 180, new(0, 0), new(1, 1), (OverlayResizeCorner)99, Secondary));
        });
    }
}
