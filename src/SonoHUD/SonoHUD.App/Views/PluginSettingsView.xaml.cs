using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SonoHUD.App.Plugins;
using SonoHUD.App.ViewModels;

namespace SonoHUD.App.Views;

public partial class PluginSettingsView : UserControl
{
    PluginHost? host;
    public PluginSettingsView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>Connect();
        Unloaded+=(_,_)=>Disconnect();
        DataContextChanged+=(_,_)=>{if(IsLoaded)Connect();};
    }
    void Disconnect(){if(host is not null)host.Changed-=Refresh;host=null;}
    void Connect(){Disconnect();host=(DataContext as StudioViewModel)?.Plugins;if(host is not null)host.Changed+=Refresh;Refresh();}
    void Refresh()
    {
        string? previous=(Extensions.SelectedItem as PluginRow)?.SourcePath;
        Extensions.ItemsSource=host?.Rows;
        Extensions.SelectedItem=host?.Rows.Find(x=>x.SourcePath==previous)??host?.Rows.FirstOrDefault();
        bool empty=host?.Rows.Count is 0 or null;
        Empty.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        Extensions.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
        SelectionChanged(null,null);
    }
    void SelectionChanged(object? sender,SelectionChangedEventArgs? args)
    {
        if(Details is null)return;
        var r=Extensions.SelectedItem as PluginRow;
        Details.Text=r?.Detail??"";
        bool active=r?.State=="활성화";
        EnableButton.IsEnabled=r?.Manifest?.Compatible==true && !active && r.State!="중복 ID";
        DisableButton.IsEnabled=active;
        RemoveButton.IsEnabled=r is not null && r.SourcePath!=host?.DirectoryPath;
    }
    bool Confirm(string prompt)=>MessageBox.Show(Window.GetWindow(this),prompt,"SonoHUD · 확장 기능",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)==MessageBoxResult.OK;
    void Run(Action<PluginHost> action)
    {
        if(host is null)return;
        Feedback.Text="";
        try{action(host);}
        catch(Exception e) when(PluginHost.Recoverable(e)){Feedback.Text="요청을 완료하지 못했습니다. "+e.Message;}
    }
    void Install(object sender,RoutedEventArgs args)=>Run(h=>{
        var picker=new OpenFileDialog{Filter="SonoHUD 확장 (*.dll)|*.dll",CheckFileExists=true,Multiselect=false};
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        var p=h.Inspect(picker.FileName);
        if(!p.Manifest.Compatible){Feedback.Text="이 확장은 현재 Core API와 호환되지 않습니다.";return;}
        if(Confirm($"{p.Manifest.Name} {p.Manifest.Version} 확장을 설치할까요?\n설치 후 활성화를 선택하면 실행됩니다."))
            InstallConfirmed(h, picker.FileName, p);
    });
    void InstallConfirmed(PluginHost pluginHost, string path, PluginPackage package)
    {
        pluginHost.Install(path, package.Hash);
        var installed = pluginHost.Rows.FirstOrDefault(row =>
            row.Manifest?.Id == package.Manifest.Id && row.Hash == package.Hash);
        Extensions.SelectedItem = installed;
        if (installed is not null) Extensions.ScrollIntoView(installed);
        Feedback.Text = installed is null
            ? "설치한 확장을 목록에서 찾을 수 없습니다. 새로 고침하세요."
            : $"{installed.DisplayName} 설치 완료. 사용하려면 활성화를 선택하세요.";
    }
    void Enable(object sender,RoutedEventArgs args)=>Run(h=>{
        if(Extensions.SelectedItem is not PluginRow {Manifest:{} m} row)return;
        string permissions=m.Permissions.Length==0?"선언 없음":string.Join(", ",m.Permissions);
        string migration=m.ImportLegacyLyricsPreferences?"\n기존 가사 선택과 시간 보정을 이 확장 설정으로 가져옵니다.":"";
        if(Confirm($"{m.Name} {m.Version} ({m.Id})\n선언한 접근: {permissions}\n{m.Disclosure}{migration}\n\n이 DLL은 SonoHUD와 같은 Windows 권한으로 실행됩니다. 출처를 신뢰할 때만 활성화하세요.\n\nSHA-256: {row.Hash}"))h.Enable(row);
    });
    void Disable(object sender,RoutedEventArgs args)=>Run(h=>{if(Extensions.SelectedItem is PluginRow {Manifest:{} m})h.Disable(m.Id);});
    void Remove(object sender,RoutedEventArgs args)=>Run(h=>{if(Extensions.SelectedItem is PluginRow row && Confirm($"{row.DisplayName}을 제거할까요? 기존 확장 설정은 보관합니다."))h.Remove(row);});
    void Rescan(object sender,RoutedEventArgs args)=>Run(h=>h.Refresh());
    void Folder(object sender,RoutedEventArgs args)=>Run(h=>h.OpenFolder());
}
