using System.Windows.Input;
using SonoHUD.Core;

namespace SonoHUD.App.ViewModels;

internal sealed partial class StudioViewModel
{
    public sealed record TaskbarMonitorOption(string Id, string Label);
    public TaskbarMonitorOption[] TaskbarMonitors =>
        [new("", "주 모니터 (자동)"), .. Monitors.Select(m => new TaskbarMonitorOption(m.Device, m.Label))];
    public string[] TaskbarPositionChoices { get; } = ["시작 쪽의 빈 공간", "끝 쪽의 빈 공간"];
    public string[] TaskbarStyleChoices { get; } = ["은은한 배경", "선명한 배경"];
    public bool TaskbarEnabled { get => Document.Taskbar.Enabled; set => ChangeTaskbar(d => d.Enabled = value); }
    public string TaskbarMonitorId { get => Document.Taskbar.MonitorId; set { if (value is not null) ChangeTaskbar(d => d.MonitorId = value); } }
    public int TaskbarPosition
    {
        get => (int)Document.Taskbar.Position;
        set { if (value is 0 or 1) ChangeTaskbar(d => d.Position = (SonoHUD.Core.TaskbarPosition)value); }
    }
    public bool TaskbarShowTitle { get => Document.Taskbar.ShowTitle; set => ChangeTaskbar(d => d.ShowTitle = value); }
    public bool TaskbarShowArtist { get => Document.Taskbar.ShowArtist; set => ChangeTaskbar(d => d.ShowArtist = value); }
    public bool TaskbarShowArtwork { get => Document.Taskbar.ShowArtwork; set => ChangeTaskbar(d => d.ShowArtwork = value); }
    public bool TaskbarShowControls { get => Document.Taskbar.ShowControls; set => ChangeTaskbar(d => d.ShowControls = value); }
    public bool TaskbarControlsLeft { get => Document.Taskbar.ControlsLeft; set => ChangeTaskbar(d => d.ControlsLeft = value); }
    public bool TaskbarAutoWidth { get => Document.Taskbar.AutoWidth; set => ChangeTaskbar(d => d.AutoWidth = value); }
    public bool TaskbarFixedWidthEnabled => !TaskbarAutoWidth;
    public double TaskbarWidth
    {
        get => Document.Taskbar.Width;
        set { if (double.IsFinite(value)) ChangeTaskbar(d => d.Width = Math.Clamp(Math.Round(value / 10) * 10, 160, 520)); }
    }
    public string TaskbarWidthLabel => $"{TaskbarWidth:0}";
    public bool TaskbarScrollTitle { get => Document.Taskbar.ScrollTitle; set => ChangeTaskbar(d => d.ScrollTitle = value); }
    public bool TaskbarCompact { get => Document.Taskbar.Compact; set => ChangeTaskbar(d => d.Compact = value); }
    public int TaskbarStyle
    {
        get => (int)Document.Taskbar.Style;
        set { if (value is 0 or 1) ChangeTaskbar(d => d.Style = (SonoHUD.Core.TaskbarStyle)value); }
    }
    public string TaskbarStatus { get; internal set; } = "꺼짐 · 데스크톱 오버레이와 별도로 켤 수 있습니다.";
    internal void NotifyTaskbarStatus() => Notify(nameof(TaskbarStatus));
    internal event Action? TaskbarRetryRequested;
    ICommand? taskbarRetryCommand;
    public ICommand TaskbarRetryCommand => taskbarRetryCommand ??= new RelayCommand(
        _ => { if (!stopped && !IsPositioning && TaskbarEnabled) Run(() => TaskbarRetryRequested?.Invoke()); },
        _ => !stopped && !IsPositioning && TaskbarEnabled);

    void ChangeTaskbar(Action<TaskbarConfig> change)
    {
        if (!stopped && !IsPositioning)
            Change("작업 표시줄 위젯 설정 변경", d => change(d.Taskbar));
    }
}
