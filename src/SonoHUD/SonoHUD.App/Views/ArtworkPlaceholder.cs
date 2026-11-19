using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SonoHUD.App.Views;

public sealed class ArtworkPlaceholder : FrameworkElement
{
    public static readonly DependencyProperty InkProperty=DependencyProperty.Register("Ink",typeof(Brush),typeof(ArtworkPlaceholder),new FrameworkPropertyMetadata(DesignResources.TextTertiary,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WellProperty=DependencyProperty.Register("Well",typeof(Brush),typeof(ArtworkPlaceholder),new FrameworkPropertyMetadata(DesignResources.Surface,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EdgeProperty=DependencyProperty.Register("Edge",typeof(Brush),typeof(ArtworkPlaceholder),new FrameworkPropertyMetadata(DesignResources.Border,FrameworkPropertyMetadataOptions.AffectsRender));
    public ArtworkPlaceholder() { SetResourceReference(InkProperty,"StudioTertiary");SetResourceReference(WellProperty,"StudioPanel");SetResourceReference(EdgeProperty,"StudioLine"); IsHitTestVisible = false; Focusable = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        dc.DrawRectangle((Brush)GetValue(WellProperty), new Pen((Brush)GetValue(EdgeProperty), 1), new Rect(0, 0, ActualWidth, ActualHeight));
        var center = new Point(ActualWidth / 2, ActualHeight / 2 - (size >= 120 ? 10 : 0));
        dc.DrawEllipse(null, new Pen((Brush)GetValue(EdgeProperty), Math.Max(1, size / 180)), center, size * .27, size * .27);
        dc.DrawEllipse(null, new Pen((Brush)GetValue(InkProperty), Math.Max(1, size / 160)), center, size * .18, size * .18);
        dc.DrawEllipse((Brush)GetValue(InkProperty), null, center, Math.Max(2, size * .024), Math.Max(2, size * .024));
        if (size >= 120)
        {
            var label = new FormattedText("앨범아트 없음", CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                new Typeface(DesignResources.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11,
                (Brush)GetValue(InkProperty), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point((ActualWidth - label.Width) / 2, ActualHeight - 30));
        }
    }
}
