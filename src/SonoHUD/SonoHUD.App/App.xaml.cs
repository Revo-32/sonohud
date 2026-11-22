using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using SonoHUD.App.Deployment;
using SonoHUD.App.Media;
using SonoHUD.App.Platform;
using SonoHUD.App.ViewModels;
using SonoHUD.Core;

namespace SonoHUD.App;

public partial class App : Application
{
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    Mutex? instance;
    MemoryMappedFile? owner;
    AppLog? log;
    EventWaitHandle? openMain;
    RegisteredWaitHandle? openListener;
    LoginTray? tray;
    bool sessionEnding;
    internal bool TrayRegistered => tray?.Registered == true;
    internal DeploymentController? Deployment { get; private set; }
    internal string[] RestartArgs { get; private set; } = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool login = e.Args.Contains("--login");
        try
        {
            string? Arg(string name) { int at = Array.IndexOf(e.Args, name); return at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : null; }
            var policy = new ReleasePolicy();
            string? dataArg = Arg("--data-dir");
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataArg ?? policy.DefaultDataDirectory));
            if (dataArg is not null) RestartArgs = ["--data-dir", directory];
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..24];
            // Create the activation event first so a second launch can signal a starting process.
            openMain = new(false, EventResetMode.AutoReset, "Local\\SonoHUD-Open-" + key);
            owner = MemoryMappedFile.CreateOrOpen("Local\\SonoHUD-Owner-" + key, sizeof(int));
            using var ownerId = owner.CreateViewAccessor(0, sizeof(int));
            instance = new(true, "Local\\SonoHUD-" + key, out bool created);
            if (!created)
            {
                if (!login)
                {
                    // Windows permits a user-launched process to delegate foreground activation.
                    // The primary may still be writing its PID during simultaneous startup.
                    SpinWait.SpinUntil(() => ownerId.ReadInt32(0) > 0, 250);
                    int pid = ownerId.ReadInt32(0);
                    if (pid > 0) AllowSetForegroundWindow(pid);
                    openMain.Set();
                }
                Shutdown(); return;
            }
            ownerId.Write(0, Environment.ProcessId);
            string? migration = null;
            if (dataArg is null && policy.PackageId == "SonoHUD.Core")
                try { migration = DataMigration.ImportLegacy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevoMusic"), directory); }
                catch (Exception ex) { migration = "이전 설정 가져오기를 완료하지 못했습니다. RevoMusic 원본은 보존되어 있습니다: " + ex.Message; }
            Directory.CreateDirectory(directory);
            log = new(Path.Combine(directory, "logs"));
            DispatcherUnhandledException += (_, a) => log.Error("UI 처리되지 않은 오류", a.Exception);
            Deployment = new(policy, directory);
            var repository = new OverlayRepository(directory);
            var loaded = repository.Load();
            var media = new MediaService(Dispatcher, log);
            var vm = new StudioViewModel(loaded.Document, repository, media, log, loaded.CanSave);
            if (e.Args.Contains("--demo")) media.SetDemo(true);
            if (e.Args.Contains("--automation") && Arg("--page") is "Home" or "Overlay" or "Audio" or "Settings")
                vm.NavigateCommand.Execute(Arg("--page"));
            var window = new StudioWindow(vm, e.Args.Contains("--automation"), e.Args.Contains("--secondary"));
            MainWindow = window;
            if (loaded.Warning is not null) vm.Status = loaded.Warning;
            else if (migration is not null) vm.Status = migration;
            openListener = ThreadPool.RegisterWaitForSingleObject(openMain, (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(window.OpenFromTray);
            }, null, Timeout.Infinite, false);
            vm.StartOnlineLyrics();
            if (login) { window.ShowActivated = false; window.ShowInTaskbar = false; }
            tray = new(window);
            if (!login) window.Show();
            Deployment.Begin();
            await media.StartAsync();
        }
        catch (Exception ex)
        {
            log?.Error("시작 실패", ex);
            if (!login) MessageBox.Show("SonoHUD를 시작하지 못했습니다.\n" + ex.Message, "SonoHUD");
            Shutdown(1);
        }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (e.Cancel) return;
        sessionEnding = true;
        if (MainWindow is StudioWindow studio) studio.FinishWindowsSession();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        openListener?.Unregister(null);
        openMain?.Dispose();
        tray?.Dispose();
        try { if (!sessionEnding) Deployment?.ApplyAfterCleanup(); }
        catch (Exception ex) { log?.Error("업데이트 적용 시작 실패", ex); }
        Deployment?.Dispose();
        log?.Dispose();
        owner?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
