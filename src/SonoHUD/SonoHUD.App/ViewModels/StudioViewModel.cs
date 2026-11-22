using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SonoHUD.App.Media;
using SonoHUD.App.Platform;
using SonoHUD.App.Rendering;
using SonoHUD.App.Views;
using SonoHUD.Core;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel : INotifyPropertyChanged
{
    internal OverlayDocument Document { get; private set; }
    internal readonly OverlayRepository Repository;
    internal readonly MediaService Media;
    internal readonly AppLog Log;
    readonly DispatcherTimer autosave;
    readonly SemaphoreSlim saveGate = new(1, 1);
    readonly Stack<OverlayDocument> undo = new(), redo = new();
    OverlayDocument? positionStart;
    int revision, savedRevision = -1;
    bool stopped;
    bool updatingMediaSessions;
    bool designGallery = true;
    string page = "Home", section = "Layout", status = "", saveStatus = "저장 준비 중";
    internal bool CanSave { get; }
    internal bool LastSaveSucceeded { get; private set; } = true;
    public bool HasSaveError => !LastSaveSucceeded;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action? Changed;
    internal event Action? PositionRequested;
    internal event Action<bool>? PositionEndRequested;
    internal event Action? SettingsChanged;
    internal event Action? MotionReplayRequested;
    public ObservableCollection<Native.Monitor> Monitors { get; } = [];
    public ObservableCollection<MediaSessionItem> MediaSessions { get; } = [];
    public string[] FontChoices => FontCatalog.Choices;
    public int[] HotkeyKeys { get; } = [8, 9, 10, 11, 12];
    public string Page => page;
    public string Section => section;
    public bool HomeVisible => page == "Home";
    public bool MiniPlayerVisible => !HomeVisible;
    public bool OverlayVisible => page == "Overlay";
    public bool AudioVisible => page == "Audio";
    public bool SettingsVisible => page == "Settings";
    public bool LayoutVisible => section == "Layout";
    public bool AppearanceVisible => section == "Appearance";
    public bool AnimationVisible => section == "Animation";
    public bool DesignGalleryVisible => designGallery;
    public bool CustomizationVisible => !designGallery;
    public bool IsPositioning => positionStart is not null;
    public bool CanCustomize => !IsPositioning;
    public bool AnimationsEnabled => Document.Overlay.Animation != AnimationProfile.Off;
    public string Status { get => status; internal set { status = value; Notify(); } }
    public string SaveStatus { get => saveStatus; private set { saveStatus = value; Notify(); } }
    public string DataPath => Repository.DirectoryPath;
    public bool OverlayEnabled { get => Document.Overlay.Enabled; set { if (!IsPositioning) Change("오버레이 " + (value ? "켜짐" : "꺼짐"), d => d.Overlay.Enabled = value); } }
    public string OverlayState => IsPositioning ? "조정 중 · 임시 표시" : OverlayEnabled ? "화면에 표시 중" : "오버레이 꺼짐";
    public string VisibilityHint => IsPositioning ? "화면의 오버레이를 끌어 이동하고, 모서리를 끌어 크기를 바꾸세요. 완료하면 저장됩니다." + (OverlayEnabled ? "" : " 조정이 끝나면 원래대로 숨깁니다.") : OverlayEnabled ? "변경 사항이 화면에 바로 반영됩니다." : "미리보기에는 반영됩니다. 화면에 보려면 하단에서 오버레이를 켜세요.";
    internal bool PresetAvailable(PresetKind preset) => Enum.IsDefined(preset) && (!OverlayPresets.ShowsLyrics(preset) || HasLyricsProvider);
    public PresetKind EffectivePreset => PresetAvailable(Document.Overlay.Preset) ? Document.Overlay.Preset : PresetKind.MusicCard;
    public string PresetName => OverlayPresets.DisplayName(EffectivePreset);
    public string PresetDescription => EffectivePreset switch { PresetKind.Compact => "낮고 긴 가로형", PresetKind.LyricsFocus => "이전·현재·다음 가사를 함께 표시", PresetKind.LyricStrip => "현재 가사를 크게 표시하는 가로 자막", PresetKind.LyricSleeve => "앨범 표지 아래에 가사를 읽는 세로 카드", PresetKind.Cinematic => "큰 곡명 · 오른쪽 앨범아트", PresetKind.AlbumTile => "큰 앨범아트 · 타일형 곡 정보", PresetKind.InfoRail => "세로 구획으로 나눈 음악 정보", PresetKind.Poster => "앨범아트와 큰 제목의 포스터", PresetKind.StatusStrip => "재생 상태와 시간을 담은 얇은 띠", _ => "왼쪽 앨범아트 · 곡 정보" };
    public bool IsLyrics => OverlayPresets.ShowsLyrics(EffectivePreset);
    public bool DarkTheme => Document.Overlay.Theme == OverlayTheme.Dark;
    public bool LightTheme => !DarkTheme;
    public string AccentColor { get => Document.Overlay.AccentColor; set => Change("강조색 변경", d => d.Overlay.AccentColor = value); }
    public string FontFamily { get => FontCatalog.NormalizeSelection(Document.Overlay.FontFamily); set { if (!string.IsNullOrWhiteSpace(value)) Change("글꼴 변경", d => d.Overlay.FontFamily = value); } }
    public double SizePercent { get => Document.Overlay.Scale * 100; set => Change("크기 변경", d => d.Overlay.Scale = Math.Clamp(Math.Round(value / 5) * 5, 75, 150) / 100); }
    public string SizeLabel => $"{SizePercent:0}%";
    public string DimensionsLabel { get { var s = RenderScene(); return $"{s.Width:0} × {s.Height:0} · 프리셋에 맞는 비율"; } }
    public double OpacityPercent { get => Document.Overlay.BackgroundOpacity * 100; set => Change("배경 불투명도 변경", d => d.Overlay.BackgroundOpacity = Math.Clamp(Math.Round(value), 0, 100) / 100); }
    public string OpacityLabel => $"{OpacityPercent:0}%";
    public string MonitorId { get => Document.Overlay.MonitorId; set { if (value is not null && value != MonitorId) Change("표시할 모니터 변경", d => { d.Overlay.MonitorId = value; d.Overlay.X = 40; d.Overlay.Y = 40; }); } }
    public string MonitorLabel => Monitors.FirstOrDefault(m => m.Device == MonitorId)?.Label ?? "모니터 확인 필요";
    public bool ShowArtwork { get => Document.Overlay.ShowArtwork; set => Change("앨범아트 표시 변경", d => d.Overlay.ShowArtwork = value); }
    public bool ShowArtist { get => Document.Overlay.ShowArtist; set => Change("아티스트 표시 변경", d => d.Overlay.ShowArtist = value); }
    public bool ShowProgress { get => Document.Overlay.ShowProgress; set => Change("재생 진행률 표시 변경", d => d.Overlay.ShowProgress = value); }
    public bool ShowControls { get => Document.Overlay.ShowControls; set => Change("재생 버튼 표시 변경", d => d.Overlay.ShowControls = value); }
    public bool ClickThrough { get => Document.Overlay.ClickThrough; set => Change("마우스 클릭 통과 변경", d => d.Overlay.ClickThrough = value); }
    public bool ShowOnStartup { get => Document.Settings.ShowOverlaysOnStartup; set => Change("시작 시 표시 변경", d => d.Settings.ShowOverlaysOnStartup = value); }
    public bool KeepRunningOnClose { get => Document.Settings.KeepRunningOnClose; set => Change("창 닫기 동작 변경", d => d.Settings.KeepRunningOnClose = value); }
    public bool HotkeyEnabled { get => Document.Settings.HotkeyEnabled; set => Change("단축키 사용 변경", d => d.Settings.HotkeyEnabled = value); }
    public int HotkeyFunction { get => Document.Settings.HotkeyFunction; set => Change("단축키 변경", d => d.Settings.HotkeyFunction = value); }
    public string HotkeyStatus { get; internal set; } = "단축키 준비 중";
    public string AnimationName => Document.Overlay.Animation switch { AnimationProfile.Off => "없음", AnimationProfile.Calm => "차분하게", AnimationProfile.Dynamic => "생동감", _ => "시네마틱" };
    public bool DemoMode { get => Media.DemoMode; set { Media.SetDemo(value); NotifyMedia(); } }
    public string SelectedMedia
    {
        get => Media.SelectedKey;
        set
        {
            if (updatingMediaSessions || stopped || IsPositioning || value is null || value == Media.SelectedKey) return;
            Media.Select(value);
            Change("음악 소스 선택 저장", d => d.Settings.MediaSourceAppId = Media.PreferredSourceAppId);
        }
    }
    public string TrackTitle => Media.State.Title;
    public string TrackArtist => Media.State.Artist;
    public string ConnectionStatus => Media.State.IsDemo ? "샘플 미리보기 · 실제 음악이 아닙니다" : Media.State.Source.Length > 0 ? "연결된 플레이어 · " + Media.State.Source : Media.PreferredSourceAppId.Length > 0 ? Media.State.Status : "플레이어에서 음악을 재생해 주세요";
    public string MediaStatus => Media.State.CommandStatus switch { "플레이어 상태 변경 확인" => "", "명령 전송 중 · 플레이어 상태 확인 대기" or "명령 수락 · 플레이어 상태 확인 대기" => "플레이어 응답 확인 중…", "명령 수락됨 · 상태 변경은 아직 확인되지 않음" => "플레이어가 아직 응답하지 않았습니다. 재생 상태를 확인해 주세요.", "플레이어가 명령을 수락하지 않았습니다" => "플레이어가 이 조작을 지원하지 않거나 응답하지 않았습니다.", "명령 실패 · 플레이어 연결을 확인하세요" => "조작하지 못했습니다. 플레이어 연결을 확인해 주세요.", _ => Media.State.CommandStatus.Length > 0 ? Media.State.CommandStatus : Media.State.Status.Contains("실패") || Media.State.Status.Contains("읽") ? "플레이어 정보를 가져오지 못했습니다. 오디오에서 연결을 확인해 주세요." : "" };
    public bool HasMediaStatus => MediaStatus.Length > 0;
    public string PlaybackLabel => Media.State.Source.Length == 0 ? "연결 없음" : Media.State.IsPlaying ? "재생 중" : "일시정지";
    public string GalleryHint => Media.State.Source.Length == 0 || Media.State.IsDemo ? "샘플 정보로 모양을 비교합니다. 선택하면 바로 적용됩니다." : "현재 음악으로 모양을 비교합니다. 선택하면 바로 적용됩니다.";
    public string PlayLabel => Media.State.IsPlaying ? "일시정지" : "재생";
    public string PlayGlyph => Media.State.IsPlaying ? "Ⅱ" : "▶";
    public double Progress => Media.State.Progress * 100;
    public string PositionText => Media.State.PositionSeconds is double p && Media.State.DurationSeconds is double d ? $"{TimeSpan.FromSeconds(p):m\\:ss} / {TimeSpan.FromSeconds(d):m\\:ss}" : "재생 위치 정보 없음";
    public BitmapSource? AlbumArt => ElementDrawing.Artwork(Media.State);
    public bool HasArtwork => AlbumArt is not null;
    public string AudioStatus { get; internal set; } = "기본 출력 장치 확인 중";
    public double OutputLevel { get; internal set; }
    public ICommand NavigateCommand { get; }
    public ICommand DesignsCommand { get; }
    public ICommand CustomizeCommand { get; }
    public ICommand SectionCommand { get; }
    public ICommand PresetCommand { get; }
    public ICommand ThemeCommand { get; }
    public ICommand AccentCommand { get; }
    public ICommand AnimationCommand { get; }
    public ICommand ReplayCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand PositionCommand { get; }
    public ICommand AnchorCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand ExitDemoCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }
    public ICommand ApplyDesignCommand { get; }
    public ICommand CancelDesignCommand { get; }
    internal StudioViewModel(OverlayDocument document, OverlayRepository repository, MediaService media, AppLog log, bool canSave = true)
    {
        Document = document; Repository = repository; Media = media; Log = log; CanSave = canSave;
        RefreshMonitors();
        if (!document.Settings.ShowOverlaysOnStartup) document.Overlay.Enabled = false;
        RelayCommand C(Action<object?> a, Func<bool>? can = null, bool duringEdit = false) => new(p => Run(() => a(p)), _ => !stopped && (duringEdit || !IsPositioning) && (can?.Invoke() ?? true));
        NavigateCommand = C(p => { FinishDesign(false); page = p?.ToString() ?? "Home"; if(page == "Overlay") designGallery = true; NotifyAll(); }, duringEdit: true);
        DesignsCommand = C(_ => { FinishDesign(false); designGallery = true; NotifyAll(); }, duringEdit: true);
        CustomizeCommand = C(_ => { if(IsPositioning)return; page = "Overlay"; designGallery = false; if(PositionRequested is null)BeginPosition();else PositionRequested.Invoke(); NotifyAll(); }, duringEdit: true);
        SectionCommand = C(p => { section = p?.ToString() ?? "Layout"; NotifyAll(); }, duringEdit: true);
        ToggleCommand = C(_ => OverlayEnabled = !OverlayEnabled);
        PresetCommand = C(p => { if (Enum.TryParse<PresetKind>(p?.ToString(), out var v) && PresetAvailable(v)) Change("디자인 적용: " + v, d => d.Overlay.Preset = v); });
        ThemeCommand = C(p => { if (Enum.TryParse<OverlayTheme>(p?.ToString(), out var v)) Change("테마 변경", d => d.Overlay.Theme = v); }, duringEdit: true);
        AccentCommand = C(p => AccentColor = p?.ToString() ?? "#87DFC4", duringEdit: true);
        AnimationCommand = C(p => { if (Enum.TryParse<AnimationProfile>(p?.ToString(), out var v)) Change("움직임 변경", d => d.Overlay.Animation = v); }, duringEdit: true);
        ReplayCommand = C(_ => MotionReplayRequested?.Invoke(), () => OverlayEnabled);
        PositionCommand = C(_ => PositionRequested?.Invoke()); AnchorCommand = C(p => Anchor(p?.ToString() ?? "BottomRight"), duringEdit: true);
        ApplyDesignCommand = C(_ => FinishDesign(true), () => IsPositioning, duringEdit: true);
        CancelDesignCommand = C(_ => FinishDesign(false), () => IsPositioning, duringEdit: true);
        UndoCommand = C(_ => Undo(), () => undo.Count > 0); RedoCommand = C(_ => Redo(), () => redo.Count > 0);
        SaveCommand = C(_ => _ = SaveAsync(), () => CanSave);
        PlayCommand = C(_ => _ = Media.CommandAsync("toggle"), () => Media.State.CanToggle && !Media.Busy, duringEdit: true);
        PreviousCommand = C(_ => _ = Media.CommandAsync("previous"), () => Media.State.CanPrevious && !Media.Busy, duringEdit: true);
        NextCommand = C(_ => _ = Media.CommandAsync("next"), () => Media.State.CanNext && !Media.Busy, duringEdit: true);
        ExitDemoCommand = C(_ => DemoMode = false);
        RefreshMonitorsCommand = C(_ => RefreshMonitors());
        autosave = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        autosave.Tick += async (_, _) => { autosave.Stop(); await SaveAsync(false); };
        media.RestoreSourcePreference(document.Settings.MediaSourceAppId);
        media.Changed += MediaChanged;
        SaveStatus = canSave ? "변경 사항을 자동 저장합니다" : "읽기 전용 · 기존 데이터 보호 중";
        InitializePlayer();
        InitializeLyrics();
    }
    internal Scene RenderScene() => CreatePreview(Document.Overlay with { Preset = EffectivePreset });
    internal SceneDocument RenderDocument() => new() { Settings=Document.Settings with {}, Scenes=[RenderScene()] };
    internal void Run(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException) { Status = "변경하지 못했습니다: " + ex.Message; Log.Error("설정 변경", ex); NotifyAll(); } }
    void Change(string label, Action<OverlayDocument> action)
    {
        if (stopped) return;
        Run(() => { var next = OverlayJson.Clone(Document); action(next); next = OverlayJson.Parse(OverlayJson.Serialize(next)); if (OverlayJson.Serialize(next) == OverlayJson.Serialize(Document)) return;
            if (IsPositioning) { Document = next; DraftChanged(); return; }
            undo.Push(OverlayJson.Clone(Document)); redo.Clear(); Document = next; MarkChanged(label); });
    }
    void MarkChanged(string label) { Media.RestoreSourcePreference(Document.Settings.MediaSourceAppId); revision++; Status = label; SaveStatus = CanSave ? "저장 중…" : "읽기 전용 · 변경 사항은 이번 실행에만 적용"; autosave.Stop(); if (CanSave && !IsPositioning) autosave.Start(); NotifyAll(); SettingsChanged?.Invoke(); Changed?.Invoke(); ObserveLyrics(); CommandManager.InvalidateRequerySuggested(); }
    void Undo() { redo.Push(OverlayJson.Clone(Document)); Document = undo.Pop(); MarkChanged("이전 상태로 되돌렸습니다."); }
    void Redo() { undo.Push(OverlayJson.Clone(Document)); Document = redo.Pop(); MarkChanged("변경 사항을 다시 적용했습니다."); }
    void Anchor(string anchor)
    {
        var m = Monitors.FirstOrDefault(x => x.Device == MonitorId) ?? Monitors.First(); var s = RenderScene();
        double left = (m.Work.Left - m.Bounds.Left) / m.Scale + 24, top = (m.Work.Top - m.Bounds.Top) / m.Scale + 24;
        double right = Math.Max(left, (m.Work.Right - m.Bounds.Left) / m.Scale - s.Width - 24), bottom = Math.Max(top, (m.Work.Bottom - m.Bounds.Top) / m.Scale - s.Height - 24);
        Change("표시 위치 변경", d => { d.Overlay.X = anchor.Contains("Right") ? right : anchor == "Center" ? (left + right) / 2 : left; d.Overlay.Y = anchor.Contains("Bottom") ? bottom : anchor == "Center" ? (top + bottom) / 2 : top; });
    }
    internal void RefreshMonitors()
    {
        Monitors.Clear(); foreach (var m in Native.Monitors()) Monitors.Add(m);
        if (!Monitors.Any(m => m.Device == MonitorId))
        {
            Document.Overlay.MonitorId = (Monitors.FirstOrDefault(m => !m.Primary && m.Bounds.Width > m.Bounds.Height) ?? Monitors.FirstOrDefault(m => !m.Primary) ?? Monitors.First()).Device; Document.Overlay.X = 40; Document.Overlay.Y = 40;
            if (autosave is not null) MarkChanged("연결된 모니터에 오버레이 위치를 맞췄습니다.");
            else Status = "";
        }
        NotifyAll(); Changed?.Invoke();
    }
    void DraftChanged() { Status = "조정 결과를 미리 보고 있습니다. 완료하면 저장됩니다."; SaveStatus = CanSave ? "조정 중 · 완료하면 저장" : "읽기 전용 · 이번 실행에만 적용"; NotifyAll(); Changed?.Invoke(); CommandManager.InvalidateRequerySuggested(); }
    internal void BeginPosition()
    {
        if (IsPositioning) return; autosave.Stop(); positionStart = OverlayJson.Clone(Document);
        var scene = RenderScene(); var monitors = Native.DesktopMonitors(); var source = DesktopLayout.Resolve(scene,monitors);
        var origin = DesktopLayout.Origin(scene,source); var visible = DesktopLayout.VisibleOrigin(scene,monitors);
        if(origin != visible) { var target = DesktopLayout.Nearest(visible,monitors); Document.Overlay.MonitorId=target.Id;Document.Overlay.X=(visible.X-target.Bounds.X)/target.Scale;Document.Overlay.Y=(visible.Y-target.Bounds.Y)/target.Scale; }
        DraftChanged();
    }
    internal void MovePosition(Scene s) { if (!IsPositioning) return; Document.Overlay.MonitorId = s.MonitorId; Document.Overlay.X = s.X; Document.Overlay.Y = s.Y; Document.Overlay.Scale = Math.Clamp(s.Width / OverlayPresets.Dimensions(EffectivePreset).Width, .75, 1.5); DraftChanged(); }
    internal void FinishDesign(bool apply) { if(!IsPositioning)return; PositionEndRequested?.Invoke(apply); if(IsPositioning)EndPosition(apply); }
    internal void EndPosition(bool save)
    {
        if (positionStart is null) return; var before = positionStart; positionStart = null; designGallery = true;
        if (save && OverlayJson.Serialize(before) != OverlayJson.Serialize(Document)) { undo.Push(before); redo.Clear(); MarkChanged("디자인 조정을 적용했습니다."); }
        else { if(!save) Document = before; Status = save ? "변경 없이 조정을 마쳤습니다." : "미완료 디자인 조정을 취소했습니다."; SaveStatus = !CanSave ? "읽기 전용 · 기존 데이터 보호 중" : !LastSaveSucceeded ? "저장하지 못함" : revision == savedRevision ? "모든 변경 사항 저장됨" : "저장 대기"; NotifyAll(); Changed?.Invoke(); CommandManager.InvalidateRequerySuggested(); if (CanSave && revision != savedRevision) autosave.Start(); }
    }
    internal async Task SaveAsync(bool announce = true)
    {
        if (IsPositioning) return;
        if (!CanSave) { LastSaveSucceeded = true; SaveStatus = "읽기 전용 · 기존 데이터 보호 중"; return; }
        await saveGate.WaitAsync();
        try { if (IsPositioning) return; if (savedRevision == revision) { LastSaveSucceeded = true; SaveStatus = "모든 변경 사항 저장됨"; return; } int current = revision; var snapshot = OverlayJson.Clone(Document); await Task.Run(() => Repository.Save(snapshot)); savedRevision = current; LastSaveSucceeded = true; SaveStatus = IsPositioning ? "조정 중 · 완료하면 저장" : savedRevision == revision ? "모든 변경 사항 저장됨" : "새 변경 사항 저장 대기"; if (announce && !IsPositioning) Status = "설정을 저장했습니다."; Log.Write("overlay_settings_saved", new { revision = current }); }
        catch (Exception ex) { LastSaveSucceeded = false; SaveStatus = "저장하지 못함"; Status = "설정 파일에 접근할 수 없습니다. 다른 앱에서 사용 중인지 확인한 뒤 다시 저장해 주세요."; Log.Error("오버레이 설정 저장", ex); }
        finally { Notify(nameof(HasSaveError)); saveGate.Release(); }
    }
    void MediaChanged(object? s, EventArgs e) { NotifyMedia(); NotifyPlayer(); ObserveLyrics(); }
    void NotifyMedia()
    {
        var displaySessions = Media.Sessions.Count == 0 ? new[] { new MediaSessionItem("auto","자동 선택") } : Media.Sessions.Select(s => s.Key == "auto" ? s with { Label = "자동 선택" } : s).ToArray();
        updatingMediaSessions = true;
        try
        {
            for(int i=MediaSessions.Count-1;i>=0;i--) if(!displaySessions.Any(s=>s.Key==MediaSessions[i].Key))MediaSessions.RemoveAt(i);
            for(int i=0;i<displaySessions.Length;i++) { var item=displaySessions[i];int current=MediaSessions.ToList().FindIndex(s=>s.Key==item.Key);if(current<0)MediaSessions.Insert(i,item);else {if(current!=i)MediaSessions.Move(current,i);if(MediaSessions[i]!=item)MediaSessions[i]=item;} }
        }
        finally { updatingMediaSessions = false; }
        foreach (var p in new[] { nameof(DemoMode), nameof(SelectedMedia), nameof(TrackTitle), nameof(TrackArtist), nameof(ConnectionStatus), nameof(MediaStatus), nameof(HasMediaStatus), nameof(PlaybackLabel), nameof(GalleryHint), nameof(PlayLabel), nameof(PlayGlyph), nameof(Progress), nameof(PositionText), nameof(AlbumArt), nameof(HasArtwork) }) Notify(p);
        Changed?.Invoke(); CommandManager.InvalidateRequerySuggested();
    }
    internal void NotifyAudio() { Notify(nameof(AudioStatus)); Notify(nameof(OutputLevel)); }
    internal void NotifyAll() => PropertyChanged?.Invoke(this, new(string.Empty));
    void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    internal void Stop() { stopped = true; autosave.Stop(); Media.Changed -= MediaChanged; StopLyrics(); }
}





