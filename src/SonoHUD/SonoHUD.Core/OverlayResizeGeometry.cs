namespace SonoHUD.Core;

public enum OverlayResizeCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public readonly record struct OverlayResizeResult(DesktopPoint Origin, double Width, double Height, double Scale);

public static class OverlayResizeGeometry
{
    public const double MinimumScale = .75;
    public const double MaximumScale = 1.5;

    // All pointer/origin/work-area coordinates are physical pixels; returned dimensions are DIPs.
    public static OverlayResizeResult Calculate(DesktopPoint origin, double initialScale, double baseWidth, double baseHeight,
        DesktopPoint startPointer, DesktopPoint pointer, OverlayResizeCorner corner, DesktopMonitor monitor)
    {
        if (!Enum.IsDefined(corner) || new[] { origin.X, origin.Y, initialScale, baseWidth, baseHeight, startPointer.X,
                startPointer.Y, pointer.X, pointer.Y, monitor.Scale, monitor.Work.X, monitor.Work.Y, monitor.Work.Width, monitor.Work.Height }.Any(v => !double.IsFinite(v)) ||
            initialScale is < MinimumScale or > MaximumScale || baseWidth <= 0 || baseHeight <= 0 || monitor.Scale <= 0 || monitor.Work.Width <= 0 || monitor.Work.Height <= 0)
            throw new ArgumentException("크기 조정의 좌표와 배율을 확인하세요.");

        bool left = corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.BottomLeft;
        bool top = corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.TopRight;
        double unitWidth = baseWidth * monitor.Scale, unitHeight = baseHeight * monitor.Scale;
        double initialWidth = unitWidth * initialScale, initialHeight = unitHeight * initialScale;
        var anchor = new DesktopPoint(origin.X + (left ? initialWidth : 0), origin.Y + (top ? initialHeight : 0));
        double dx = (pointer.X - startPointer.X) * (left ? -1 : 1);
        double dy = (pointer.Y - startPointer.Y) * (top ? -1 : 1);
        double requested = initialScale + (dx * unitWidth + dy * unitHeight) / (unitWidth * unitWidth + unitHeight * unitHeight);

        // Preserve an existing boundary-spanning placement without allowing further offscreen growth.
        // The opposite corner remains fixed even when the pointer crosses to another monitor.
        double allowedLeft = Math.Min(monitor.Work.X, origin.X);
        double allowedTop = Math.Min(monitor.Work.Y, origin.Y);
        double allowedRight = Math.Max(monitor.Work.X + monitor.Work.Width, origin.X + initialWidth);
        double allowedBottom = Math.Max(monitor.Work.Y + monitor.Work.Height, origin.Y + initialHeight);
        double roomWidth = left ? anchor.X - allowedLeft : allowedRight - anchor.X;
        double roomHeight = top ? anchor.Y - allowedTop : allowedBottom - anchor.Y;
        double largest = Math.Max(MinimumScale, Math.Min(MaximumScale, Math.Min(roomWidth / unitWidth, roomHeight / unitHeight)));
        double scale = Math.Clamp(requested, MinimumScale, largest);
        double width = baseWidth * scale, height = baseHeight * scale;
        return new(new(anchor.X - (left ? width * monitor.Scale : 0), anchor.Y - (top ? height * monitor.Scale : 0)), width, height, scale);
    }
}
