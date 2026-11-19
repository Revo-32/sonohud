using System.IO;
using System.Windows;
using System.Windows.Controls;
using SonoHUD.App.Deployment;

namespace SonoHUD.App.Views;

public partial class DeploymentSettingsView : UserControl
{
    DeploymentController? controller;
    bool refreshing;
    bool lastKnownLogin;
    public DeploymentSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => { controller = (Application.Current as App)?.Deployment; if (controller is not null) controller.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { if (controller is not null) controller.Changed -= Refresh; };
    }
    void Refresh()
    {
        refreshing = true;
        try
        {
            Login.IsEnabled = controller?.Installed == true;
            Summary.Text = controller is null ? "Portable" : $"버전 {controller.Version} · {controller.Status}";
            try { lastKnownLogin = controller?.LoginEnabled == true; }
            catch (Exception e) when (IsSettingsError(e))
            {
                Login.IsEnabled = false;
                Summary.Text = "시작 설정을 읽을 수 없습니다. 현재 계정의 접근 권한을 확인하세요.";
            }
            Login.IsChecked = lastKnownLogin;
            Automatic.IsEnabled = controller?.HasChannel == true;
            Automatic.IsChecked = controller?.AutoDownload == true;
            Check.Visibility = controller?.HasChannel == true ? Visibility.Visible : Visibility.Collapsed;
            Check.IsEnabled = controller?.Busy == false;
            Download.Visibility = controller?.CanDownload == true ? Visibility.Visible : Visibility.Collapsed;
            Apply.Visibility = controller?.CanApply == true ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { refreshing = false; }
    }
    static bool IsSettingsError(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException;
    void Change(CheckBox setting, Action action)
    {
        if (refreshing || controller is null || !setting.IsEnabled) return;
        try { action(); Refresh(); }
        catch (Exception e) when (IsSettingsError(e))
        { Refresh(); Summary.Text = "설정을 저장할 수 없습니다. 현재 계정의 접근 권한을 확인하세요."; }
    }
    void LoginChanged(object sender, RoutedEventArgs e) => Change(Login, () => controller?.SetLogin(Login.IsChecked == true));
    void AutoChanged(object sender, RoutedEventArgs e) => Change(Automatic, () => controller?.SetAutoDownload(Automatic.IsChecked == true));
    async void CheckClick(object sender, RoutedEventArgs e) { if (controller is not null) await controller.CheckAsync(); }
    async void DownloadClick(object sender, RoutedEventArgs e) { if (controller is not null) await controller.DownloadAsync(); }
    async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (controller is not null && Window.GetWindow(this) is { } window && Application.Current is App app)
            await controller.RequestApplyAsync(window, app.RestartArgs);
    }
}
