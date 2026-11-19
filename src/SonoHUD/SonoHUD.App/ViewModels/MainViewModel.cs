using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SonoHUD.Core;
using SonoHUD.App.Media;
using SonoHUD.App.Platform;
using SonoHUD.App.Rendering;

namespace SonoHUD.App.ViewModels;

internal sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}
internal sealed record SceneListItem(Guid Id, string Name, string Detail, bool Visible);
internal sealed record LayerItem(Guid Id, string Name, string Detail);
internal sealed class MainViewModel : INotifyPropertyChanged
{
    internal readonly SceneStore Store; internal readonly MediaService Media; internal readonly SceneRepository Repository; internal readonly AppLog Log;
    readonly DispatcherTimer autosave; readonly SemaphoreSlim saveGate = new(1, 1); int savedRevision = -1;
    Guid? sceneId; string page = "Home", status = "새 Scene을 만들어 나만의 위젯을 배치하세요.";
    readonly HashSet<Guid> selection = [];
    internal bool IsRefreshing { get; private set; }
    internal Guid? EditingSceneId { get; private set; }
    internal bool LastSaveSucceeded { get; private set; } = true;
    internal int ValidationFailures { get; private set; }
    bool pointerGesture;
    internal bool PointerGestureActive { get => pointerGesture; set { pointerGesture = value; CommandManager.InvalidateRequerySuggested(); } }
    internal event EventHandler? VisualChanged;
    internal event Action? ScreenEditRequested;
    internal event Action? ImportImageRequested;
    internal event Action? SettingsChanged;
    internal event Action? ToggleOverlaysRequested;
    internal Func<bool>? CommitInputRequested { get; set; }
    internal bool RuntimeSuspended { get; set; }
    public string OverlayPolicyStatus => RuntimeSuspended ? "오버레이 일시 숨김 · 단축키 또는 아래 버튼으로 복원" : "저장된 Scene 표시 정책 사용 중";
    public ICommand ToggleOverlaysCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<SceneListItem> Scenes { get; } = [];
    public ObservableCollection<LayerItem> Layers { get; } = [];
    public ObservableCollection<Native.Monitor> Monitors { get; } = [];
    public ObservableCollection<MediaSessionItem> MediaSessions { get; } = [];
    public Array ElementKinds { get; } = Enum.GetValues<ElementKind>().Where(x => x != ElementKind.Group).ToArray();
    public Array Animations { get; } = Enum.GetValues<AnimationProfile>();
    public int[] HotkeyKeys { get; } = [8, 9, 10, 11, 12];
    public ElementKind AddKind { get; set; } = ElementKind.Text;
    public string Page { get => page; private set { page = value; NotifyAll(); } }
    public bool HomeVisible => Page == "Home";
    public bool ScenesVisible => Page == "Scenes";
    public bool EditorVisible => Page == "Editor";
    public bool SettingsVisible => Page == "Settings";
    public string Status { get => status; set { status = value; Notify(); } }
    public Guid? SelectedSceneId { get => sceneId; set { if (value is null || value == sceneId || EditingSceneId.HasValue && value != EditingSceneId) return; sceneId = value; selection.Clear(); Refresh(); } }
    internal Scene? SelectedScene => sceneId.HasValue ? Store.Document.Scenes.FirstOrDefault(s => s.Id == sceneId) : null;
    internal IReadOnlySet<Guid> Selection => selection;
    internal SceneElement? SelectedElement => selection.Count == 1 ? SelectedScene?.Elements.FirstOrDefault(e => selection.Contains(e.Id)) : null;
    public bool HasScene => SelectedScene is not null;
    public bool HasElement => SelectedElement is not null;
    public bool SceneManagementEnabled => !EditingSceneId.HasValue && !Store.IsEditing;
    public string SceneName { get => SelectedScene?.Name ?? ""; set => SceneChange("Scene 이름", s => s.Name = value); }
    public string SceneX { get => Number(SelectedScene?.X); set => SceneNumber(value, "Scene 위치", (s, v) => s.X = v); }
    public string SceneY { get => Number(SelectedScene?.Y); set => SceneNumber(value, "Scene 위치", (s, v) => s.Y = v); }
    public string SceneWidth { get => Number(SelectedScene?.Width); set => SceneNumber(value, "Scene 크기", (s, v) => s.Width = v); }
    public string SceneHeight { get => Number(SelectedScene?.Height); set => SceneNumber(value, "Scene 크기", (s, v) => s.Height = v); }
    public string? SceneMonitor { get => SelectedScene?.MonitorId; set { if (value is not null && value != SelectedScene?.MonitorId) SceneChange("모니터 변경", s => { s.MonitorId = value; s.X = 64; s.Y = 72; }); } }
    public bool SceneVisible { get => SelectedScene?.Visible ?? false; set => SceneChange(value ? "Scene 표시" : "Scene 숨김", s => s.Visible = value); }
    public bool SceneActive { get => SelectedScene?.Active ?? false; set => SceneChange("Scene 활성화", s => s.Active = value); }
    public bool SceneClickThrough { get => SelectedScene?.ClickThrough ?? true; set => SceneChange("클릭 통과", s => s.ClickThrough = value); }
    public AnimationProfile SceneAnimation { get => SelectedScene?.AnimationProfile ?? AnimationProfile.Off; set => SceneChange("Scene 애니메이션", s => s.AnimationProfile = value); }
    public string ElementName { get => SelectedElement?.Name ?? ""; set => ElementChange("요소 이름", e => e.Name = value); }
    public string ElementX { get => Number(SelectedElement?.X); set => ElementNumber(value, (e, v) => e.X = v); }
    public string ElementY { get => Number(SelectedElement?.Y); set => ElementNumber(value, (e, v) => e.Y = v); }
    public string ElementWidth { get => Number(SelectedElement?.Width); set => ElementNumber(value, (e, v) => e.Width = v); }
    public string ElementHeight { get => Number(SelectedElement?.Height); set => ElementNumber(value, (e, v) => e.Height = v); }
    public string ElementRotation { get => Number(SelectedElement?.Rotation); set => ElementNumber(value, (e, v) => e.Rotation = v); }
    public string ElementScale { get => Number(SelectedElement?.Scale); set => ElementNumber(value, (e, v) => e.Scale = v); }
    public string ElementOpacity { get => Number(SelectedElement?.Opacity * 100); set => ElementNumber(value, (e, v) => e.Opacity = v / 100); }
    public bool ElementVisible { get => SelectedElement?.Visible ?? false; set => ElementChange("요소 표시", e => e.Visible = value); }
    public bool ElementLocked { get => SelectedElement?.Locked ?? false; set => ElementChange("잠금 변경", e => e.Locked = value, true); }
    public string ElementText { get => SelectedElement?.Style.Text ?? ""; set => ElementChange("텍스트 변경", e => e.Style.Text = value); }
    public string FontFamily { get => SelectedElement?.Style.FontFamily ?? DesignTokens.FontFamily; set => ElementChange("글꼴 변경", e => e.Style.FontFamily = value); }
    public string FontSize { get => Number(SelectedElement?.Style.FontSize); set => ElementNumber(value, (e, v) => e.Style.FontSize = v); }
    public string FontWeight { get => Number(SelectedElement?.Style.FontWeight); set => ElementNumber(value, (e, v) => { if (v != Math.Truncate(v)) throw new ArgumentException("글꼴 굵기는 100~900 사이 정수로 입력하세요."); e.Style.FontWeight = (int)v; }); }
    public string ForegroundColor { get => SelectedElement?.Style.Foreground ?? "#FFFFFFFF"; set => ElementChange("색상 변경", e => e.Style.Foreground = value); }
    public string BackgroundColor { get => SelectedElement?.Style.Background ?? "#FF181E28"; set => ElementChange("배경 변경", e => e.Style.Background = value); }
    public string ElementTypeLabel => SelectedElement?.Type.ToString() ?? (selection.Count > 1 ? $"{selection.Count}개 요소 선택" : "요소를 선택하세요");
    public bool ShowOnStartup { get => Store.Document.Settings.ShowOverlaysOnStartup; set => Setting("시작 표시", s => s.ShowOverlaysOnStartup = value); }
    public bool DefaultClickThrough { get => Store.Document.Settings.DefaultClickThrough; set => Setting("기본 클릭 통과", s => s.DefaultClickThrough = value); }
    public bool HotkeyEnabled { get => Store.Document.Settings.HotkeyEnabled; set => Setting("단축키", s => s.HotkeyEnabled = value); }
    public int HotkeyFunction { get => Store.Document.Settings.HotkeyFunction; set => Setting("단축키", s => s.HotkeyFunction = value); }
    public bool GridSnap { get => Store.Document.Settings.GridSnap; set => Setting("그리드 스냅", s => s.GridSnap = value); }
    public string DefaultMonitor { get => string.IsNullOrEmpty(Store.Document.Settings.DefaultMonitorId) ? (Monitors.FirstOrDefault(m => !m.Primary) ?? Monitors.First()).Device : Store.Document.Settings.DefaultMonitorId; set { if (value is not null && value != DefaultMonitor) Setting("기본 모니터", s => s.DefaultMonitorId = value); } }
    public AnimationProfile DefaultAnimation { get => Store.Document.Settings.DefaultAnimation; set => Setting("기본 애니메이션", s => s.DefaultAnimation = value); }
    public bool DemoMode { get => Media.DemoMode; set { Media.SetDemo(value); NotifyAll(); } }
    public string SelectedMedia { get => Media.SelectedKey; set { if (value is not null && value != Media.SelectedKey) Media.Select(value); } }
    public string TrackTitle => Media.State.Title;
    public string TrackArtist => Media.State.Artist;
    public string ConnectionStatus => Media.State.IsDemo ? "DEMO · 실제 미디어 재생 아님" : Media.State.Source.Length > 0 ? "연결됨 · " + Media.State.Source : Media.State.Status;
    public string MediaStatus => Media.State.CommandStatus.Length > 0 ? Media.State.CommandStatus : Media.State.Status;
    public string PlayLabel => Media.State.IsPlaying ? "일시정지" : "재생";
    public double Progress => Media.State.Progress * 100;
    public string PositionText => Media.State.PositionSeconds is double p && Media.State.DurationSeconds is double d ? TimeSpan.FromSeconds(p).ToString(@"m\:ss") + " / " + TimeSpan.FromSeconds(d).ToString(@"m\:ss") + " · 플레이어 제공 위치" : "재생 위치 정보 없음";
    public BitmapSource? AlbumArt => ElementDrawing.Artwork(Media.State);
    public string ActiveSceneSummary => $"{(RuntimeSuspended ? 0 : Store.Document.Scenes.Count(s => s.Active && s.Visible))}개 표시 중 / {Store.Document.Scenes.Count}개 Scene";
    public string DataPath => Repository.DirectoryPath;
    public string HotkeyStatus { get; internal set; } = "단축키 준비 중";
    public bool IsScreenEditing => EditingSceneId.HasValue;
    public ICommand NavigateCommand { get; }
    public ICommand AddSceneCommand { get; }
    public ICommand DuplicateSceneCommand { get; }
    public ICommand DeleteSceneCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand AddElementCommand { get; }
    public ICommand DeleteElementCommand { get; }
    public ICommand DuplicateElementCommand { get; }
    public ICommand GroupCommand { get; }
    public ICommand UngroupCommand { get; }
    public ICommand LayerUpCommand { get; }
    public ICommand LayerDownCommand { get; }
    public ICommand ScreenEditCommand { get; }
    public ICommand ImportImageCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand DemoCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }
    internal MainViewModel(SceneStore store, MediaService media, SceneRepository repository, AppLog log)
    {
        Store = store; Media = media; Repository = repository; Log = log; RefreshMonitors(); sceneId = store.Document.Scenes.FirstOrDefault()?.Id;
        RelayCommand C(Action action, Func<bool>? can = null) => new(_ => { if (!PointerGestureActive) Run(action); }, _ => !PointerGestureActive && (can?.Invoke() ?? true));
        NavigateCommand = new RelayCommand(p => Page = p?.ToString() ?? "Home");
        ToggleOverlaysCommand = C(() => ToggleOverlaysRequested?.Invoke(), () => !IsScreenEditing);
        AddSceneCommand = C(AddScene, () => SceneManagementEnabled); DuplicateSceneCommand = C(() => { sceneId = Store.DuplicateScene(sceneId!.Value); Refresh(); }, () => HasScene && SceneManagementEnabled);
        DeleteSceneCommand = C(() => { var id = sceneId; Store.Execute("Scene 삭제", d => d.Scenes.RemoveAll(s => s.Id == id)); }, () => HasScene && SceneManagementEnabled);
        SaveCommand = C(() => { if (CommitInputRequested?.Invoke() != false) _ = SaveAsync(); }, () => !Store.IsEditing); UndoCommand = C(Store.Undo, () => Store.CanUndo); RedoCommand = C(Store.Redo, () => Store.CanRedo);
        AddElementCommand = C(AddElement, () => HasScene); DeleteElementCommand = C(() => Store.RemoveElements(sceneId!.Value, selection.ToArray()), () => selection.Count > 0);
        DuplicateElementCommand = C(() => Select(Store.DuplicateElements(sceneId!.Value, selection.ToArray())), () => selection.Count > 0);
        GroupCommand = C(() => { var id = Store.GroupElements(sceneId!.Value, selection.ToArray()); Select([id]); }, () => selection.Count >= 2);
        UngroupCommand = C(() => { Store.UngroupElements(sceneId!.Value, SelectedElement!.Id); Select([]); }, () => SelectedElement?.Type == ElementKind.Group);
        LayerUpCommand = C(() => Store.ChangeZOrder(sceneId!.Value, SelectedElement!.Id, 1), () => HasElement); LayerDownCommand = C(() => Store.ChangeZOrder(sceneId!.Value, SelectedElement!.Id, -1), () => HasElement);
        ScreenEditCommand = C(() => ScreenEditRequested?.Invoke(), () => HasScene && !Store.IsEditing && !EditingSceneId.HasValue); ImportImageCommand = C(() => ImportImageRequested?.Invoke(), () => SelectedElement?.Type == ElementKind.Image);
        PlayCommand = C(() => _ = Media.CommandAsync("toggle"), () => Media.State.CanToggle && !Media.Busy); PreviousCommand = C(() => _ = Media.CommandAsync("previous"), () => Media.State.CanPrevious && !Media.Busy); NextCommand = C(() => _ = Media.CommandAsync("next"), () => Media.State.CanNext && !Media.Busy);
        DemoCommand = C(() => DemoMode = !DemoMode); RefreshMonitorsCommand = C(RefreshMonitors);
        autosave = new(DispatcherPriority.Background, Dispatcher.CurrentDispatcher) { Interval = TimeSpan.FromSeconds(2) }; autosave.Tick += async (_, _) => { autosave.Stop(); if (!Store.IsEditing) await SaveAsync(false); };
        store.Changed += StoreChanged; media.Changed += MediaChanged; Refresh();
    }
    internal void Run(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ValidationFailures++; Status = "변경하지 못했습니다: " + e.Message; Log.Error("사용자 명령", e); NotifyAll();
            // A binding ignores reentrant source notifications during its own setter.
            // Refresh once it has returned so a rejected value cannot block the next command.
            Dispatcher.CurrentDispatcher.BeginInvoke(NotifyAll, DispatcherPriority.DataBind);
        }
    }
    void RefreshMonitors() { Monitors.Clear(); foreach (var m in Native.Monitors()) Monitors.Add(m); }
    void AddScene()
    {
        string monitor = Store.Document.Settings.DefaultMonitorId; if (!Monitors.Any(m => m.Device == monitor)) monitor = (Monitors.FirstOrDefault(m => !m.Primary) ?? Monitors.First()).Device;
        var s = SceneFactory.Music(monitor, Store.Document.Settings); s.Name = "Music Widget " + (Store.Document.Scenes.Count + 1); s.X += Store.Document.Scenes.Count * 28; s.Y += Store.Document.Scenes.Count * 28;
        Store.Execute("Scene 생성", d => d.Scenes.Add(s)); sceneId = s.Id; Page = "Editor"; Select([s.Elements.First(e => e.Type == ElementKind.TrackTitle).Id]); Refresh(); Status = RuntimeSuspended ? "Scene을 만들었습니다. Home의 모두 표시 / 숨김 버튼으로 화면에 표시하세요." : "Scene이 화면에 표시됩니다. 요소를 끌어서 배치해 보세요.";
    }
    void AddElement()
    {
        var e = new SceneElement { Type = AddKind, Name = AddKind.ToString(), X = 36, Y = 36, ZIndex = (SelectedScene?.Elements.Select(e => e.ZIndex).DefaultIfEmpty(0).Max() ?? 0) + 1, Width = AddKind is ElementKind.Image or ElementKind.AlbumArtwork ? 96 : 180, Height = AddKind is ElementKind.Image or ElementKind.AlbumArtwork ? 96 : 44 };
        Store.AddElement(sceneId!.Value, e); Select([e.Id]);
    }
    internal void Select(IEnumerable<Guid> ids) { selection.Clear(); foreach (var id in ids) if (SelectedScene?.Elements.Any(e => e.Id == id) == true) selection.Add(id); NotifyAll(); VisualChanged?.Invoke(this, EventArgs.Empty); CommandManager.InvalidateRequerySuggested(); }
    void StoreChanged(object? sender, EventArgs e) { Refresh(); autosave.Stop(); if (!Store.IsEditing) autosave.Start(); SettingsChanged?.Invoke(); }
    void MediaChanged(object? sender, EventArgs e)
    {
        if (!MediaSessions.SequenceEqual(Media.Sessions)) { MediaSessions.Clear(); foreach (var s in Media.Sessions) MediaSessions.Add(s); }
        foreach (var name in new[] { nameof(SelectedMedia), nameof(DemoMode), nameof(TrackTitle), nameof(TrackArtist), nameof(ConnectionStatus), nameof(MediaStatus), nameof(PlayLabel), nameof(Progress), nameof(PositionText), nameof(AlbumArt) }) Notify(name);
        VisualChanged?.Invoke(this, EventArgs.Empty); CommandManager.InvalidateRequerySuggested();
    }
    internal void Refresh()
    {
        IsRefreshing = true;
        try
        {
            if (sceneId is null || Store.Document.Scenes.All(s => s.Id != sceneId)) sceneId = Store.Document.Scenes.FirstOrDefault()?.Id;
            var scenes = Store.Document.Scenes.Select(s => new SceneListItem(s.Id, s.Name, $"{s.Elements.Count}개 요소 · {(s.Active && s.Visible ? "표시 중" : "숨김")}", s.Visible)).ToArray();
            if (!Scenes.SequenceEqual(scenes)) { Scenes.Clear(); foreach (var s in scenes) Scenes.Add(s); }
            var layers = SelectedScene is null ? [] : SceneMath.Flatten(SelectedScene).Reverse().Select(f => new LayerItem(f.Element.Id, f.Element.Name, $"{(f.Locked ? "잠금 · " : "")}{(!f.Visible ? "숨김 · " : "")}{(f.Element.ParentGroup.HasValue ? "↳ " : "")}{f.Element.Type}")).ToArray();
            if (!Layers.SequenceEqual(layers)) { Layers.Clear(); foreach (var l in layers) Layers.Add(l); }
            selection.RemoveWhere(id => SelectedScene?.Elements.All(e => e.Id != id) != false); NotifyAll();
        }
        finally { IsRefreshing = false; }
        VisualChanged?.Invoke(this, EventArgs.Empty); CommandManager.InvalidateRequerySuggested();
    }
    void SceneChange(string label, Action<Scene> change) { if (sceneId is Guid id && !IsRefreshing) Run(() => Store.Execute(label, d => change(d.Scenes.Single(s => s.Id == id)))); }
    void ElementChange(string label, Action<SceneElement> change, bool allowLocked = false) { if (SelectedElement is { } e && !IsRefreshing) Run(() => Store.UpdateElement(sceneId!.Value, e.Id, label, change, allowLocked)); }
    static string Number(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
    static double Parse(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new ArgumentException("유효한 숫자를 입력하세요.");
    void ElementNumber(string value, Action<SceneElement, double> change) => Run(() => ElementChange("요소 속성", e => change(e, Parse(value))));
    void SceneNumber(string value, string label, Action<Scene, double> change) => Run(() => SceneChange(label, s => change(s, Parse(value))));
    void Setting(string label, Action<AppSettings> change) { if (IsRefreshing || Store.IsEditing) return; Run(() => Store.Execute(label, d => change(d.Settings))); }
    internal void BeginScreenEdit() { if (SelectedScene is null) return; EditingSceneId = SelectedScene.Id; Store.BeginTransaction("화면에서 편집"); Status = "화면에서 편집 중 · 저장 또는 취소로 마무리하세요."; NotifyAll(); }
    internal void EndScreenEdit(bool save) { EditingSceneId = null; if (save) Store.CommitTransaction(); else Store.CancelTransaction(); Status = save ? "화면 편집을 적용했습니다." : "화면 편집을 취소했습니다."; Refresh(); }
    internal async Task SaveAsync(bool announce = true)
    {
        if (Store.IsEditing) return; await saveGate.WaitAsync();
        try { if (Store.IsEditing) return; if (savedRevision == Store.Revision) { LastSaveSucceeded = true; if (announce) Status = "모든 변경 사항이 저장되어 있습니다."; return; } int revision = Store.Revision; var snapshot = SceneJson.Clone(Store.Document); await Task.Run(() => Repository.Save(snapshot)); savedRevision = revision; LastSaveSucceeded = true; if (announce) Status = "Scene을 저장했습니다."; Log.Write("document_saved", new { revision, scenes = snapshot.Scenes.Count }); }
        catch (Exception e) { LastSaveSucceeded = false; Status = "저장하지 못했습니다. 기존 정상 파일은 유지됩니다: " + e.Message; Log.Error("Scene 저장", e); }
        finally { saveGate.Release(); }
    }
    internal void Stop() { autosave.Stop(); Store.Changed -= StoreChanged; Media.Changed -= MediaChanged; }
    internal void NotifyAll() => PropertyChanged?.Invoke(this, new(string.Empty));
    void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
