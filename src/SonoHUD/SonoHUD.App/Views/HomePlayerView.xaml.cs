using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SonoHUD.App.ViewModels;

namespace SonoHUD.App.Views;

public partial class HomePlayerView : UserControl
{
    internal bool SpectrumIntersects(FrameworkElement viewport) => SonoHUD.App.Rendering.SpectrumVisibility.Intersects(FrequencySpectrum, viewport);
    internal void SetSpectrum(ReadOnlySpan<float> values, SonoHUD.Core.SpectrumStyle style, Brush ink,ReadOnlySpan<float> peaks=default) => FrequencySpectrum.SetSpectrum(values,style==SonoHUD.Core.SpectrumStyle.Preset?SonoHUD.Core.SpectrumStyle.Bars:style,ink,peaks);
    StudioViewModel? Player => DataContext as StudioViewModel;
    double spectrumHeight;
    bool shortViewport;
    public HomePlayerView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => {shortViewport=e.NewSize.Height<660;UpdateLayoutDensity(e.NewSize.Width);};
        OnlineLyricsPanel.LayoutPolicyChanged+=()=>UpdateLayoutDensity(ActualWidth);
        IsVisibleChanged += (_, _) => { if (!IsVisible) Player?.CancelSeek(); };
        Unloaded += (_, _) => Player?.CancelSeek();
    }
    internal void SetViewportHeight(double height)
    {
        bool value=height<700;
        if(shortViewport==value)return;
        shortViewport=value;UpdateLayoutDensity(ActualWidth);
    }
    void UpdateLayoutDensity(double width)
    {
        if (width <= 0) return;
        bool compact = width < 1000;
        double artworkWidth = shortViewport ? 220 : compact ? 280 : Math.Clamp((width - 18) * .34, 320, 380);
        if(Player?.HomeSpectrumVisible==true)artworkWidth=Math.Min(artworkWidth,shortViewport?220:compact?260:320);
        bool reading=Player is { HasLyricsProvider: true, LyricsLayout: true };
        double height=ActualHeight>0?ActualHeight:700;
        artworkWidth=Math.Clamp(height*(reading?.23:.31),96,artworkWidth);
        double target=reading?Math.Clamp(height*.047,18,40):Math.Clamp(height*.25,64,192);
        if(Math.Abs(target-spectrumHeight)>.5){
            spectrumHeight=target;
            var from=FrequencySpectrum.ActualHeight;FrequencySpectrum.BeginAnimation(HeightProperty,null);FrequencySpectrum.Height=target;
            if(from>0 && IsVisible && Player?.AnimationsEnabled==true && SystemParameters.ClientAreaAnimation)
                FrequencySpectrum.BeginAnimation(HeightProperty,new System.Windows.Media.Animation.DoubleAnimation(from,target,TimeSpan.FromMilliseconds(240)){FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});
        }
        double artworkHeight = artworkWidth;
        ArtworkColumn.Width = new GridLength(artworkWidth);
        ArtworkFrame.Width = artworkWidth;
        ArtworkFrame.Height = artworkHeight;
        ArtworkFrame.Clip = new RectangleGeometry(new Rect(0, 0, artworkWidth, artworkHeight), 6, 6);
        ArtworkGap.Width = new GridLength(compact ? 24 : 32);
        bool smallSpectrum=shortViewport&&Player?.HomeSpectrumVisible==true;
        PlaybackSurface.Padding=new Thickness(shortViewport?(smallSpectrum?12:16):24);
        TimelineArea.Margin=new Thickness(12,smallSpectrum?12:20,12,0);
        FrequencyPanel.Margin=new Thickness(12,smallSpectrum?12:20,12,0);
        HomeTitle.FontSize = shortViewport ? 24 : compact ? 26 : 30;
        HomeTitle.LineHeight = shortViewport ? 30 : compact ? 34 : 38;
        HomeTitle.MaxHeight = HomeTitle.LineHeight * 2;
        PlaybackButtons.Margin = new Thickness(12,0,0,0);PlaybackButtons.VerticalAlignment=VerticalAlignment.Center;
        SeekHelpSlot.Visibility=FrequencyLabels.Visibility=TimelineRuler.Visibility=height<590?Visibility.Collapsed:Visibility.Visible;
        if(shortViewport){PlaybackSurface.Padding=new Thickness(12,6,12,6);TimelineArea.Margin=new Thickness(8,4,8,0);FrequencyPanel.Margin=new Thickness(8,4,8,0);}
    }
    // The parent receives the tunnel before Slider's class handler changes Value for track clicks.
    void SeekMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!FromSeekSlider(e.OriginalSource)) return;
        SeekSlider.Focus();
        if (Player?.BeginSeek() != true) e.Handled = true;
    }
    async void SeekMouseUp(object sender, MouseButtonEventArgs e) { if (FromSeekSlider(e.OriginalSource) && Player is { } vm) await vm.CommitSeekAsync(); }
    bool FromSeekSlider(object source) => source is DependencyObject target && (ReferenceEquals(target, SeekSlider) || SeekSlider.IsAncestorOf(target));
    void SeekKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Player?.CancelSeek(); e.Handled = true; }
        else if (IsSeekKey(e.Key))
        {
            var vm = Player;
            if (vm is null || !vm.CanSeek || (!e.IsRepeat && !vm.BeginSeek())) e.Handled = true;
        }
    }
    async void SeekKeyUp(object sender, KeyEventArgs e) { if (IsSeekKey(e.Key) && Player is { } vm) await vm.CommitSeekAsync(); }
    void SeekLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Player?.CancelSeek();
    static bool IsSeekKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
}
