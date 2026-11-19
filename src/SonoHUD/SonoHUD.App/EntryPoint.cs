using SonoHUD.App.Deployment;
using Velopack;

namespace SonoHUD.App;

public static class EntryPoint
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetLocator(new CoreWindowsLocator()).SetArgs(args).SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => new ReleasePolicy().RemoveLogin()).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
