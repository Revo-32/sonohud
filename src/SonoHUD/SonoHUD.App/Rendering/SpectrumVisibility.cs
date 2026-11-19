using System.Windows;

namespace SonoHUD.App.Rendering;

internal static class SpectrumVisibility
{
    // IsVisible alone does not account for ScrollViewer clipping. Do not spend the
    // shared 60 Hz budget drawing cards below the viewport.
    internal static bool Intersects(FrameworkElement view, FrameworkElement viewport)
    {
        return view.IsVisible && viewport.IsVisible && IntersectsBounds(view, viewport);
    }
    internal static bool IntersectsBounds(FrameworkElement view, FrameworkElement viewport)
    {
        if(view.ActualWidth <= 0 || view.ActualHeight <= 0 || !viewport.IsAncestorOf(view))return false;
        var bounds = view.TransformToAncestor(viewport).TransformBounds(new Rect(view.RenderSize));
        return Rect.Intersect(bounds, new Rect(viewport.RenderSize)) is { IsEmpty: false, Width: > 0, Height: > 0 };
    }
}
