using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SonoHUD.Core;
using SonoHUD.App.Rendering;
using SonoHUD.App.ViewModels;

namespace SonoHUD.App.Views;

public partial class AppearanceView : UserControl
{
    StudioViewModel? Model=>DataContext as StudioViewModel;
    readonly List<(Button Tile,TextBlock Caption,MusicTheme Theme)> tiles=[];
    public AppearanceView()
    {
        InitializeComponent();
        foreach(string value in new[]{"#95DCBA","#92C5ED","#C7A9DE","#DEA766","#D7D7D5","#CE837E","#344F65","#51505C"}){
            var swatch=new Button{Width=32,Height=32,MinHeight=0,Background=ElementDrawing.Brush(value),ToolTip=value,Margin=new(0,0,4,0)};
            swatch.Click+=(_,_)=>Hex.Text=value;Swatches.Children.Add(swatch);
        }
        foreach(var theme in MusicTheme.Gallery){
            var preview=new StackPanel();
            var caption=new TextBlock{Foreground=ElementDrawing.Brush(theme.Ink),FontWeight=FontWeights.SemiBold};
            preview.Children.Add(caption);
            var meter=new StackPanel{Orientation=Orientation.Horizontal,Height=19,Margin=new(0,6,0,0)};
            int bandIndex=0;
            foreach(int h in new[]{5,9,14,11,17,12,8,6,11,7,4})meter.Children.Add(new Border{Width=4,Height=h,Margin=new(0,0,2,0),VerticalAlignment=VerticalAlignment.Bottom,Background=theme.SpectrumGradient?PrismPaint.Band(bandIndex++,11):ElementDrawing.Brush(theme.Spectrum)});
            preview.Children.Add(meter);
            preview.Children.Add(new Border{Height=2,Width=44,HorizontalAlignment=HorizontalAlignment.Left,
                Margin=new(0,5,0,0),Background=theme.IsPrism?PrismPaint.Spectrum:ElementDrawing.Brush(theme.Progress)});
            var tile=new Button{Content=preview,Margin=new(0,0,8,6),Padding=new(14,9,14,9),HorizontalContentAlignment=HorizontalAlignment.Left,Background=ElementDrawing.Brush(theme.Surface)};
            System.Windows.Automation.AutomationProperties.SetName(tile,theme.Name);
            tile.Click+=(_,_)=>Model?.SetAppearance(Model.Appearance with{Theme=theme.Name});
            Gallery.Children.Add(tile);tiles.Add((tile,caption,theme));caption.Text=theme.Name;
        }
    }
    internal void Refresh(MusicTheme active)
    {
        foreach(var (tile,caption,theme) in tiles){
            bool selected=theme.Name==active.Name;
            caption.Text=(selected?"✓  ":"")+theme.Name;
            tile.BorderBrush=selected?ElementDrawing.Brush(active.Accent):Brushes.Transparent;
        }
        Art.IsChecked=Model?.Appearance.ArtworkAccent;
        AccentButton.Content="강조색  "+active.Accent;
        SpectrumButton.Content="스펙트럼  "+active.Spectrum;
    }
    bool editingSpectrum;
    void Pick(bool spectrum)
    {
        if(Model is not {} vm)return;
        editingSpectrum=spectrum;Hex.Text=spectrum?vm.Palette.Spectrum:vm.Palette.Accent;
        PickerTitle.Text=spectrum?"스펙트럼 색상":"강조색";
        ColorHint.Text="밝기는 읽기 쉬운 범위로 자동 조정됩니다.";Picker.Visibility=Visibility.Visible;
    }
    void ApplyColor(object s,RoutedEventArgs e)
    {
        if(Model is not {} vm)return;
        string color=Hex.Text.Trim().ToUpperInvariant();
        if(!MusicTheme.IsRgb(color)){ColorHint.Text="#RRGGBB 형식으로 입력하세요.";return;}
        vm.SetAppearance(editingSpectrum?vm.Appearance with{Spectrum=color}:vm.Appearance with{Accent=color,ArtworkAccent=false});
        Picker.Visibility=Visibility.Collapsed;
    }
    void UseThemeColor(object sender,RoutedEventArgs e)
    {
        if(Model is not {} vm)return;
        vm.SetAppearance(editingSpectrum ? vm.Appearance with { Spectrum=null } : vm.Appearance with { Accent=null, ArtworkAccent=false });
        Picker.Visibility=Visibility.Collapsed;
    }
    void ClosePicker(object s,RoutedEventArgs e)=>Picker.Visibility=Visibility.Collapsed;
    void AccentClick(object s,RoutedEventArgs e)=>Pick(false);
    void SpectrumClick(object s,RoutedEventArgs e)=>Pick(true);
    void ArtworkClick(object s,RoutedEventArgs e){if(Model is {} vm)vm.SetAppearance(vm.Appearance with{ArtworkAccent=Art.IsChecked==true});}
    void Reset(object s,RoutedEventArgs e)=>Model?.SetAppearance(new());
}