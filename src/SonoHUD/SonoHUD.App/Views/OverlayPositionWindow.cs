using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SonoHUD.App.Platform;
using SonoHUD.Core;

namespace SonoHUD.App.Views;

internal sealed class OverlayPositionWindow : Window
{
    readonly Canvas layout = new();
    readonly PositionSurface surface = new();
    readonly Border toolbar;
    readonly Dictionary<OverlayResizeCorner, ResizeGrip> resizeGrips = [];
    Scene scene;
    Scene? gestureSnapshot;
    UIElement? captureOwner;
    DesktopPoint startPointer, startOrigin;
    double startScale;
    bool moving;
    OverlayResizeCorner? resizeCorner;
    DesktopMonitor? resizeMonitor;

    internal bool AllowClose { get; set; }
    internal event Action<Scene>? PositionChanged;
    internal event Action<bool>? Finished;

    internal OverlayPositionWindow(Scene initial, MusicTheme? theme=null)
    {
        scene = Copy(initial);
        Title = "SonoHUD · 오버레이 편집";
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/SonoHUD;component/Views/StudioTheme.xaml",UriKind.Relative) });
        StudioAppearance.Apply(Resources,theme??MusicTheme.Gallery[0]);
        FontFamily=DesignResources.FontFamily;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = layout;
        layout.Children.Add(surface);
        AutomationProperties.SetAutomationId(surface, "PositionSurface");
        AutomationProperties.SetName(surface, "오버레이 전체를 끌어서 위치 조정");
        AttachDrag(surface);
        foreach (var corner in Enum.GetValues<OverlayResizeCorner>())
        {
            var grip = new ResizeGrip(corner);
            AutomationProperties.SetAutomationId(grip, "Resize" + corner);
            AutomationProperties.SetName(grip, corner switch
            {
                OverlayResizeCorner.TopLeft => "왼쪽 위 모서리에서 비율을 유지하며 크기 조정",
                OverlayResizeCorner.TopRight => "오른쪽 위 모서리에서 비율을 유지하며 크기 조정",
                OverlayResizeCorner.BottomLeft => "왼쪽 아래 모서리에서 비율을 유지하며 크기 조정",
                _ => "오른쪽 아래 모서리에서 비율을 유지하며 크기 조정"
            });
            resizeGrips.Add(corner, grip);
            Panel.SetZIndex(grip, 2);
            layout.Children.Add(grip);
            AttachDrag(grip, corner);
        }

        toolbar = new Border
        {
            Width = 390, Height = 52, Padding = new Thickness(6, 4, 0, 0),
            Background = (Brush)FindResource("StudioPanel"),
            BorderBrush = (Brush)FindResource("StudioAccent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
        };
        var row = new DockPanel();
        toolbar.Child = row;
        Panel.SetZIndex(toolbar, 1);
        layout.Children.Add(toolbar);
        void AddAction(string text, string id, bool apply)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin=new Thickness(0,0,6,4) };
            if(apply)button.Style=(Style)FindResource("StudioPrimary");
            AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => { if (apply) EndMove(false); else CancelGesture(); Finished?.Invoke(apply); };
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        AddAction("취소 · Esc", "PositionCancel", false);
        AddAction("완료", "PositionApply", true);
        var handle = new TextBlock
        {
            Text = "이동 · 모서리로 크기", Foreground = (Brush)FindResource("StudioInk"),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, Padding = new Thickness(8, 0, 6, 6)
        };
        AutomationProperties.SetAutomationId(handle, "PositionMoveHandle");
        AutomationProperties.SetName(handle, "오버레이 전체 이동 손잡이");
        row.Children.Add(handle);
        AttachDrag(handle);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtrW(hwnd, -20, new IntPtr(Native.GetWindowLongPtrW(hwnd, -20).ToInt64() | Native.ToolWindow));
            HwndSource.FromHwnd(hwnd)?.AddHook(DpiMessage);
            RefreshGeometry();
        };
        Loaded += (_, _) => RefreshGeometry();
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CancelGesture(); Finished?.Invoke(false); e.Handled = true; }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { EndMove(false); Finished?.Invoke(true); e.Handled = true; }
        };
    }

    static Scene Copy(Scene source) => SceneJson.Clone(new SceneDocument { Scenes = [source] }).Scenes[0];

    static bool TryDimensions(Scene value, out (double Width, double Height) size)
    {
        if (Enum.TryParse<PresetKind>(value.UserSettings.GetValueOrDefault("preset"), out var preset) && Enum.IsDefined(preset))
        { size = OverlayPresets.Dimensions(preset); return true; }
        size = default;
        return false;
    }

    void AttachDrag(UIElement target, OverlayResizeCorner? corner = null)
    {
        target.MouseLeftButtonDown += (_, e) =>
        {
            if (moving || !Native.GetCursorPos(out var pointer)) return;
            if (corner.HasValue && (!TryDimensions(scene, out var initialDimensions) || !scene.Elements.Any(element => element.Type == ElementKind.Group && element.ParentGroup is null))) return;
            var monitors = Native.DesktopMonitors();
            startPointer = new DesktopPoint(pointer.X, pointer.Y);
            startOrigin = DesktopLayout.VisibleOrigin(scene, monitors);
            startScale = DesktopLayout.Resolve(scene, monitors).Scale;
            gestureSnapshot = Copy(scene);
            resizeCorner = corner;
            resizeMonitor = corner.HasValue ? DesktopLayout.Resolve(scene, monitors) : null;
            moving = true;
            captureOwner = target;
            if (!target.CaptureMouse()) { moving = false; captureOwner = null; gestureSnapshot = null; resizeCorner = null; resizeMonitor = null; return; }
            e.Handled = true;
        };
        target.MouseMove += (_, e) =>
        {
            if (!moving || !Native.GetCursorPos(out var pointer)) return;
            if (resizeCorner is { } cornerValue && resizeMonitor is { } monitor && gestureSnapshot is { } original && TryDimensions(original, out var dimensions))
            {
                var resized = OverlayResizeGeometry.Calculate(startOrigin, original.Width / dimensions.Width, dimensions.Width, dimensions.Height,
                    startPointer, new DesktopPoint(pointer.X, pointer.Y), cornerValue, monitor);
                scene.MonitorId = monitor.Id;
                scene.X = (resized.Origin.X - monitor.Bounds.X) / monitor.Scale;
                scene.Y = (resized.Origin.Y - monitor.Bounds.Y) / monitor.Scale;
                scene.Width = resized.Width;
                scene.Height = resized.Height;
                scene.Elements.Single(element => element.Type == ElementKind.Group && element.ParentGroup is null).Scale = resized.Scale;
            }
            else DesktopLayout.Move(scene, startOrigin, startPointer, new DesktopPoint(pointer.X, pointer.Y), startScale, Native.DesktopMonitors());
            PositionChanged?.Invoke(Copy(scene));
            RefreshGeometry();
            e.Handled = true;
        };
        target.MouseLeftButtonUp += (_, e) => { if (moving) { EndMove(false); e.Handled = true; } };
        target.LostMouseCapture += (_, _) => { if (moving) EndMove(true); };
    }

    void EndMove(bool cancel)
    {
        if (!moving) return;
        moving = false;
        resizeCorner = null;
        resizeMonitor = null;
        var captured = captureOwner;
        captureOwner = null;
        captured?.ReleaseMouseCapture();
        if (cancel && gestureSnapshot is not null)
        {
            scene = gestureSnapshot;
            PositionChanged?.Invoke(Copy(scene));
        }
        gestureSnapshot = null;
        RefreshGeometry();
    }

    internal bool CancelGesture()
    {
        if (!moving) return false;
        EndMove(true);
        return true;
    }

    internal void Refresh(Scene value)
    {
        if (moving && value.UserSettings.GetValueOrDefault("preset") != scene.UserSettings.GetValueOrDefault("preset")) EndMove(false);
        scene = Copy(value);
        RefreshGeometry();
    }

    IntPtr DpiMessage(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (message == 0x2E0) Dispatcher.BeginInvoke(RefreshGeometry);
        return IntPtr.Zero;
    }

    void RefreshGeometry()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitors = Native.DesktopMonitors();
        var desktop = DesktopLayout.Union(monitors.Select(m => m.Bounds));
        Native.SetWindowPos(hwnd, new IntPtr(-1), (int)desktop.X, (int)desktop.Y, (int)desktop.Width, (int)desktop.Height, 0x10);
        uint dpi = Native.GetDpiForWindow(hwnd);
        double windowScale = dpi > 0 ? dpi / 96d : 1;
        var origin = DesktopLayout.VisibleOrigin(scene, monitors);
        double sceneScale = DesktopLayout.Resolve(scene, monitors).Scale;
        surface.Width = desktop.Width / windowScale;
        surface.Height = desktop.Height / windowScale;
        surface.OverlayBounds = new Rect((origin.X - desktop.X) / windowScale, (origin.Y - desktop.Y) / windowScale, scene.Width * sceneScale / windowScale, scene.Height * sceneScale / windowScale);
        foreach (var (corner, grip) in resizeGrips)
        {
            grip.Visibility = TryDimensions(scene, out _) ? Visibility.Visible : Visibility.Collapsed;
            bool left = corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.BottomLeft;
            bool top = corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.TopRight;
            Canvas.SetLeft(grip, (left ? surface.OverlayBounds.Left : surface.OverlayBounds.Right) - grip.Width / 2);
            Canvas.SetTop(grip, (top ? surface.OverlayBounds.Top : surface.OverlayBounds.Bottom) - grip.Height / 2);
        }
        var work = DesktopLayout.Nearest(origin, monitors).Work;
        double x = Math.Clamp(origin.X, work.X, Math.Max(work.X, work.X + work.Width - toolbar.Width * windowScale));
        double desiredY = origin.Y + scene.Height * sceneScale + 16 * windowScale;
        if (desiredY + toolbar.Height * windowScale > work.Y + work.Height && origin.Y - (toolbar.Height + 16) * windowScale >= work.Y)
            desiredY = origin.Y - (toolbar.Height + 16) * windowScale;
        double y = Math.Clamp(desiredY, work.Y, Math.Max(work.Y, work.Y + work.Height - toolbar.Height * windowScale));
        Canvas.SetLeft(toolbar, (x - desktop.X) / windowScale);
        Canvas.SetTop(toolbar, (y - desktop.Y) / windowScale);
        surface.InvalidateVisual();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        CancelGesture();
        if (AllowClose) return;
        e.Cancel = true;
        Finished?.Invoke(false);
    }

    sealed class PositionSurface : FrameworkElement
    {
        internal Rect OverlayBounds { get; set; }
        internal PositionSurface() { Cursor = Cursors.SizeAll; Focusable = true; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            // Only the overlay body receives input; the rest of the virtual desktop stays alpha zero.
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(2, 0, 0, 0)), new Pen((Brush)FindResource("StudioFocus"), 1.5), OverlayBounds);
        }
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }

    sealed class ResizeGrip : FrameworkElement
    {
        internal ResizeGrip(OverlayResizeCorner corner)
        {
            Width = Height = 18;
            Cursor = corner is OverlayResizeCorner.TopLeft or OverlayResizeCorner.BottomRight ? Cursors.SizeNWSE : Cursors.SizeNESW;
            ToolTip = "드래그하여 크기 조정 · 비율 유지 · 75~150%";
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(2, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            dc.DrawRoundedRectangle((Brush)FindResource("StudioPanel"), new Pen((Brush)FindResource("StudioFocus"), 2),
                new Rect(3, 3, Math.Max(1, ActualWidth - 6), Math.Max(1, ActualHeight - 6)), 2, 2);
        }
        protected override AutomationPeer OnCreateAutomationPeer() => new GripPeer(this);
        sealed class GripPeer(ResizeGrip owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
            protected override string GetClassNameCore() => "OverlayResizeHandle";
        }
    }
}

