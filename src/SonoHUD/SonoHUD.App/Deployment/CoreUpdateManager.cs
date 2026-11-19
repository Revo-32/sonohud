using System.IO;
using Velopack;
using Velopack.Sources;

namespace SonoHUD.App.Deployment;

internal sealed class CoreUpdateManager(IUpdateSource source) : UpdateManager(source,
    new UpdateOptions { ExplicitChannel = "stable", AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = 0 })
{
    protected override void CleanPackagesExcept(string? assetToKeep)
    {
        // The installed full package is the uninstaller's ownership inventory.
        // VelopackApp.Run prunes obsolete releases on startup while preserving installed and pending versions.
        if (assetToKeep is null) return;
        try { File.Delete(assetToKeep + ".partial"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
