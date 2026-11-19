using System.IO;
using Microsoft.Win32;
using Velopack.Locators;

namespace SonoHUD.App.Deployment;

internal sealed partial class ReleasePolicy
{
    internal string PackageId { get; private set; } = "SonoHUD.Core";
    internal string DefaultDataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonoHUD");
    internal string RunKey { get; private set; } = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal string RunName { get; private set; } = "SonoHUD";
    internal StableFeed? Feed { get; private set; }

    // A release channel and its pinned verification key are compiled in by the distributor.
    // Until a public feed exists the installed app makes no update requests.
    internal ReleasePolicy() => ConfigureRelease();
    partial void ConfigureRelease();

    internal bool Installed => VelopackLocator.Current is { IsPortable: false, CurrentlyInstalledVersion: not null } locator
        && string.Equals(locator.AppId, PackageId, StringComparison.OrdinalIgnoreCase)
        && File.Exists(locator.UpdateExePath);
    string LoginCommand => $"\"{VelopackLocator.Current.UpdateExePath}\" start -- --login";
    internal bool LoginEnabled
    {
        get
        {
            if (!Installed) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(RunName) as string, LoginCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal void SetLogin(bool enabled)
    {
        if (!Installed) throw new InvalidOperationException("설치형 앱에서 설정할 수 있습니다.");
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(RunName, LoginCommand, RegistryValueKind.String);
        }
        else RemoveLogin();
    }

    internal void RemoveLogin()
    {
        if (!Installed) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (string.Equals(key?.GetValue(RunName) as string, LoginCommand, StringComparison.OrdinalIgnoreCase))
            key!.DeleteValue(RunName, false);
    }
}
