using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

// Presentation only: input, focus and the page state never wait for motion.
public static class DesignMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DesignMotion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    internal static void RevealArtwork(FrameworkElement cover,UIElement edge)
    {
        var glint=new DoubleAnimationUsingKeyFrames{FillBehavior=FillBehavior.Stop};
        glint.KeyFrames.Add(new LinearDoubleKeyFrame(0,TimeSpan.Zero));
        glint.KeyFrames.Add(new LinearDoubleKeyFrame(.9,TimeSpan.FromMilliseconds(180)));
        glint.KeyFrames.Add(new LinearDoubleKeyFrame(0,TimeSpan.FromMilliseconds(780)));
        edge.BeginAnimation(UIElement.OpacityProperty,glint);
        var scale=new ScaleTransform(1,1);cover.RenderTransformOrigin=new(.5,.5);cover.RenderTransform=scale;
        var reveal=new DoubleAnimation(.94,1,TimeSpan.FromMilliseconds(400)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,reveal);scale.BeginAnimation(ScaleTransform.ScaleYProperty,reveal);
    }
    internal static void StopArtwork(FrameworkElement cover,UIElement edge)
    {
        edge.BeginAnimation(UIElement.OpacityProperty,null);
        if(cover.RenderTransform is ScaleTransform scale){
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            cover.RenderTransform=Transform.Identity;
        }
    }
    internal static void Transition(UIElement element, AnimationProfile profile)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1;
        if (profile == AnimationProfile.Off || !SystemParameters.ClientAreaAnimation) return;
        var animation = new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(DesignTokens.MotionPageMilliseconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
