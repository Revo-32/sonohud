// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Threading;
using SonoHUD.App.Platform;
using SonoHUD.App.ViewModels;
using SonoHUD.Core;

namespace SonoHUD.App.Taskbar;

internal sealed class TaskbarRuntime : IDisposable
{
    readonly StudioViewModel vm;
    readonly DispatcherTimer timer;
    TaskbarConfig settings;
    Task<TaskbarSnapshot>? discovery;
    TaskbarWidgetWindow? window;
    bool disposed,refreshing;
    int epoch,failures;
    nint failedShell;
    TaskbarSnapshot? snapshot;
    Rect? placement;
    internal nint WidgetHandle=>window?.Handle??0;
    internal TaskbarWidgetWindow? Widget=>window;
    internal TaskbarSnapshot? Snapshot=>snapshot;
    internal bool Enabled=>timer.IsEnabled;
    internal event Action? SettingsRequested;
    internal TaskbarRuntime(StudioViewModel viewModel)
    {
        vm=viewModel;settings=vm.Document.Taskbar with{};
        timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(1500)};
        timer.Tick+=Tick;vm.SettingsChanged+=SettingsChanged;vm.Media.Changed+=MediaChanged;vm.TaskbarRetryRequested+=Retry;
        ApplySettings();
    }
    void Status(string message){if(vm.TaskbarStatus==message)return;vm.TaskbarStatus=message;vm.NotifyTaskbarStatus();vm.Log.Write("taskbar_status",new{message});}
    void SettingsChanged(){window?.UpdateMedia();if(settings==vm.Document.Taskbar)return;settings=vm.Document.Taskbar with{};epoch++;failures=0;placement=null;ApplySettings();}
    void ApplySettings()
    {
        if(!settings.Enabled){timer.Stop();Release();Status("꺼짐 · 데스크톱 오버레이와 별도로 켤 수 있습니다.");return;}
        timer.Start();_ = RefreshAsync();
    }
    void MediaChanged(object? sender,EventArgs e){window?.UpdateMedia();}
    void Tick(object? sender,EventArgs e)
    {
        if(failures>=3)
        {
            // A secondary taskbar can disappear and return without restarting the primary one.
            if(!ShellReturned(ref failedShell,TaskbarShell.FindHandle(settings.MonitorId)))return;
            failures=0;
        }
        _ = RefreshAsync();
    }
    internal static bool ShellReturned(ref nint previous,nint current)
    {
        if(current==previous)return false;
        previous=current;
        return current!=0;
    }
    internal void Retry(){if(disposed||!settings.Enabled)return;epoch++;failures=0;Release();timer.Start();_ = RefreshAsync();}
    internal async Task RefreshAsync()
    {
        if(disposed||!settings.Enabled||refreshing||failures>=3)return;
        if(discovery is {IsCompleted:false}){Status("작업 표시줄 응답을 기다리는 중입니다. 반복 연결은 중지했습니다.");return;}
        refreshing=true;int version=epoch;nint own=WidgetHandle;
        try
        {
            var selected=settings;
            discovery=Task.Run(()=>TaskbarShell.Read(selected.MonitorId,own));
            if(await Task.WhenAny(discovery,Task.Delay(2500))!=discovery)
            {
                _=discovery.ContinueWith(t=>_ = t.Exception,TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("작업 표시줄 응답이 늦어 위젯을 숨겼습니다. 다시 연결하거나 SonoHUD를 다시 시작하세요.");
            }
            var found=await discovery;
            if(disposed||!settings.Enabled||version!=epoch)return;
            // The shell HWND may survive while our child is detached. Cached geometry
            // alone cannot prove a live attachment; rebuild the owned widget in that case.
            if(window is not null&&(!Native.IsWindow(window.Handle)||snapshot?.Handle!=found.Handle||TaskbarShell.GetParent(window.Handle)!=found.Handle))Release();
            window??=CreateWidget();
            double desired=window.DesiredWidth(selected);
            var next=TaskbarShell.Place(found,desired,selected.Position);
            if(next is null)throw new InvalidOperationException(found.ForeignWidget?"다른 위젯과 겹치지 않는 공간이 없습니다. FluentFlyout 위젯을 사용 중이면 직접 끄거나 표시 위치를 바꾼 뒤 다시 연결하세요.":"작업 표시줄에 충분한 빈 공간이 없습니다. 고정 폭을 줄이거나 표시 위치·모니터를 바꾸세요.");
            bool attach=window.Handle==0;
            bool changed=placement!=next||snapshot?.Dpi!=found.Dpi||snapshot?.Handle!=found.Handle;
            if(changed){window.Configure(selected,found,next.Value);if(attach)window.Attach(found,next.Value);else window.Place(found,next.Value);}
            snapshot=found;placement=next;failures=0;
            Status("작업 표시줄에 표시 중"+(found.ForeignWidget?" · 다른 위젯 영역을 피했습니다.":"")+(found.Vertical?" · 세로 배치":""));
        }
        catch(Exception ex)
        {
            if(disposed||version!=epoch)return;
            Release();failures++;
            if(failures>=3)failedShell=TaskbarShell.FindHandle(settings.MonitorId);
            Status(ex.Message+(failures>=3?" 자동 재시도를 중지했습니다. ‘다시 연결’을 누르세요.":""));vm.Log.Error("taskbar_connect",ex);
        }
        finally {refreshing=false;}
    }
    TaskbarWidgetWindow CreateWidget()
    {
        var value=new TaskbarWidgetWindow(vm);value.SettingsRequested+=OpenSettings;value.PlacementInvalidated+=Invalidate;return value;
    }
    void Invalidate(){placement=null;_ = RefreshAsync();}
    void OpenSettings()=>SettingsRequested?.Invoke();
    void Release()
    {
        var old=window;window=null;snapshot=null;placement=null;
        if(old is null)return;old.SettingsRequested-=OpenSettings;old.PlacementInvalidated-=Invalidate;
        try {old.Release();}catch(Exception ex){vm.Log.Error("taskbar_release",ex);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;epoch++;timer.Stop();timer.Tick-=Tick;
        vm.SettingsChanged-=SettingsChanged;vm.Media.Changed-=MediaChanged;vm.TaskbarRetryRequested-=Retry;Release();
    }
}
