using System.IO;
using System.Security.Cryptography;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Velopack;
using Velopack.Locators;

namespace SonoHUD.App.Deployment;

internal sealed class DeploymentController : IDisposable
{
    readonly ReleasePolicy policy;
    readonly UpdateManager? manager;
    readonly string preferences;
    readonly CancellationTokenSource stopping = new();
    readonly DispatcherTimer? timer;
    UpdateInfo? available;
    VelopackAsset? ready;
    string[] restartArgs = [];
    bool applyRequested;
    internal event Action? Changed;
    internal bool Installed => policy.Installed;
    internal bool HasChannel => manager is not null;
    internal bool LoginEnabled => policy.LoginEnabled;
    internal bool AutoDownload { get; private set; }
    internal bool Busy { get; private set; }
    internal bool CanDownload => available is not null && ready is null && !Busy;
    internal bool CanApply => ready is not null && !Busy;
    internal string Status { get; private set; }
    internal string Version => VelopackLocator.Current.CurrentlyInstalledVersion?.ToString() ?? typeof(App).Assembly.GetName().Version!.ToString(3);

    internal DeploymentController(ReleasePolicy policy, string dataDirectory)
    {
        this.policy = policy;
        preferences = Path.Combine(dataDirectory, "deployment.json");
        try { if (File.Exists(preferences)) AutoDownload = JsonSerializer.Deserialize<bool>(File.ReadAllText(preferences)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (Installed && policy.Feed is not null)
        {
            manager = new CoreUpdateManager(policy.Feed);
            timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            timer.Tick += TimerTick;
        }
        Status = Installed ? HasChannel ? "업데이트를 확인할 수 있습니다." : "공식 업데이트 채널 개설 전입니다." : "Portable · 새 ZIP으로 업데이트할 수 있습니다.";
    }

    internal void Begin()
    {
        timer?.Start();
        if (HasChannel) _ = CheckAsync();
    }
    void TimerTick(object? sender, EventArgs args) => _ = CheckAsync();

    internal void SetLogin(bool enabled) => policy.SetLogin(enabled);

    internal void SetAutoDownload(bool enabled)
    {
        string temp = preferences + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(enabled));
            File.Move(temp, preferences, true);
            AutoDownload = enabled;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal Task CheckAsync() => Operate(async () =>
    {
        available = null;
        ready = null;
        Status = "업데이트 확인 중"; Changed?.Invoke();
        var candidate = await manager!.CheckForUpdatesAsync();
        if (stopping.IsCancellationRequested) return;
        available = candidate;
        if (candidate is null) { Status = "현재 최신 버전입니다."; return; }
        if (candidate.TargetFullRelease.Version <= manager.CurrentVersion) throw new InvalidDataException();
        Status = $"새 버전 {candidate.TargetFullRelease.Version}을 사용할 수 있습니다.";
        if (File.Exists(Path.Combine(VelopackLocator.Current.PackagesDir!, candidate.TargetFullRelease.FileName)))
        {
            try { await VerifyCached(candidate.TargetFullRelease); ready = candidate.TargetFullRelease; Status = $"버전 {ready.Version} 설치 준비 완료"; return; }
            catch (Exception error) when (InvalidCache(error)) { }
        }
        if (AutoDownload) await DownloadCore();
    });

    internal Task DownloadAsync() => Operate(DownloadCore);
    async Task DownloadCore()
    {
        if (available is null) return;
        Status = "업데이트 다운로드 중"; Changed?.Invoke();
        var cachedPath = Path.Combine(VelopackLocator.Current.PackagesDir!, available.TargetFullRelease.FileName);
        if (File.Exists(cachedPath))
        {
            try { await VerifyCached(available.TargetFullRelease); }
            catch (Exception ex) when (InvalidCache(ex)) { File.Delete(cachedPath); }
        }
        await manager!.DownloadUpdatesAsync(available, cancelToken: stopping.Token);
        var asset = available.TargetFullRelease;
        await VerifyCached(asset);
        ready = asset;
        Status = $"버전 {asset.Version} 설치 준비 완료";
    }

    static bool InvalidCache(Exception error) => error is InvalidDataException or EndOfStreamException or System.Xml.XmlException;

    async Task VerifyCached(VelopackAsset asset)
    {
        var file = new FileInfo(Path.Combine(VelopackLocator.Current.PackagesDir!, asset.FileName));
        if (!file.Exists || file.Length != asset.Size) throw new InvalidDataException();
        await using var stream = file.OpenRead();
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, stopping.Token));
        if (!string.Equals(hash, asset.SHA256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("패키지 무결성을 확인하지 못했습니다.");
        PackageIdentity.Verify(stream, asset);
    }

    internal Task RequestApplyAsync(Window window, string[] args) => Operate(async () =>
    {
        if (ready is null) return;
        await VerifyCached(ready);
        restartArgs = args;
        applyRequested = true;
        try
        {
            if (window is not StudioWindow studio || !await studio.ExitApplicationAsync(ExitReason.RestartForUpdate))
                applyRequested = false;
        }
        catch { applyRequested = false; throw; }
    });

    internal void ApplyAfterCleanup()
    {
        if (applyRequested && ready is not null)
            manager?.WaitExitThenApplyUpdates(ready, silent: true, restart: true, restartArgs);
    }

    async Task Operate(Func<Task> operation)
    {
        if (Busy || manager is null || stopping.IsCancellationRequested) return;
        Busy = true;
        try { await operation(); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ready = null;
            Status = ex switch
            {
                HttpRequestException { StatusCode: not null } => "업데이트 서버가 응답하지 않았습니다. 잠시 후 다시 확인해 주세요.",
                HttpRequestException or OperationCanceledException => "업데이트 서버에 연결할 수 없습니다. 연결을 확인한 후 다시 시도하세요.",
                IOException io when (io.HResult & 0xffff) is 39 or 112 => "저장 공간이 부족합니다. 공간을 확보한 후 다시 다운로드하세요.",
                _ => "업데이트를 완료하지 못했습니다. 현재 버전을 유지합니다. 다시 확인해 주세요."
            };
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public void Dispose()
    {
        timer?.Stop();
        stopping.Cancel();
        Changed = null;
    }
}

