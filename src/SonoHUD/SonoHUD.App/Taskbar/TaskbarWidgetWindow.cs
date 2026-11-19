// Modified 2026-11-19: neutral widget surface, animated Prism edge and multicolor frequency bars.
// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later
// Modified for SonoHUD 2026-11-15. Adapted from TaskbarWidgetControl.xaml(.cs)
// and TaskbarWindow.xaml.cs at 2d742fa4fc90a228803d90db2902740463113e77.
// Retains compact sizing, art/info/control ordering, clipped single-line title,
// context-menu sessions, and child-window placement. Uses SonoHUD media/commands,
// bundled fonts and marquee; excludes flyouts, volume, premium, capture and branding.
// Modified 2026-11-16: A Rail presentation, consistent insets and scoped transport
// styles; existing media commands, configuration and native placement retained.
// Modified 2026-11-17: shared frequency spectrum in spare rail space; edge fade
// and end gutters for title marquee. Native parent and media commands unchanged.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SonoHUD.App.Media;
using SonoHUD.App.Platform;
using SonoHUD.App.Rendering;
using SonoHUD.App.ViewModels;
using SonoHUD.App.Views;
using SonoHUD.Core;

namespace SonoHUD.App.Taskbar;

internal sealed class TaskbarWidgetWindow : Window
{
    readonly StudioViewModel vm;
    readonly Border border=new();
    readonly Grid row=new();
    readonly Border lightRail=new(){Height=2,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(6,0,6,0),IsHitTestVisible=false};
    readonly Image art=new(){Stretch=Stretch.Uniform};
    readonly Border artFrame=new(){CornerRadius=new(4),Background=new SolidColorBrush(Color.FromRgb(41,41,40)),ClipToBounds=true};
    readonly ArtworkPlaceholder artPlaceholder=new();
    readonly TextBlock title=new(){FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White};
    readonly TextBlock artist=new(){FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(184,184,184)),TextTrimming=TextTrimming.CharacterEllipsis};
    readonly Canvas titleClip=new(){Height=17,ClipToBounds=true};
    readonly StackPanel info=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly StackPanel controls=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
    readonly Button previous,play,next;
    readonly TextBlock stateMark=new(){FontSize=10,VerticalAlignment=VerticalAlignment.Center,Margin=new(3,0,6,0),Foreground=Brushes.White};
    readonly TranslateTransform titleTranslation=new();
    readonly SpectrumView spectrum=new(){Width=44,Height=24,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,BandCount=12};
    double spectrumAvailableWidth;
    HwndSource? source;
    string mediaKey="";
    TaskbarConfig config=new();
    bool compact, disposed;
    internal nint Handle {get;private set;}
    internal int MediaRenderCount {get;private set;}
    internal event Action? SettingsRequested;
    internal event Action? PlacementInvalidated;
    internal TaskbarWidgetWindow(StudioViewModel viewModel)
    {
        vm=viewModel;Title="SonoHUD 작업 표시줄 위젯";ShowActivated=false;ShowInTaskbar=false;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
        FontFamily=ElementDrawing.ResolveFontFamily("Wanted Sans");Focusable=false;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/SonoHUD;component/Views/TaskbarWidgetTheme.xaml",UriKind.Relative) });
        UseLayoutRounding=true;SnapsToDevicePixels=true;
        Width=320;Height=40;Left=-20000;Top=-20000;
        AutomationProperties.SetAutomationId(this,"SonoHUDTaskbarWidget");
        AutomationProperties.SetAutomationId(title,"TaskbarTrackTitle");AutomationProperties.SetAutomationId(artist,"TaskbarArtist");
        title.RenderTransform=titleTranslation;titleClip.Children.Add(title);info.Children.Add(titleClip);info.Children.Add(artist);
        artist.LineHeight=14;artist.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;
        var cover=new Grid();cover.Children.Add(artPlaceholder);cover.Children.Add(art);artFrame.Child=cover;
        Button Control(IconKind kind,string name,string id,System.Windows.Input.ICommand command)
        {
            var b=new Button{Style=(Style)FindResource("TaskbarTransport"),Content=new IconGlyph{Kind=kind,Width=14,Height=14},ToolTip=name,Command=command,Width=30,Height=30};
            AutomationProperties.SetName(b,name);AutomationProperties.SetAutomationId(b,id);controls.Children.Add(b);return b;
        }
        previous=Control(IconKind.Previous,"이전 곡","TaskbarPrevious",vm.PreviousCommand);
        play=Control(IconKind.Play,"재생","TaskbarPlayPause",vm.PlayCommand);
        play.Style=(Style)FindResource("TaskbarPrimaryTransport");
        next=Control(IconKind.Next,"다음 곡","TaskbarNext",vm.NextCommand);
        border.Child=row;border.CornerRadius=new(5);border.Padding=new(8,4,8,4);var panel=new Grid();panel.Children.Add(border);panel.Children.Add(lightRail);Content=panel;
        titleClip.SizeChanged+=(_,_)=>StartTitle();
        ContextMenu=new ContextMenu{Background=new SolidColorBrush(Color.FromRgb(20,20,20)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(60,60,60)),FontFamily=FontFamily};
        ContextMenu.Resources[SystemColors.MenuBrushKey]=ContextMenu.Background;
        ContextMenu.Resources[SystemColors.MenuTextBrushKey]=Brushes.White;
        ContextMenu.Resources[SystemColors.MenuHighlightBrushKey]=new SolidColorBrush(Color.FromRgb(55,55,55));
        ContextMenu.Resources[SystemColors.HighlightTextBrushKey]=Brushes.White;
        ContextMenu.Opened+=(_,_)=>RefreshMenu();
        SourceInitialized+=(_,_)=> {Handle=new WindowInteropHelper(this).Handle;source=HwndSource.FromHwnd(Handle);source.AddHook(Message);};
        IsVisibleChanged+=(_,_)=> {ApplyAppearance();if(IsVisible)StartTitle();else StopTitle();};
        SystemParameters.StaticPropertyChanged+=MotionChanged;
        Closed+=(_,_)=> {disposed=true;SystemParameters.StaticPropertyChanged-=MotionChanged;StopTitle();source?.RemoveHook(Message);};
    }
    void MotionChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)){ApplyAppearance();StartTitle();}}
    nint Message(nint h,int message,nint wp,nint lp,ref bool handled)
    {
        if(message==0x21){handled=true;return 3;} // MA_NOACTIVATE; click still delivered.
        if(message is 0x2E0 or 0x2E3 or 0x7E)Dispatcher.BeginInvoke(()=>PlacementInvalidated?.Invoke());
        return 0;
    }
    internal void Configure(TaskbarConfig value,TaskbarSnapshot snapshot,Rect rect)
    {
        bool small=value.Compact||(snapshot.Vertical?snapshot.Bounds.Width:snapshot.Bounds.Height)/(snapshot.Dpi/96d)<40||(snapshot.Vertical?rect.Height:rect.Width)/(snapshot.Dpi/96d)<240;
        config=value with{};compact=small;
        double scale=snapshot.Dpi/96d;
        Width=rect.Width/scale;Height=rect.Height/scale;
        spectrumAvailableWidth=snapshot.Vertical?Height:Width;
        spectrum.Height=small?18:24;ConfigureSpectrum(vm.SpectrumOptions);
        lightRail.Width=snapshot.Vertical?2:double.NaN;lightRail.Height=snapshot.Vertical?double.NaN:2;
        lightRail.HorizontalAlignment=snapshot.Vertical?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        lightRail.VerticalAlignment=snapshot.Vertical?VerticalAlignment.Stretch:VerticalAlignment.Bottom;
        lightRail.Margin=snapshot.Vertical?new Thickness(0,6,0,6):new Thickness(6,0,6,0);
        row.LayoutTransform=snapshot.Vertical?new RotateTransform(90):Transform.Identity;
        row.ColumnDefinitions.Clear();row.Children.Clear();
        art.Width=art.Height=small?24:32;
        artFrame.Width=artFrame.Height=art.Width;
        artFrame.Clip=new RectangleGeometry(new Rect(0,0,art.Width,art.Height),4,4);
        double edge=small?6:8;
        double crossInset=Math.Max(0,Math.Min(4,((snapshot.Vertical?Width:Height)-art.Height)/2));
        border.Padding=snapshot.Vertical?new(crossInset,edge,crossInset,edge):new(edge,crossInset,edge,crossInset);
        artFrame.Margin=new(0,0,8,0);info.Margin=new(0,0,value.ShowControls&&!value.ControlsLeft?8:0,0);
        controls.Margin=value.ControlsLeft?new(0,0,8,0):new(0);
        artist.Visibility=value.ShowArtist&&(!small||!value.ShowTitle)?Visibility.Visible:Visibility.Collapsed;
        titleClip.Visibility=value.ShowTitle?Visibility.Visible:Visibility.Collapsed;
        foreach(var b in new[]{previous,play,next})b.Width=b.Height=small?24:30;
        var order=new List<FrameworkElement>();
        if(value.ControlsLeft&&value.ShowControls)order.Add(controls);
        if(value.ShowArtwork)order.Add(artFrame);
        if(!value.ShowControls)order.Add(stateMark);
        order.Add(info);
        order.Add(spectrum);
        if(!value.ControlsLeft&&value.ShowControls)order.Add(controls);
        foreach(var child in order){int col=row.ColumnDefinitions.Count;row.ColumnDefinitions.Add(new ColumnDefinition{Width=child==info?new GridLength(1,GridUnitType.Star):GridLength.Auto});Grid.SetColumn(child,col);row.Children.Add(child);}
        ApplyAppearance(true);
        border.BorderThickness=new(0);
        UpdateMedia(true);UpdateLayout();StartTitle();
    }
    internal double DesiredWidth(TaskbarConfig value)
    {
        if(!value.AutoWidth)return value.Width;
        var state=vm.Media.State;string text=value.ShowTitle?state.Title:value.ShowArtist?state.Artist:"";
        if(string.IsNullOrWhiteSpace(text))text="재생 중인 음악 없음";
        var measured=new FormattedText(TitleMarquee.SingleLine(text),System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),12,Brushes.White,1);
        return Math.Clamp(Math.Min(220,Math.Max(90,measured.Width+10))+(value.ShowArtwork?40:0)+(value.ShowControls?96:16)+18,160,520);
    }
    internal void ConfigureSpectrum(SpectrumSettings settings)
    {
        // Preserve the existing width request and HWND placement. The visualizer
        // borrows spare space only after artwork, readable text and transport fit.
        double required=160+(config.ShowArtwork?40:0)+(config.ShowControls?90:0);
        spectrum.Visibility=settings.Enabled && settings.Taskbar && spectrumAvailableWidth>=required?Visibility.Visible:Visibility.Collapsed;
    }
    internal void SetSpectrum(ReadOnlySpan<float> values,Brush ink,ReadOnlySpan<float> peaks=default)
    { if(spectrum.Visibility==Visibility.Visible)spectrum.SetSpectrum(values,SpectrumStyle.Bars,vm.Palette.SpectrumGradient?PrismPaint.Spectrum:ink,peaks); }
    MusicTheme? currentTheme;
    bool prismMotion;
    void ApplyAppearance(bool force=false)
    {
        var p=vm.Palette;bool motion=p.IsPrism && IsVisible && vm.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
        if(!force && currentTheme==p && prismMotion==motion)return;
        currentTheme=p;prismMotion=motion;
        StudioAppearance.Apply(Resources,p);var color=(Color)ColorConverter.ConvertFromString(p.Surface);
        if(config.Style!=TaskbarStyle.Solid)color.A=240;
        lightRail.Background=p.IsPrism?PrismPaint.Ribbon(motion):null;
        border.Background=new SolidColorBrush(color);title.Foreground=stateMark.Foreground=ElementDrawing.Brush(p.Ink);
        artist.Foreground=ElementDrawing.Brush(p.Secondary);artFrame.Background=ElementDrawing.Brush(p.Raised);
        if(ContextMenu is {} menu){menu.Background=ElementDrawing.Brush(p.Surface);menu.Foreground=title.Foreground;menu.BorderBrush=ElementDrawing.Brush(p.Border);
        menu.Resources[SystemColors.MenuTextBrushKey]=title.Foreground;menu.Resources[SystemColors.MenuBrushKey]=menu.Background;
        menu.Resources[SystemColors.MenuHighlightBrushKey]=ElementDrawing.Brush(p.Raised);menu.Resources[SystemColors.HighlightTextBrushKey]=title.Foreground;}
    }
    internal void UpdateMedia(bool force=false)
    {
        ApplyAppearance();
        var s=vm.Media.State;
        string key=string.Join('\0',s.Source,s.Title,s.Artist,s.ArtworkKey,s.IsDemo,s.IsPlaying,s.CanToggle,s.CanPrevious,s.CanNext,vm.Media.Busy,s.CommandStatus);
        if(!force&&key==mediaKey)return;mediaKey=key;MediaRenderCount++;
        title.Text=TitleMarquee.SingleLine(s.Source.Length==0?"재생 중인 음악 없음":string.IsNullOrWhiteSpace(s.Title)?"곡명 미제공":s.Title);
        artist.Text=s.IsDemo?"샘플 · "+s.Artist:s.Artist;
        art.Source=ElementDrawing.Artwork(s);
        artPlaceholder.Visibility=art.Source is null?Visibility.Visible:Visibility.Collapsed;
        ((IconGlyph)play.Content).Kind=s.IsPlaying?IconKind.Pause:IconKind.Play;play.ToolTip=s.IsPlaying?"일시정지":"재생";AutomationProperties.SetName(play,(string)play.ToolTip);
        stateMark.Text=s.Source.Length==0?"—":s.IsPlaying?"▶":"Ⅱ";
        border.ToolTip=string.Join('\n',new[]{s.IsDemo?"샘플 미리보기":title.Text,s.Artist,vm.MediaStatus}.Where(x=>x.Length>0));
        StartTitle();
    }
    void StopTitle(){titleTranslation.BeginAnimation(TranslateTransform.XProperty,null);titleTranslation.X=0;}
    void StartTitle()
    {
        if(disposed)return;StopTitle();
        double width=Math.Max(0,info.ActualWidth);titleClip.Width=width;
        title.Width=double.NaN;
        title.Measure(new Size(double.PositiveInfinity,20));
        bool animate=config.ScrollTitle&&SystemParameters.ClientAreaAnimation&&IsVisible&&config.ShowTitle;
        var layout=new TitleMarqueeLayout(title.Text,title.DesiredSize.Width+(title.DesiredSize.Width>width?20:0),width,17,animate);
        Canvas.SetLeft(title,layout.CanAnimate?10:0);
        titleClip.OpacityMask=layout.CanAnimate?TitleMarquee.EdgeMask(width):null;
        title.Width=layout.CanAnimate?double.NaN:width;
        title.TextTrimming=layout.CanAnimate?TextTrimming.None:TextTrimming.CharacterEllipsis;
        if(layout.CanAnimate)TitleMarquee.Start(titleTranslation,layout);
    }
    internal void RefreshMenu()
    {
        ContextMenu.Items.Clear();
        var sessions=new MenuItem{Header="음악 소스"};
        foreach(var session in vm.MediaSessions)
        {
            string key=session.Key;var item=new MenuItem{Header=session.Label,IsCheckable=true,IsChecked=vm.SelectedMedia==key,IsEnabled=!vm.IsPositioning};
            item.Click+=(_,_)=>vm.SelectedMedia=key;sessions.Items.Add(item);
        }
        ContextMenu.Items.Add(sessions);ContextMenu.Items.Add(new Separator());
        var settings=new MenuItem{Header="작업 표시줄 위젯 설정"};settings.Click+=(_,_)=>SettingsRequested?.Invoke();ContextMenu.Items.Add(settings);
        var disable=new MenuItem{Header="작업 표시줄 위젯 끄기",IsEnabled=!vm.IsPositioning};disable.Click+=(_,_)=>vm.TaskbarEnabled=false;ContextMenu.Items.Add(disable);
    }
    internal void Attach(TaskbarSnapshot snapshot,Rect rect)
    {
        if(Handle==0)new WindowInteropHelper(this).EnsureHandle();
        if(!TaskbarShell.AreDpiAwarenessContextsEqual(TaskbarShell.GetWindowDpiAwarenessContext(Handle),TaskbarShell.GetWindowDpiAwarenessContext(snapshot.Handle)))
            throw new InvalidOperationException("작업 표시줄과 화면 배율 방식이 달라 연결하지 않았습니다. 기본 Windows 작업 표시줄에서 다시 시도하세요.");
        long style=Native.GetWindowLongPtrW(Handle,TaskbarShell.GwlStyle).ToInt64();
        Native.SetWindowLongPtrW(Handle,TaskbarShell.GwlStyle,new((style&~TaskbarShell.Popup)|TaskbarShell.Child));
        long ex=Native.GetWindowLongPtrW(Handle,TaskbarShell.GwlExStyle).ToInt64();
        Native.SetWindowLongPtrW(Handle,TaskbarShell.GwlExStyle,new((ex|TaskbarShell.NoActivate|TaskbarShell.ToolWindow)&~0x40000L));
        TaskbarShell.SetParent(Handle,snapshot.Handle);
        if(TaskbarShell.GetParent(Handle)!=snapshot.Handle)throw new InvalidOperationException("작업 표시줄에 연결하지 못했습니다. 다른 위젯과 권한 상태를 확인하고 다시 연결하세요.");
        Show();Place(snapshot,rect);
    }
    internal void Place(TaskbarSnapshot snapshot,Rect rect)
    {
        if(!Native.IsWindow(snapshot.Handle)||TaskbarShell.GetParent(Handle)!=snapshot.Handle)throw new InvalidOperationException("작업 표시줄 연결이 끊겼습니다.");
        var point=new Native.Point{X=(int)Math.Round(rect.Left),Y=(int)Math.Round(rect.Top)};
        if(!TaskbarShell.ScreenToClient(snapshot.Handle,ref point)||!Native.SetWindowPos(Handle,0,point.X,point.Y,(int)Math.Round(rect.Width),(int)Math.Round(rect.Height),0x10|0x40|0x4000))
            throw new InvalidOperationException("위젯 위치를 반영하지 못했습니다. 다시 연결하세요.");
    }
    internal void Release()
    {
        if(disposed)return;Hide();StopTitle();
        if(Handle!=0&&Native.IsWindow(Handle))
        {
            TaskbarShell.SetParent(Handle,0);
            long style=Native.GetWindowLongPtrW(Handle,TaskbarShell.GwlStyle).ToInt64();
            Native.SetWindowLongPtrW(Handle,TaskbarShell.GwlStyle,new((style&~TaskbarShell.Child)|TaskbarShell.Popup));
        }
        Close();
    }
}
