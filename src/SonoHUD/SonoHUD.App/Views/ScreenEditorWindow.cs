using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SonoHUD.App.Platform;
using SonoHUD.App.ViewModels;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

internal sealed class ScreenEditorWindow : Window
{
    readonly MainViewModel vm;
    readonly EditorSurface surface;
    readonly Canvas layout;
    readonly Border toolbar;
    readonly TextBlock handle;
    bool moving;
    DesktopPoint startPointer, startOrigin;
    double startScale;
    SceneDocument? moveSnapshot;
    internal bool AllowClose { get; set; }
    internal event Action<bool>? Finished;
    internal ScreenEditorWindow(MainViewModel vm)
    {
        this.vm = vm;
        Title = "SonoHUD · 화면에서 편집"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        surface = new() { ViewModel = vm, OnScreen = true }; layout = new(); layout.Children.Add(surface);
        toolbar = new() { Width = 340, Height = 42, Background = new SolidColorBrush(Color.FromRgb(27, 34, 44)), BorderBrush = Brushes.MediumAquamarine, BorderThickness = new(1), CornerRadius = new(5), Padding = new(6, 3, 3, 3) };
        layout.Children.Add(toolbar); var row = new DockPanel(); toolbar.Child = row;
        void ActionButton(string text, string id, bool save)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
            System.Windows.Automation.AutomationProperties.SetName(button, text);
            button.Click += (_, _) => { CancelGesture(); Finished?.Invoke(save); };
            DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
        }
        ActionButton("취소 · Esc", "ScreenCancel", false); ActionButton("완료", "ScreenSave", true);
        handle = new() { Text = "⠿ Scene 이동", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, Padding = new(8, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(handle, "SceneMoveHandle");
        System.Windows.Automation.AutomationProperties.SetName(handle, "Scene 전체를 다른 모니터로 이동"); row.Children.Add(handle);
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (vm.SelectedScene is not { } scene) return;
            var monitors = Native.DesktopMonitors(); Native.GetCursorPos(out var point);
            startPointer = new(point.X, point.Y); startOrigin = DesktopLayout.VisibleOrigin(scene, monitors); startScale = DesktopLayout.Resolve(scene, monitors).Scale;
            moveSnapshot = SceneJson.Clone(vm.Store.Document); moving = true; vm.PointerGestureActive = true;
            handle.CaptureMouse(); e.Handled = true;
        };
        handle.MouseMove += (_, _) =>
        {
            if (!moving || vm.SelectedScene is not { } scene) return;
            Native.GetCursorPos(out var point); var monitors = Native.DesktopMonitors();
            vm.Run(() => vm.Store.Execute("Scene 이동", d => DesktopLayout.Move(d.Scenes.Single(s => s.Id == scene.Id), startOrigin, startPointer, new(point.X, point.Y), startScale, monitors)));
        };
        handle.MouseLeftButtonUp += (_, _) => EndMove(false);
        handle.LostMouseCapture += (_, _) => { if (moving) EndMove(true); };
        Content = layout;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtrW(h, -20, new(Native.GetWindowLongPtrW(h, -20).ToInt64() | Native.ToolWindow)); Refresh();
            HwndSource.FromHwnd(h)?.AddHook(DpiMessage);
        };
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (!CancelGesture()) Finished?.Invoke(false); e.Handled = true; }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CancelGesture(); Finished?.Invoke(true); e.Handled = true; }
        };
    }
    IntPtr DpiMessage(IntPtr h, int message, IntPtr wp, IntPtr lp, ref bool handled)
    { if (message == 0x2E0) Dispatcher.BeginInvoke(Refresh); return IntPtr.Zero; }
    void EndMove(bool cancel)
    {
        if (!moving) return; moving = false; vm.PointerGestureActive = false; handle.ReleaseMouseCapture();
        if (cancel && moveSnapshot is not null) vm.Store.RestorePreview(moveSnapshot);
        moveSnapshot = null;
    }
    internal bool CancelGesture()
    { if (moving) { EndMove(true); return true; } return surface.CancelGesture(); }
    internal void Refresh()
    {
        if (vm.SelectedScene is not { } scene) return;
        var h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return;
        var all = Native.DesktopMonitors(); var desktop = DesktopLayout.Union(all.Select(m => m.Bounds));
        Native.SetWindowPos(h, new(-1), (int)desktop.X, (int)desktop.Y, (int)desktop.Width, (int)desktop.Height, 0x10);
        double windowScale = Native.GetDpiForWindow(h) / 96.0;
        surface.Width = desktop.Width / windowScale; surface.Height = desktop.Height / windowScale;
        var origin = DesktopLayout.VisibleOrigin(scene, all); double sceneScale = DesktopLayout.Resolve(scene, all).Scale;
        surface.ScreenOrigin = new((origin.X - desktop.X) / windowScale, (origin.Y - desktop.Y) / windowScale);
        surface.ScreenZoom = sceneScale / windowScale;
        var monitor = DesktopLayout.Nearest(origin, all); var work = monitor.Work;
        double tx = Math.Clamp(origin.X, work.X, Math.Max(work.X, work.X + work.Width - toolbar.Width * windowScale));
        double ty = Math.Clamp(origin.Y + scene.Height * sceneScale + 2, work.Y, Math.Max(work.Y, work.Y + work.Height - toolbar.Height * windowScale));
        Canvas.SetLeft(toolbar, (tx - desktop.X) / windowScale); Canvas.SetTop(toolbar, (ty - desktop.Y) / windowScale);
        surface.InvalidateVisual();
    }
    void OnClosing(object? sender, CancelEventArgs e)
    { CancelGesture(); if (AllowClose) return; e.Cancel = true; Finished?.Invoke(false); }
}
