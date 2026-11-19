using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SonoHUD.App.Media;
using SonoHUD.App.Platform;
using SonoHUD.App.Rendering;
using SonoHUD.App.ViewModels;
using SonoHUD.App.Views;
using SonoHUD.Core;
using SonoHUD.App.Taskbar;

namespace SonoHUD.App;

public partial class StudioWindow : Window
{
    readonly StudioViewModel vm;
    readonly CompositionRuntime runtime = null!;
    readonly bool renderOnly;
    readonly TaskbarRuntime? taskbar;
    readonly OverlayPreview livePreview = new() { PreserveUserScale = true };
    readonly Dictionary<PresetKind, (Button Button, OverlayPreview Preview, TextBlock Label)> gallery = [];
    readonly DispatcherTimer? diagnostics;
    readonly DispatcherTimer audioTimer = new();
    readonly SpectrumCoordinator spectrumCoordinator=new();
    readonly Dictionary<string,int> displayRates=new(StringComparer.OrdinalIgnoreCase);
    Brush spectrumInk=Brushes.LightGray;
    SpectrumSettings renderSpectrumOptions=new();
    OverlayPositionWindow? positionWindow;
    IntPtr hwnd; HwndSource? source;
    bool closing, canClose, audioBusy;
    string lastPage = "Home";
    bool wasGallery;
    (bool Enabled, int Key)? hotkey;
    internal StudioWindow(StudioViewModel viewModel, bool automation, bool secondary, bool renderOnly = false)
    {
        this.renderOnly = renderOnly;
        vm = viewModel; InitializeComponent(); DataContext = vm; ApplyAppearance();
        LivePreviewHost.Content = livePreview;
        foreach (var preset in Enum.GetValues<PresetKind>())
        {
            var preview = new OverlayPreview { Height = 200, IsHitTestVisible = false };
            var label = new TextBlock { FontSize = 11, Foreground = (Brush)FindResource("StudioMuted"), Margin = new(0,6,0,0), Text = " " };
            string presetName = OverlayPresets.DisplayName(preset);
            var stack = new StackPanel(); stack.Children.Add(preview); stack.Children.Add(new TextBlock { Text = presetName, FontWeight = FontWeights.SemiBold, Margin = new(0,8,0,0) }); stack.Children.Add(label);
            var b = new Button { Content = stack, Padding = new(12), Margin = new(4,0,4,10), HorizontalContentAlignment = HorizontalAlignment.Stretch, Command = vm.PresetCommand, CommandParameter = preset.ToString() };
            AutomationProperties.SetAutomationId(b, "Preset" + preset); AutomationProperties.SetName(b, presetName + (OverlayPresets.ShowsLyrics(preset) ? " 현재 곡 가사 디자인" : preset == PresetKind.MusicCard ? " 기본 추천 디자인" : " 디자인"));
            PresetGallery.Children.Add(b); gallery[preset] = (b,preview,label);
        }
        NavigationButtons.SizeChanged+=(_,_)=>MoveNavBeam(false);
        DeckFrame.SizeChanged += (_,e) => { bool compact=e.NewSize.Height<700; foreach(var item in gallery.Values) item.Preview.Height=compact?104:200; DeckFrame.Padding=new Thickness(compact?12:20,44,compact?12:20,14); };
        PageScroll.ScrollChanged += (_, e) =>
        {
            if(e.ViewportHeightChange!=0)HomePlayer.SetViewportHeight(PageScroll.ViewportHeight);
            if(!renderOnly && hwnd!=IntPtr.Zero)RefreshSpectrum();
        };
        // Snapshot tests only measure and draw Content. No HWND, renderer, timers, hotkeys or input handlers.
        if (renderOnly) { ShowActivated = false; RefreshRenderOnly(); return; }
        runtime = new(vm.Log); runtime.CommandRequested += MediaCommand;
        vm.Lyrics.FrameChanged += RefreshLyrics;
        HomePlayer.OnlineLyricsPanel.OptionsPanel.IsVisibleChanged += (_,_) => RefreshLyricsVisibility();
        HomePlayer.OnlineLyricsPanel.IsVisibleChanged += (_,_) => RefreshLyricsVisibility();
        taskbar=new(vm);taskbar.SettingsRequested+=OpenTaskbarSettings;
        spectrumCoordinator.Frame+=PaintSpectrum;spectrumCoordinator.StatusChanged+=SetSpectrumStatus;
        vm.Changed += RefreshVisuals; vm.PropertyChanged += VmPropertyChanged; vm.PositionRequested += BeginPosition; vm.PositionEndRequested += EndPosition; vm.SettingsChanged += RegisterHotkey; vm.MotionReplayRequested += ReplayMotion;
        ShowActivated = !secondary;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd); source.AddHook(WindowMessage);
            vm.Log.Write("window_chrome",WindowChromeTheme.Apply(hwnd,IsActive,vm.Palette));
            var monitors = Native.Monitors(); var m = monitors.Where(x => !x.Primary).OrderByDescending(x => x.Work.Width).FirstOrDefault() ?? monitors.First();
            int width = Math.Min(1280, m.Work.Width - 32), height = Math.Min(940, m.Work.Height - 32);
            Native.SetWindowPos(hwnd, IntPtr.Zero, m.Work.Left + Math.Max(8,(m.Work.Width-width)/2), m.Work.Top+Math.Max(8,(m.Work.Height-height)/2), width,height,0x14);
            RegisterHotkey(); RefreshVisuals(); UpdateSelection();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && vm.IsPositioning) { EndPosition(false); e.Handled=true; } else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { _ = vm.SaveAsync(); e.Handled = true; } };
        Activated += (_,_)=>WindowChromeTheme.Apply(hwnd,true,vm.Palette);
        Deactivated += (_,_)=>WindowChromeTheme.Apply(hwnd,false,vm.Palette);
        StateChanged += (_,_) => { ApplyAppearance(); if(vm.OverlayVisible && WindowState!=WindowState.Minimized)RefreshPreviewPanels(vm.RenderScene()); UpdateCaptionControls(); if (WindowState == WindowState.Minimized && positionWindow is not null) EndPosition(false); RefreshSpectrum(); };
        IsVisibleChanged += (_,_) => { ApplyAppearance(); RefreshSpectrum(); };
        SystemParameters.StaticPropertyChanged += SystemMotionChanged;
        Closing += CloseAsync;
        if (automation) { diagnostics = new(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, (_,_) => Diagnostics(), Dispatcher); diagnostics.Start(); }
        audioTimer = new(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_,_) => await RefreshAudio(), Dispatcher); audioTimer.Start();
        Loaded += async (_,_) => { await vm.SaveAsync(false); await RefreshAudio(); vm.SetSpectrumOutputs(await Task.Run(SpectrumCapture.Outputs)); };
    }
    internal void RefreshRenderOnly()
    {
        if (!renderOnly) throw new InvalidOperationException("오프스크린 전용 인스턴스가 아닙니다.");
        ApplyAppearance();
        livePreview.SetContent(vm.RenderScene(), vm.LyricsPreviewPlayback); LivePreviewHost.Height=OverlayPresets.Dimensions(vm.EffectivePreset).Height>=300?480:240;
        foreach (var (preset,item) in gallery)
        {
            if (!vm.PresetAvailable(preset)) continue;
            var config=vm.Document.Overlay with { Preset = preset, Scale = 1 };
            SetGalleryPreview(item.Preview,config,vm.LyricsPreviewPlayback);
        }
        UpdateSelection();
    }
    void ReleaseInfoExpanded(object sender, RoutedEventArgs e)
    {
        var assembly = typeof(StudioWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3);
        ReleaseVersion.Text = $"SonoHUD {version} · GPL-3.0-or-later";
        if (ReleaseDocument.SelectedIndex < 0) ReleaseDocument.SelectedIndex = 0;
    }

    void ReleaseDocumentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseDocumentText is null) return;
        string? file = ReleaseDocument.SelectedIndex switch
        {
            0 => "LICENSE",
            1 => "THIRD-PARTY-NOTICES.md",
            2 => "개인정보와-보안.md",
            _ => null
        };
        if (file is null) return;
        try
        {
            ReleaseDocumentText.Text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));
            ReleaseDocumentText.ScrollToHome();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReleaseDocumentText.Text = $"{file} 파일을 읽을 수 없습니다. 배포 ZIP을 새 폴더에 다시 풀어 주세요.";
        }
    }

    void MinimizeWindow(object sender, RoutedEventArgs e) { if(!renderOnly) SystemCommands.MinimizeWindow(this); }
    void OpenTaskbarSettings() { vm.NavigateCommand.Execute("Settings");Show();WindowState=WindowState.Normal;Activate(); }
    void ToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if(renderOnly)return;
        if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);
    }
    void CloseWindow(object sender, RoutedEventArgs e) { if(!renderOnly) SystemCommands.CloseWindow(this); }
    void UpdateCaptionControls()
    {
        bool maximized=WindowState==WindowState.Maximized;
        MaximizeGlyph.Kind=maximized?IconKind.Restore:IconKind.Maximize;
        MaximizeButton.ToolTip=maximized?"이전 크기로":"최대화";
        AutomationProperties.SetName(MaximizeButton,maximized?"창 이전 크기로":"창 최대화");
        DeckFrame.Margin=new Thickness(maximized?6:12);
    }
    void VmPropertyChanged(object? s, PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName)) { if(lastPage != vm.Page) { lastPage=vm.Page; PageScroll.ScrollToTop(); if(vm.OverlayVisible && !renderOnly && hwnd!=0)RefreshPreviewPanels(vm.RenderScene()); DesignMotion.Transition(PageContent,vm.Document.Overlay.Animation); } if(vm.OverlayVisible && vm.DesignGalleryVisible && !wasGallery && hwnd!=0)RefreshPreviewPanels(vm.RenderScene());wasGallery=vm.OverlayVisible && vm.DesignGalleryVisible; if(!vm.AnimationsEnabled)DesignMotion.Transition(PageContent,AnimationProfile.Off); UpdateSelection(); RefreshSpectrum(); } }
    void UpdateSelection()
    {
        void Selected(Button b,bool selected)
        {
            bool navigation = b == NavHome || b == NavOverlay || b == NavAudio || b == NavSettings;
            b.Background = navigation && !selected ? Brushes.Transparent : (Brush)FindResource(selected ? "StudioSelected" : "StudioPanel");
            b.BorderBrush = navigation || !selected ? Brushes.Transparent : (Brush)FindResource("StudioMuted");
            if(navigation)b.Foreground=(Brush)FindResource(selected ? "StudioInk" : "StudioMuted");
            b.Tag=selected; AutomationProperties.SetItemStatus(b,selected ? "선택됨" : "선택 안 됨");
        }
        MoveNavBeam(prismMotion);
        Selected(NavHome,vm.HomeVisible); Selected(NavOverlay,vm.OverlayVisible); Selected(NavAudio,vm.AudioVisible); Selected(NavSettings,vm.SettingsVisible);
        // Overlay controls receive the latest state on entry to their page.
        if (!vm.OverlayVisible && !renderOnly) return;
        Selected(ViewDesigns,vm.DesignGalleryVisible); Selected(CustomizeDesign,vm.CustomizationVisible); Selected(TabLayout,vm.LayoutVisible); Selected(TabAppearance,vm.AppearanceVisible); Selected(TabAnimation,vm.AnimationVisible);

        Selected(MotionOff,vm.Document.Overlay.Animation == AnimationProfile.Off); Selected(MotionCalm,vm.Document.Overlay.Animation == AnimationProfile.Calm); Selected(MotionDynamic,vm.Document.Overlay.Animation == AnimationProfile.Dynamic); Selected(MotionCinematic,vm.Document.Overlay.Animation == AnimationProfile.Cinematic);
        foreach (var (preset,item) in gallery)
        {
            item.Button.Visibility = vm.PresetAvailable(preset) ? Visibility.Visible : Visibility.Collapsed;
            if (item.Button.Visibility == Visibility.Collapsed) continue;
            bool selected=preset==vm.EffectivePreset;
            Selected(item.Button,selected);
            string detail=OverlayPresets.ShowsLyrics(preset) ? vm.LyricsLayout ? "현재 곡 가사" : "샘플 가사 미리보기" : "";
            item.Label.Text=selected ? detail.Length>0 ? "✓ 적용 중 · "+detail : "✓ 적용 중" :
                detail.Length>0 ? detail : preset==PresetKind.MusicCard ? "기본 추천" : " ";
        }
    }
    static Button? FindByAutomationId(DependencyObject root,string id)
    {
        if (root is Button b && AutomationProperties.GetAutomationId(b)==id) return b;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var result=FindByAutomationId(VisualTreeHelper.GetChild(root,i),id); if(result is not null)return result; }
        return null;
    }
    double navTarget=double.NaN;
    void MoveNavBeam(bool animate)
    {
        double width=NavigationButtons.ActualWidth/4;
        if(width<=0)return;
        int index=vm.HomeVisible?0:vm.OverlayVisible?1:vm.AudioVisible?2:3;
        double target=width*(index+.5)-NavBeam.Width/2;
        var move=(TranslateTransform)NavBeam.RenderTransform;
        if(target==navTarget && (animate || !move.HasAnimatedProperties))return;
        double start=move.X;
        move.BeginAnimation(TranslateTransform.XProperty,null);move.X=target;
        if(animate && double.IsFinite(navTarget) && Math.Abs(start-target)>1)
            move.BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(start,target,TimeSpan.FromMilliseconds(310))
            { EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut},
              FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop });
        navTarget=target;
    }
    MusicTheme? appliedTheme;
    bool prismMotion;
    string? coverIdentity;
    void ApplyAppearance()
    {
        var palette=vm.Palette;
        bool motion = palette.IsPrism && vm.AnimationsEnabled && SystemParameters.ClientAreaAnimation && !renderOnly && IsVisible && WindowState!=WindowState.Minimized;
        string identity=vm.Media.State.Source+"|"+vm.Media.State.Title+"|"+vm.Media.State.ArtworkKey;
        if(coverIdentity!=identity && coverIdentity is not null && motion && vm.HomeVisible)
            DesignMotion.RevealArtwork(HomePlayer.ArtworkFrame,HomePlayer.CoverLight);
        coverIdentity=identity;
        if(appliedTheme==palette && prismMotion==motion && AppearancePanel.Art.IsChecked==vm.Appearance.ArtworkAccent)return;
        prismMotion=motion;
        if(!motion)DesignMotion.StopArtwork(HomePlayer.ArtworkFrame,HomePlayer.CoverLight);
        if(palette.IsPrism)
        {
            Resources["PrismRibbon"]=PrismPaint.Ribbon(motion);
            Resources["PrismRefraction"]=PrismPaint.Refraction(motion);
        }
        else
        {
            Resources["PrismRibbon"]=Brushes.Transparent;
            Resources["PrismRefraction"]=Brushes.Transparent;
        }
        bool animate=appliedTheme is not null && vm.Appearance.ArtworkAccent && vm.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
        appliedTheme=palette;
        foreach(var scope in new FrameworkElement[]{this,HomePlayer,Mini,SpectrumSettings,WidgetSettings})
            StudioAppearance.Apply(scope.Resources,palette,animate);
        AppearancePanel.Refresh(palette);
        HomePlayer.OnlineLyricsPanel.RefreshColors();
        if(hwnd!=0)WindowChromeTheme.Apply(hwnd,IsActive,palette);
        if(gallery.Count>0)UpdateSelection();
        foreach(var item in gallery.Values)item.Label.SetResourceReference(TextBlock.ForegroundProperty,"StudioMuted");
    }
    void RefreshVisuals()
    {
        if (closing || hwnd == IntPtr.Zero) return;
        try
        {
            ApplyAppearance();
            var document=vm.RenderDocument();var scene=document.Scenes[0];
            runtime.Update(document,vm.Media.State,vm.IsPositioning ? scene.Id : null);
            if(vm.OverlayVisible && IsVisible && WindowState!=WindowState.Minimized)RefreshPreviewPanels(scene);
            positionWindow?.Refresh(scene);
            RefreshSpectrum();
        }
        catch(Exception ex) { vm.Status = "오버레이 반영 실패: " + ex.Message; vm.Log.Error("새 UI 오버레이 반영",ex); }
    }
    void RefreshPreviewPanels(Scene scene)
    {
        var playback=vm.LyricsPreviewPlayback;
        livePreview.SetContent(scene,playback);
        LivePreviewHost.Height=OverlayPresets.Dimensions(vm.EffectivePreset).Height>=300?480:240;
        if(!vm.DesignGalleryVisible)return;
        foreach(var (preset,item) in gallery)
        {
            if (!vm.PresetAvailable(preset)) continue;
            var config=vm.Document.Overlay with{Preset=preset,Scale=1};
            SetGalleryPreview(item.Preview,config,playback);
        }
    }
    static readonly LyricDocument samplePreview = new(LyricFormat.Lines,
        LyricsContent.Demo(0).PlainText.Split('\n').Select((text,i)=>new LyricLine(text,i*4,(i+1)*4)).ToArray());
    void SetGalleryPreview(OverlayPreview preview,OverlayConfig config,PlaybackState playback)
    {
        bool sample=OverlayPresets.ShowsLyrics(config.Preset) && !vm.LyricsLayout;
        var state=sample ? PlaybackState.Demo() with { PreviewLyrics=samplePreview, PreviewLyricFrame=LyricFrame.At(samplePreview,5) }
            : playback.Source.Length>0 ? playback : PlaybackState.Demo();
        preview.SetContent(vm.CreatePreview(config,sample),state);
    }
    void MediaCommand(string command) => _ = vm.Media.CommandAsync(command);
    void RefreshLyrics()
    {
        var lyrics=vm.Lyrics;
        runtime.UpdateLyrics(lyrics.Document,lyrics.Frame,lyrics.Message);
        if (!vm.OverlayVisible || !IsVisible || WindowState==WindowState.Minimized) return;
        RefreshPreviewLyrics();
    }
    void RefreshLyricsVisibility()
    {
        if(renderOnly || closing)return;
        bool mainVisible = IsVisible && WindowState != WindowState.Minimized;
        var panel = HomePlayer.OnlineLyricsPanel;
        bool home = mainVisible && vm.HomeVisible && panel.IsVisible && !panel.OptionsPanel.IsVisible;
        panel.SetPresentationActive(home);
        bool lyricPreset = OverlayPresets.ShowsLyrics(vm.EffectivePreset);
        bool desktop = lyricPreset && (vm.OverlayEnabled || vm.IsPositioning);
        bool preview = mainVisible && vm.OverlayVisible &&
            (lyricPreset && SpectrumVisibility.Intersects(livePreview,PageScroll) ||
             vm.DesignGalleryVisible && gallery.Any(card=>OverlayPresets.ShowsLyrics(card.Key) &&
                 SpectrumVisibility.Intersects(card.Value.Preview,PageScroll)));
        vm.SetLyricsPresentationVisible(home || desktop || preview);
        if(preview)RefreshPreviewLyrics();
    }
    void RefreshPreviewLyrics()
    {
        // Scrolling a previously clipped card into view also refreshes a paused track.
        var lyrics=vm.Lyrics;
        if(SpectrumVisibility.Intersects(livePreview,PageScroll))livePreview.RefreshLyricFrame(lyrics.Document,lyrics.Frame);
        if(vm.DesignGalleryVisible)
            foreach(var (kind,item) in gallery)
                if(OverlayPresets.ShowsLyrics(kind) && SpectrumVisibility.Intersects(item.Preview,PageScroll))
                    item.Preview.RefreshLyricFrame(lyrics.Document,lyrics.Frame);
    }
    void RefreshSpectrum()
    {
        if(renderOnly || closing)return;
        bool mainVisible=IsVisible && WindowState!=WindowState.Minimized;
        RefreshLyricsVisibility();
        var options=vm.SpectrumOptions; renderSpectrumOptions=options;
        bool visible=options.Home && mainVisible && vm.HomeVisible && HomePlayer.SpectrumIntersects(HomePlayer) || options.Overlay && (vm.OverlayEnabled || mainVisible && (vm.OverlayVisible || vm.IsPositioning)) || options.Taskbar && taskbar?.Widget is {IsVisible:true};
        spectrumInk=vm.Palette.SpectrumGradient?PrismPaint.Spectrum:(Brush)FindResource("StudioSpectrum");
        var monitors=vm.Monitors; var active=new HashSet<string>();
        if(mainVisible && (options.Home && vm.HomeVisible || options.Overlay && (vm.OverlayVisible || vm.IsPositioning)) && Native.GetWindowRect(hwnd,out var bounds))
            foreach(var m in monitors) if(bounds.Right>m.Bounds.Left && bounds.Left<m.Bounds.Right && bounds.Bottom>m.Bounds.Top && bounds.Top<m.Bounds.Bottom)active.Add(m.Device);
        if(options.Overlay && vm.OverlayEnabled)active.Add(vm.Document.Overlay.MonitorId);
        if(options.Taskbar && taskbar?.Widget is {IsVisible:true})active.Add(vm.Document.Taskbar.MonitorId);
        if(active.Remove(""))active.Add(monitors.FirstOrDefault(m=>m.Primary)?.Device??"");
        // One shared clock follows the slowest participating output, never an unused fast screen.
        int displayHz=monitors.Where(m=>active.Contains(m.Device)).Select(m=>
        {
            if(!displayRates.TryGetValue(m.Device,out int hz))displayRates[m.Device]=hz=DisplayRefresh.Read(m.Device);
            return hz;
        }).DefaultIfEmpty(60).Min();
        spectrumCoordinator.Configure(options,vm.Document.Overlay.Animation,visible,displayHz);
        taskbar?.Widget?.ConfigureSpectrum(options);
    }
    void PaintSpectrum(ReadOnlySpan<float> values,ReadOnlySpan<float> peaks)
    {
        if(renderOnly)return;
        var options=renderSpectrumOptions;
        runtime.UpdateSpectrum(options.Overlay?values:[],peaks);
        bool mainVisible=IsVisible && WindowState!=WindowState.Minimized;
        if(mainVisible && vm.HomeVisible && HomePlayer.SpectrumIntersects(HomePlayer) || values.IsEmpty)
            HomePlayer.SetSpectrum(options.Home?values:[],options.Style,spectrumInk,peaks);
        if(mainVisible && vm.OverlayVisible || values.IsEmpty)
        {
            if(SpectrumVisibility.Intersects(livePreview,PageScroll) || values.IsEmpty)livePreview.SetSpectrum(options.Overlay?values:[],peaks);
            if(vm.DesignGalleryVisible || values.IsEmpty)
                foreach(var item in gallery.Values)if(SpectrumVisibility.Intersects(item.Preview,PageScroll) || values.IsEmpty)item.Preview.SetSpectrum(options.Overlay?values:[],peaks);
        }
        taskbar?.Widget?.SetSpectrum(options.Taskbar?values:[],spectrumInk,peaks);
    }
    void SetSpectrumStatus(string status) {if(vm.SpectrumStatus!=status){vm.SpectrumStatus=status;vm.NotifySpectrum();}}
    void StopSpectrum()=>spectrumCoordinator.Configure(vm.SpectrumOptions,vm.Document.Overlay.Animation,false,60);
    void SystemMotionChanged(object? sender, PropertyChangedEventArgs e)
    { if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation))Dispatcher.BeginInvoke(RefreshVisuals); }
    void ReplayMotion() { runtime.ReplayMotion(); }
    void RegisterHotkey()
    {
        if (hwnd == IntPtr.Zero || closing) return;
        var config=(vm.HotkeyEnabled,vm.HotkeyFunction); if (hotkey == config) return;
        Native.UnregisterHotKey(hwnd,1); hotkey=config;
        bool ok = !config.Item1 || Native.RegisterHotKey(hwnd,1,0x4000|1|2,(uint)(0x70+config.Item2-1));
        vm.HotkeyStatus = !config.Item1 ? "전역 단축키 꺼짐" : ok ? $"Ctrl + Alt + F{config.Item2} · 오버레이 켜기 / 끄기" : "다른 앱에서 사용 중입니다. 다른 키를 선택하세요.";
        vm.Log.Write("hotkey",new { enabled=config.Item1,key=config.Item2,registered=ok&&config.Item1 }); vm.NotifyAll();
    }
    IntPtr WindowMessage(IntPtr h,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if (message==0x312 && wp.ToInt32()==1 && !closing && !vm.IsPositioning) { var before=Native.GetForegroundWindow(); vm.OverlayEnabled=!vm.OverlayEnabled; vm.Log.Write("hotkey_toggle",new { enabled=vm.OverlayEnabled,focusBefore=before.ToInt64(),focusAfter=Native.GetForegroundWindow().ToInt64() }); handled=true; }
        if (message==0x7E) Dispatcher.BeginInvoke(() => { displayRates.Clear(); if(positionWindow is not null)EndPosition(false); vm.RefreshMonitors(); });
        if (message==0x02E0) Dispatcher.BeginInvoke(() => { displayRates.Clear(); RefreshSpectrum(); });
        if(message==0x1A)Dispatcher.BeginInvoke(()=>WindowChromeTheme.Apply(hwnd,IsActive,vm.Palette));
        return IntPtr.Zero;
    }
    void BeginPosition()
    {
        if (positionWindow is not null) return;
        try {
            vm.BeginPosition(); RefreshVisuals();
            positionWindow = new(vm.RenderScene(),vm.Palette) { Owner = this }; positionWindow.PositionChanged += vm.MovePosition; positionWindow.Finished += EndPosition;
            positionWindow.Show(); positionWindow.Refresh(vm.RenderScene()); vm.Log.Write("position_open");
        } catch(Exception ex) { EndPosition(false); vm.Status="디자인 조정을 열지 못했습니다. 다시 시도해 주세요.";vm.Log.Error("디자인 조정 시작",ex); }
    }
    void EndPosition(bool save)
    {
        var window=positionWindow;
        bool restoreFocus=window is not null && Native.GetForegroundWindow()==new WindowInteropHelper(window).Handle;
        if(window is not null) { window.CancelGesture(); positionWindow=null; window.PositionChanged-=vm.MovePosition; window.Finished-=EndPosition; window.AllowClose=true; window.Close(); }
        vm.EndPosition(save); RefreshVisuals(); if(restoreFocus && !closing) Native.SetForegroundWindow(hwnd); vm.Log.Write("position_close",new { save, restoreFocus });
    }
    async Task RefreshAudio()
    {
        if (audioBusy || closing) return; audioBusy=true;
        try { var state=await Task.Run(AudioOutputReader.Read); vm.AudioStatus=state.Text; vm.OutputLevel=state.Level; vm.NotifyAudio(); }
        finally { audioBusy=false; RefreshSpectrum(); }
    }
    void Diagnostics()
    {
        if (closing) return;
        try
        {
            var data=new { utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,hwnd=hwnd.ToInt64(),foreground=Native.GetForegroundWindow().ToInt64(),settings=vm.Document,document=vm.RenderDocument(),extensions=new { lyrics=vm.HasLyricsProvider, effectivePreset=vm.EffectivePreset.ToString(), availablePresets=gallery.Keys.Count(vm.PresetAvailable), homeLyrics=HomePlayer.OnlineLyricsPanel.Visibility.ToString(), homeSpectrum=HomePlayer.FrequencySpectrum.ActualHeight },page=vm.Page,section=vm.Section,positioning=vm.IsPositioning,positionHwnd=positionWindow is null ? 0 : new WindowInteropHelper(positionWindow).Handle.ToInt64(),overlayWindows=runtime.Snapshot(),windowCount=runtime.WindowCount,nodeCount=runtime.NodeCount,uploads=runtime.UploadCount,spectrum=new { spectrumCoordinator.EffectiveLimit,spectrumCoordinator.SubmittedFrames,spectrumCoordinator.LastSnapshotAgeMs,spectrumCoordinator.CaptureStatus,SpectrumCapture.ActiveWorkers },media=new {vm.Media.State.Source,vm.Media.State.IsDemo,vm.Media.State.IsPlaying,vm.Media.State.Status,vm.Media.State.CommandStatus,vm.Media.State.CanToggle,vm.Media.State.CanNext,vm.Media.State.CanPrevious,vm.Media.State.CanSeek,vm.Media.State.AlbumTitle,vm.Media.State.PositionSeconds,vm.Media.State.DurationSeconds,lyricsKind=vm.Lyrics.Document.Format.ToString(),lyricsStatus=vm.Lyrics.Status.ToString(),lyricsRecordId=vm.Lyrics.Document.RecordId,lyricsLineCount=vm.Lyrics.Document.Lines.Count,lyricsUncertain=vm.Lyrics.Frame.Uncertain,titleHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(vm.Media.State.Title)))},sessions=vm.Media.Sessions,vm.Status,vm.SaveStatus,vm.HotkeyStatus };
            string path=Path.Combine(vm.Repository.DirectoryPath,"diagnostics.json"); File.WriteAllText(path+".tmp",JsonSerializer.Serialize(data,new JsonSerializerOptions(SceneJson.Options){IncludeFields=true})); File.Move(path+".tmp",path,true);
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }
    async void CloseAsync(object? sender,CancelEventArgs e)
    {
        if(canClose)return; e.Cancel=true; if(closing)return;
        if(positionWindow is not null)EndPosition(false);
        closing=true; IsEnabled=false; diagnostics?.Stop(); audioTimer.Stop(); StopSpectrum(); await vm.SaveAsync(false);
        if(!vm.LastSaveSucceeded) { closing=false;IsEnabled=true;diagnostics?.Start();audioTimer.Start();RefreshSpectrum();vm.Status="저장되지 않은 변경 사항이 있습니다. 경로를 확인하고 다시 저장해 주세요.";return; }
        spectrumCoordinator.Dispose(); taskbar?.Dispose();
        vm.Stop();vm.Changed-=RefreshVisuals;vm.PropertyChanged-=VmPropertyChanged;vm.PositionRequested-=BeginPosition;vm.PositionEndRequested-=EndPosition;vm.SettingsChanged-=RegisterHotkey;vm.MotionReplayRequested-=ReplayMotion;
        SystemParameters.StaticPropertyChanged-=SystemMotionChanged;
        vm.Lyrics.FrameChanged-=RefreshLyrics;
        vm.Media.Dispose();Native.UnregisterHotKey(hwnd,1);source?.RemoveHook(WindowMessage);runtime.CommandRequested-=MediaCommand;
        await runtime.ShutdownAsync(); ElementDrawing.ClearCache();vm.Log.Write("app_shutdown_complete");canClose=true;
        // Cleanup may complete synchronously (for example after an earlier shutdown).
        // Leave the current Closing event before asking WPF to close again.
        _ = Dispatcher.BeginInvoke(() => { Close(); Application.Current.Shutdown(); }, DispatcherPriority.Normal);
    }
}








