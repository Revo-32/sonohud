using System.IO;
using Velopack;
using Velopack.Locators;

namespace SonoHUD.App.Deployment;

internal sealed class CoreWindowsLocator() : WindowsVelopackLocator(null, null)
{
    public override List<VelopackAsset> GetLocalPackages()
    {
        var owned = new List<VelopackAsset>();
        if (CurrentlyInstalledVersion is null || PackagesDir is null || string.IsNullOrEmpty(AppId)) return owned;
        try
        {
            foreach (string path in Directory.EnumerateFiles(PackagesDir, AppId + "-*-full.nupkg"))
            {
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    var asset = VelopackAsset.FromNupkgNoChecksum(path);
                    if (asset.PackageId != AppId || asset.Type != VelopackAssetType.Full || asset.Version.IsPrerelease) continue;
                    string name = Path.GetFileName(path);
                    if (name != $"{AppId}-{asset.Version}-full.nupkg" && name != $"{AppId}-{asset.Version}-stable-full.nupkg") continue;
                    if (name != asset.FileName) continue;
                    using var package = File.OpenRead(path);
                    PackageIdentity.Verify(package, asset);
                    owned.Add(asset);
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
                {
                    // An unreadable, foreign or malformed archive is never owned by this installation.
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return owned;
    }
}
