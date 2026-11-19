using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SonoHUD.App.Views;

public enum IconKind { Home, Overlay, Audio, Settings, Undo, Redo, Play, Pause, Previous, Next, ChevronDown, Check, Close, Music, Monitor, Minimize, Maximize, Restore }

// SonoHUD's own 24-unit vector drawings; no icon font or external asset is used.
public sealed class IconGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(IconGlyph), new FrameworkPropertyMetadata(IconKind.Music, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(IconGlyph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(IconGlyph), new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender));
    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    static readonly Dictionary<IconKind, Geometry> Shapes = new()
    {
        [IconKind.Home] = Shape("M3,11 L12,4 21,11 M6,10 L6,20 10,20 10,14 14,14 14,20 18,20 18,10"),
        [IconKind.Overlay] = Shape("M3,4 L18,4 18,16 3,16 Z M7,19 L21,19 21,8"),
        [IconKind.Audio] = Shape("M5,4 L5,8 M5,12 L5,20 M12,4 L12,14 M12,18 L12,20 M19,4 L19,6 M19,10 L19,20 M2.5,8 L7.5,8 7.5,12 2.5,12 Z M9.5,14 L14.5,14 14.5,18 9.5,18 Z M16.5,6 L21.5,6 21.5,10 16.5,10 Z"),
        [IconKind.Undo] = Shape("M4,5 L4,11 10,11 M4,11 C7,5 17,5 19,12 C21,18 15,21 10,19"),
        [IconKind.Redo] = Shape("M20,5 L20,11 14,11 M20,11 C17,5 7,5 5,12 C3,18 9,21 14,19"),
        [IconKind.Play] = Shape("M7,4 L20,12 7,20 Z"),
        [IconKind.Pause] = Shape("M6,4 L10,4 10,20 6,20 Z M14,4 L18,4 18,20 14,20 Z"),
        [IconKind.Previous] = Shape("M5,5 L7,5 7,19 5,19 Z M19,5 L9,12 19,19 Z"),
        [IconKind.Next] = Shape("M17,5 L19,5 19,19 17,19 Z M5,5 L15,12 5,19 Z"),
        [IconKind.ChevronDown] = Shape("M6,9 L12,15 18,9"),
        [IconKind.Check] = Shape("M5,12 L10,17 20,7"),
        [IconKind.Close] = Shape("M6,6 L18,18 M18,6 L6,18"),
        [IconKind.Minimize] = Shape("M4,16 L20,16"),
        [IconKind.Maximize] = Shape("M5,5 L19,5 19,19 5,19 Z"),
        [IconKind.Restore] = Shape("M8,5 L19,5 19,16 M5,8 L16,8 16,19 5,19 Z"),
        [IconKind.Monitor] = Shape("M3,4 L21,4 21,17 3,17 Z M12,17 L12,21 M8,21 L16,21")
    };
    static Geometry Shape(string data) { var shape = Geometry.Parse(data); shape.Freeze(); return shape; }

    public IconGlyph() { Width = Height = 20; Focusable = false; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0 || Foreground is null) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Foreground, Math.Max(.1, StrokeThickness) / scale) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Miter };
        if (Kind == IconKind.Settings)
        {
            dc.DrawEllipse(null, pen, new Point(12, 12), 6.7, 6.7);
            dc.DrawEllipse(null, pen, new Point(12, 12), 2.2, 2.2);
            for (int i = 0; i < 8; i++) { double angle = i * Math.PI / 4; dc.DrawLine(pen, new Point(12 + Math.Cos(angle) * 7, 12 + Math.Sin(angle) * 7), new Point(12 + Math.Cos(angle) * 9.3, 12 + Math.Sin(angle) * 9.3)); }
        }
        else if (Kind == IconKind.Music)
        {
            dc.DrawLine(pen, new Point(9, 17), new Point(9, 5)); dc.DrawLine(pen, new Point(9, 5), new Point(19, 3)); dc.DrawLine(pen, new Point(19, 3), new Point(19, 15));
            dc.DrawEllipse(Foreground, null, new Point(6.5, 17), 2.8, 2); dc.DrawEllipse(Foreground, null, new Point(16.5, 15), 2.8, 2);
        }
        else if (Shapes.TryGetValue(Kind, out var geometry))
        {
            bool filled = Kind is IconKind.Play or IconKind.Pause or IconKind.Previous or IconKind.Next;
            dc.DrawGeometry(filled ? Foreground : null, filled ? null : pen, geometry);
        }
        dc.Pop(); dc.Pop();
    }
}
