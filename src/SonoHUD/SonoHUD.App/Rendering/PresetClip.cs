using System.Numerics;
using System.Windows;
using System.Windows.Media;
using SonoHUD.Core;

namespace SonoHUD.App.Rendering;

// Both renderers use the original preset box, then the exact same root/device transform.
// No painted border or opacity layer is introduced by this geometry.
internal readonly record struct PresetClipLayout(Vector2 Size, float Radius, Matrix3x2 Transform)
{
    internal RectangleGeometry WpfGeometry()
    {
        var m = Transform;
        var geometry = new RectangleGeometry(new Rect(0, 0, Size.X, Size.Y), Radius, Radius)
        { Transform = new MatrixTransform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32) };
        geometry.Freeze();
        return geometry;
    }

    internal bool Contains(Vector2 point)
    {
        if (!Matrix3x2.Invert(Transform, out var inverse)) return false;
        var local = Vector2.Transform(point, inverse);
        if (local.X < 0 || local.Y < 0 || local.X > Size.X || local.Y > Size.Y) return false;
        float x = Math.Clamp(local.X, Radius, Size.X - Radius), y = Math.Clamp(local.Y, Radius, Size.Y - Radius);
        return Vector2.DistanceSquared(local, new(x, y)) <= Radius * Radius;
    }
}

internal static class PresetClip
{
    internal static bool TryCreate(Scene scene, double deviceScale, Vector2 sceneOffset, out PresetClipLayout layout)
    {
        layout = default;
        if (scene.UserSettings.GetValueOrDefault("format") != "SonoHUD.Overlay" ||
            !Enum.TryParse<PresetKind>(scene.UserSettings.GetValueOrDefault("preset"), out var preset) || !Enum.IsDefined(preset)) return false;
        Guid rootId = OverlayPresets.ElementId(scene.Id, "root");
        var root = scene.Elements.FirstOrDefault(e => e.Id == rootId && e.Type == ElementKind.Group && e.ParentGroup is null);
        if (root is null) return false;
        var dimensions = OverlayPresets.Dimensions(preset);
        float radius = (float)Math.Min(DesignTokens.PanelRadius, Math.Min(dimensions.Width, dimensions.Height) / 2);
        layout = new(new((float)dimensions.Width, (float)dimensions.Height), radius,
            SceneMath.Local(root) * Matrix3x2.CreateScale((float)deviceScale) * Matrix3x2.CreateTranslation(sceneOffset));
        return true;
    }

    internal static bool Contains(Scene scene, double x, double y) =>
        !TryCreate(scene, 1, Vector2.Zero, out var layout) || layout.Contains(new((float)x, (float)y));
}
