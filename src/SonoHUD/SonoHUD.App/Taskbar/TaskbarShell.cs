// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted for SonoHUD on 2026-11-15 from TaskbarWindow.xaml.cs and NativeMethods.cs,
// FluentFlyout v2.16.1, 2d742fa4fc90a228803d90db2902740463113e77.
// Changes: monitor device IDs, bounded shell-only UIA reads, occupied-space placement,
// small child HWND, explicit failure reporting; no settings/media/premium dependencies.
// Modified 2026-11-19: share monitor-specific Explorer handle lookup with reconnect detection.
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using SonoHUD.App.Platform;
using SonoHUD.Core;

namespace SonoHUD.App.Taskbar;

internal sealed record TaskbarSnapshot(nint Handle, string MonitorId, uint Dpi, Rect Bounds, Rect[] Occupied, bool ForeignWidget)
{
    internal bool Vertical => Bounds.Height > Bounds.Width;
}

internal static class TaskbarShell
{
    internal const int GwlStyle=-16, GwlExStyle=-20;
    internal const long Child=0x40000000, Popup=0x80000000, NoActivate=0x08000000, ToolWindow=0x80;
    internal delegate bool EnumCallback(nint hwnd,nint data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern nint FindWindowW(string name,string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern nint FindWindowExW(nint parent,nint previous,string name,string? title);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassNameW(nint h,StringBuilder text,int length);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint h,uint command);
    [DllImport("user32.dll",SetLastError=true)] internal static extern nint SetParent(nint child,nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint h);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint h,ref Native.Point point);
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint h);
    [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint a,nint b);
    [DllImport("user32.dll")] internal static extern int GetWindowRgn(nint hwnd,nint region);
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] static extern int GetRgnBox(nint rgn,out Native.Rect rect);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    internal static string Class(nint h) {var name=new StringBuilder(256);GetClassNameW(h,name,256);return name.ToString();}
    internal static Rect Bounds(nint h) => Native.GetWindowRect(h,out var r)?new(r.Left,r.Top,Math.Max(0,r.Width),Math.Max(0,r.Height)):Rect.Empty;
    internal static bool Overlaps(Rect a,Rect b) {var area=Rect.Intersect(a,b);return !area.IsEmpty&&area.Width>0&&area.Height>0;}

    static Native.Monitor? SelectedMonitor(string monitorId) =>
        Native.Monitors().FirstOrDefault(m => monitorId.Length == 0 ? m.Primary : m.Device == monitorId);

    // This path runs while expensive UI Automation discovery is suspended.
    internal static nint FindHandle(string monitorId) => FindHandle(SelectedMonitor(monitorId));

    static nint FindHandle(Native.Monitor? monitor)
    {
        if (monitor is null) return 0;
        var area = new Rect(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height);
        bool Matches(nint handle) => handle != 0 && Overlaps(area, Bounds(handle));
        nint primary = FindWindowW("Shell_TrayWnd", null);
        if (Matches(primary)) return primary;
        nint secondary = 0;
        while ((secondary = FindWindowExW(0, secondary, "Shell_SecondaryTrayWnd", null)) != 0)
            if (Matches(secondary)) return secondary;
        return 0;
    }

    internal static TaskbarSnapshot Read(string monitorId,nint own)
    {
        var monitor=SelectedMonitor(monitorId)
            ??throw new InvalidOperationException("선택한 모니터가 연결되어 있지 않습니다. 표시할 모니터를 다시 선택하세요.");
        nint selected=FindHandle(monitor);
        if(selected==0)throw new InvalidOperationException("선택한 모니터에 Windows 작업 표시줄이 없습니다. 작업 표시줄이 있는 모니터를 선택하세요.");
        var bounds=Bounds(selected);uint dpi=Native.GetDpiForWindow(selected);
        if(bounds.IsEmpty||bounds.Width<2||bounds.Height<2||dpi==0||!Native.IsWindowVisible(selected))
            throw new InvalidOperationException("작업 표시줄을 사용할 수 없습니다. 다시 연결을 누르세요.");
        Native.GetWindowThreadProcessId(selected,out uint shellPid);
        var occupied=new List<Rect>();bool buttonsKnown=false,foreign=false;int nodes=0;
        // Only query Explorer's own XAML bridge, never our reparented WPF provider.
        // Read runs on a dedicated background operation; the caller bounds its lifetime.
        void ReadElement(AutomationElement element,int depth)
        {
            if(++nodes>400||depth>12)throw new InvalidOperationException("작업 표시줄 구성을 확인하지 못했습니다. 위젯을 숨겼습니다.");
            var p=element.Current;if(p.ProcessId!=(int)shellPid||p.IsOffscreen)return;
            var rect=p.BoundingRectangle;string id=p.AutomationId;
            bool buttonGroup=id.Contains("FrameRepeater",StringComparison.OrdinalIgnoreCase);
            bool tray=id.Contains("SystemTray",StringComparison.OrdinalIgnoreCase);
            if(buttonGroup||tray||p.ControlType==ControlType.Button||p.ControlType==ControlType.CheckBox||p.ControlType==ControlType.MenuItem)
            {
                if(!rect.IsEmpty&&rect.Width>0&&rect.Height>0)occupied.Add(rect);
                if(buttonGroup||p.ControlType==ControlType.Button)buttonsKnown=true;
                if(buttonGroup||tray)return;
            }
            var walker=TreeWalker.RawViewWalker;
            for(var child=walker.GetFirstChild(element);child is not null;child=walker.GetNextSibling(child))ReadElement(child,depth+1);
        }
        for(nint child=GetWindow(selected,5);child!=0;child=GetWindow(child,2))
        {
            if(child==own||!Native.IsWindowVisible(child))continue;
            Native.GetWindowThreadProcessId(child,out uint pid);string cls=Class(child);
            if(pid!=shellPid)
            {
                var rect=Bounds(child);nint rgn=CreateRectRgn(0,0,0,0);
                try {if(rgn!=0&&GetWindowRgn(child,rgn)>0&&GetRgnBox(rgn,out var local)>0)rect=new(rect.Left+local.Left,rect.Top+local.Top,local.Width,local.Height);}
                finally {if(rgn!=0)DeleteObject(rgn);}
                if(!rect.IsEmpty&&rect.IntersectsWith(bounds)){occupied.Add(rect);foreign=true;}continue;
            }
            if(cls.Contains("DesktopWindowContentBridge",StringComparison.Ordinal)||cls=="Windows.UI.Input.InputSite.WindowClass")ReadElement(AutomationElement.FromHandle(child),0);
            else if(cls is "TrayNotifyWnd" or "MSTaskSwWClass" or "ReBarWindow32")
            {occupied.Add(Bounds(child));if(cls!="TrayNotifyWnd")buttonsKnown=true;}
        }
        if(!buttonsKnown)throw new InvalidOperationException("작업 표시줄 버튼 위치를 확인하지 못해 위젯을 숨겼습니다. 다시 연결하거나 다른 모니터를 선택하세요.");
        return new(selected,monitor.Device,dpi,bounds,occupied.ToArray(),foreign);
    }

    internal static Rect? Place(TaskbarSnapshot shell,double widthDip,TaskbarPosition position)
    {
        bool vertical=shell.Vertical;double scale=shell.Dpi/96d;
        double length=vertical?shell.Bounds.Height:shell.Bounds.Width;
        double origin=vertical?shell.Bounds.Top:shell.Bounds.Left;
        double cross=vertical?shell.Bounds.Width:shell.Bounds.Height;
        double wanted=widthDip*scale,padding=8*scale;
        var intervals=shell.Occupied.Where(r=>!r.IsEmpty&&r.IntersectsWith(shell.Bounds)).Select(r=>(Start:Math.Max(0,(vertical?r.Top:r.Left)-origin-padding),End:Math.Min(length,(vertical?r.Bottom:r.Right)-origin+padding))).OrderBy(r=>r.Start).ToArray();
        var spaces=new List<(double Start,double End)>();double next=padding;
        foreach(var block in intervals){if(block.Start>next)spaces.Add((next,block.Start));next=Math.Max(next,block.End);}
        if(next<length-padding)spaces.Add((next,length-padding));
        var candidates=spaces.Where(s=>s.End-s.Start>=wanted);
        if(position==TaskbarPosition.End)candidates=candidates.Reverse();
        foreach(var space in candidates)
        {
            double start=position==TaskbarPosition.End?space.End-wanted:space.Start;
            double height=Math.Min(cross-4*scale,40*scale);if(height<28*scale)return null;
            return vertical?new Rect(shell.Bounds.Left+(cross-height)/2,origin+start,height,wanted):new Rect(origin+start,shell.Bounds.Top+(cross-height)/2,wanted,height);
        }
        return null;
    }
}
