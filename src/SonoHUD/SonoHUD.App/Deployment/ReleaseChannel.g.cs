namespace SonoHUD.App.Deployment;

internal sealed partial class ReleasePolicy
{
    partial void ConfigureRelease()
    {
        Feed = StableFeed.FromGitHubReleases(PackageId, "Revo-32", "sonohud", Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEOb30IGhOZhu7MiiCWl1UHXYtHv9qhexxSk+R4oygm+PojKFpTBmMBy82w5oYFoGmnOIfZxWt/hhtGT0LHcUyuQ=="));
    }
}
