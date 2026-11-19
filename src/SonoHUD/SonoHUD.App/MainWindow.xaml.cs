using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SonoHUD.Core;
using SonoHUD.App.Platform;
using SonoHUD.App.Rendering;
using SonoHUD.App.ViewModels;
using SonoHUD.App.Views;

namespace SonoHUD.App;

public partial class MainWindow : Window
{
    readonly MainViewModel vm;
    readonly CompositionRuntime runtime;
    readonly EditorSurface canvas;
    readonly DispatcherTimer? diagnostics;
    ScreenEditorWindow? screenEditor;
    HwndSource? source; IntPtr hwnd;
    bool selectionSync, suspended, closing, canClose;
    (bool, int)? hotkeyConfig;
    internal MainWindow(MainViewModel viewModel, bool automation, bool secondary)
    {
        vm = viewModel; InitializeComponent(); DataContext = vm; vm.CommitInputRequested = CommitFocusedInput;
        canvas = new() { ViewModel = vm }; CanvasHost.Content = canvas;
        ElementDrawing.AssetDirectory = Path.Combine(vm.Repository.DirectoryPath, "assets");
        runtime = new(vm.Log); runtime.CommandRequested += OnMediaCommand;
        suspended = !vm.ShowOnStartup;
        vm.RuntimeSuspended = suspended; vm.ToggleOverlaysRequested += ToggleOverlays;
        vm.VisualChanged += VisualChanged; vm.ScreenEditRequested += StartScreenEditor; vm.ImportImageRequested += ImportImage; vm.SettingsChanged += RegisterHotkey;
        ShowActivated = !secondary;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd); source.AddHook(WindowMessage);
            var monitors = Native.Monitors(); var monitor = monitors.Where(m => !m.Primary).OrderByDescending(m => m.Work.Width).FirstOrDefault() ?? monitors.First();
            int width = Math.Min(1460, monitor.Work.Width - 40), height = Math.Min(920, monitor.Work.Height - 40);
            Native.SetWindowPos(hwnd, IntPtr.Zero, monitor.Work.Left + Math.Max(10, (monitor.Work.Width - width) / 2), monitor.Work.Top + Math.Max(10, (monitor.Work.Height - height) / 2), width, height, 0x14);
            RegisterHotkey(); RefreshVisuals();
        };
        Closing += ClosingAsync;
        PreviewMouseDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is not TextBox field || field.IsMouseOver) return;
            // Committing a name rebuilds the item source. Preserve the clicked item's identity
            // before that happens, otherwise the first click targets a detached ListBoxItem.
            object? clickedItem = null;
            for (var node = e.OriginalSource as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is ListBoxItem item) { clickedItem = item.DataContext; break; }
            if (!CommitFocusedInput()) { e.Handled = true; return; }
            if (clickedItem is LayerItem layer)
            {
                var ids = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? vm.Selection.ToHashSet() : [];
                if (!ids.Add(layer.Id)) ids.Remove(layer.Id); vm.Select(ids); LayerList.Focus(); e.Handled = true;
            }
            else if (clickedItem is SceneListItem scene && vm.SceneManagementEnabled)
            { vm.SelectedSceneId = scene.Id; Keyboard.ClearFocus(); e.Handled = true; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            { e.Handled = true; if (CommitFocusedInput() && vm.SaveCommand.CanExecute(null)) vm.SaveCommand.Execute(null); }
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { canvas.CancelGesture(); if (screenEditor is not null) FinishScreenEditor(false); } };
        if (automation) { diagnostics = new(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => WriteDiagnostics(), Dispatcher); diagnostics.Start(); }
    }
    void OnMediaCommand(string command) => _ = vm.Media.CommandAsync(command);
    bool CommitFocusedInput()
    {
        if (Keyboard.FocusedElement is not TextBox field) return true;
        var binding = field.GetBindingExpression(TextBox.TextProperty);
        if (binding is null || !binding.IsDirty) return true;
        int errors = vm.ValidationFailures; binding.UpdateSource();
        if (errors != vm.ValidationFailures) binding.UpdateTarget();
        return errors == vm.ValidationFailures && !Validation.GetHasError(field);
    }
    void VisualChanged(object? sender, EventArgs e) => RefreshVisuals();
    void RefreshVisuals()
    {
        if (closing) return;
        try { runtime.Update(vm.Store.Document, vm.Media.State, vm.EditingSceneId, suspended); canvas.InvalidateVisual(); screenEditor?.Refresh(); }
        catch (Exception e) { vm.Log.Error("오버레이 반영", e); vm.Status = "오버레이를 갱신하지 못했습니다: " + e.Message; }
        selectionSync = true;
        try { LayerList.SelectedItems.Clear(); foreach (var item in vm.Layers.Where(l => vm.Selection.Contains(l.Id))) LayerList.SelectedItems.Add(item); }
        finally { selectionSync = false; }
    }
    void LayersSelected(object sender, SelectionChangedEventArgs e)
    {
        if (selectionSync || vm.IsRefreshing) return;
        vm.Select(LayerList.SelectedItems.Cast<LayerItem>().Select(l => l.Id).ToArray());
    }
    void RegisterHotkey()
    {
        if (hwnd == IntPtr.Zero || closing) return; var config = (vm.HotkeyEnabled, vm.HotkeyFunction); if (hotkeyConfig == config) return;
        Native.UnregisterHotKey(hwnd, 1); hotkeyConfig = config;
        bool ok = !config.Item1 || Native.RegisterHotKey(hwnd, 1, 0x4000 | 1 | 2, (uint)(0x70 + config.Item2 - 1));
        vm.HotkeyStatus = !config.Item1 ? "전역 단축키 꺼짐" : ok ? $"Ctrl + Alt + F{config.Item2} · 표시 / 숨김" : "단축키 등록 실패 · 다른 키를 선택하세요";
        vm.Log.Write("hotkey", new { enabled = config.Item1, key = config.Item2, registered = ok && config.Item1 }); vm.NotifyAll();
    }
    IntPtr WindowMessage(IntPtr h, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (message == 0x312 && wp.ToInt32() == 1 && !vm.IsScreenEditing)
        {
            var before = Native.GetForegroundWindow(); ToggleOverlays();
            vm.Log.Write("hotkey_toggle", new { suspended, focusBefore = before.ToInt64(), focusAfter = Native.GetForegroundWindow().ToInt64() }); handled = true;
        }
        return IntPtr.Zero;
    }
    void ToggleOverlays() { suspended = !suspended; vm.RuntimeSuspended = suspended; vm.NotifyAll(); RefreshVisuals(); vm.Status = suspended ? "모든 오버레이를 일시적으로 숨겼습니다." : "오버레이 표시를 복원했습니다."; }
    void StartScreenEditor()
    {
        if (screenEditor is not null || vm.SelectedScene is null || !CommitFocusedInput()) return;
        vm.BeginScreenEdit(); RefreshVisuals();
        screenEditor = new(vm) { Owner = this }; screenEditor.Finished += FinishScreenEditor;
        screenEditor.Show(); screenEditor.Refresh(); vm.Log.Write("screen_edit_open", new { scene = vm.EditingSceneId });
    }
    void FinishScreenEditor(bool save)
    {
        var editor = screenEditor; if (editor is null) return; editor.CancelGesture(); screenEditor = null; editor.Finished -= FinishScreenEditor;
        vm.EndScreenEdit(save); editor.AllowClose = true; editor.Close(); RefreshVisuals();
        vm.Log.Write("screen_edit_close", new { save }); if (save) _ = vm.SaveAsync();
    }
    void ImportImage()
    {
        vm.Log.Write("image_import_requested", new { selection = vm.Selection.Count });
        if (vm.SelectedElement is not { } element || vm.SelectedScene is not { } scene) return;
        var dialog = new OpenFileDialog { Title = "SonoHUD · 이미지 가져오기", Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        var accepted = dialog.ShowDialog(this); vm.Log.Write("image_import_dialog_closed", new { accepted }); if (accepted != true) return;
        vm.Run(() =>
        {
            if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new InvalidDataException("16MiB 이하의 이미지를 선택하세요.");
            using (var stream = File.OpenRead(dialog.FileName)) { var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad); if (decoder.Frames.Count == 0) throw new InvalidDataException("이미지를 읽을 수 없습니다."); }
            byte[] data = File.ReadAllBytes(dialog.FileName); string name = Convert.ToHexString(SHA256.HashData(data)) + Path.GetExtension(dialog.FileName).ToLowerInvariant();
            Directory.CreateDirectory(ElementDrawing.AssetDirectory); string target = Path.Combine(ElementDrawing.AssetDirectory, name); if (!File.Exists(target)) File.WriteAllBytes(target, data);
            vm.Store.UpdateElement(scene.Id, element.Id, "이미지 변경", e => e.Style.ImagePath = name);
        });
    }
    void WriteDiagnostics()
    {
        if (closing) return;
        try
        {
            var point = canvas.IsVisible ? canvas.PointToScreen(new(0, 0)) : new Point();
            var data = new { utc = DateTimeOffset.UtcNow, pid = Environment.ProcessId, hwnd = hwnd.ToInt64(), foreground = Native.GetForegroundWindow().ToInt64(), document = vm.Store.Document, revision = vm.Store.Revision, selection = vm.Selection, scene = vm.SelectedSceneId, page = vm.Page, screenEditing = vm.EditingSceneId, screenHwnd = screenEditor is null ? 0 : new WindowInteropHelper(screenEditor).Handle.ToInt64(), overlayWindows = runtime.Snapshot(), windowCount = runtime.WindowCount, nodeCount = runtime.NodeCount, uploads = runtime.UploadCount, canvas = new { x = point.X, y = point.Y, width = canvas.ActualWidth, height = canvas.ActualHeight, zoom = canvas.Zoom, originX = canvas.Origin.X, originY = canvas.Origin.Y }, media = new { vm.Media.State.Source, vm.Media.State.IsDemo, vm.Media.State.IsPlaying, vm.Media.State.Status, vm.Media.State.CommandStatus, vm.Media.State.PositionSeconds, vm.Media.State.DurationSeconds, vm.Media.State.ArtworkKey, titleHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(vm.Media.State.Title))) }, sessions = vm.Media.Sessions, vm.Status, vm.HotkeyStatus };
            string path = Path.Combine(vm.Repository.DirectoryPath, "diagnostics.json"); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions(SceneJson.Options) { IncludeFields = true })); File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (canClose) return; e.Cancel = true; if (closing || !CommitFocusedInput()) return;
        canvas.CancelGesture();
        if (screenEditor is not null) FinishScreenEditor(false);
        closing = true; IsEnabled = false; diagnostics?.Stop(); await vm.SaveAsync(false);
        if (!vm.LastSaveSucceeded) { closing = false; IsEnabled = true; diagnostics?.Start(); vm.Status = "저장되지 않은 변경 사항이 있습니다. 저장 경로를 확인한 후 다시 저장하세요."; return; }
        vm.Stop(); vm.ToggleOverlaysRequested -= ToggleOverlays;
        vm.VisualChanged -= VisualChanged; vm.ScreenEditRequested -= StartScreenEditor; vm.ImportImageRequested -= ImportImage; vm.SettingsChanged -= RegisterHotkey;
        vm.Media.Dispose(); Native.UnregisterHotKey(hwnd, 1); source?.RemoveHook(WindowMessage); runtime.CommandRequested -= OnMediaCommand;
        try { await runtime.ShutdownAsync(); } catch (Exception ex) { vm.Log.Error("렌더러 종료", ex); }
        ElementDrawing.ClearCache(); vm.Log.Write("app_shutdown_complete"); canClose = true; Close(); Application.Current.Shutdown();
    }
}
