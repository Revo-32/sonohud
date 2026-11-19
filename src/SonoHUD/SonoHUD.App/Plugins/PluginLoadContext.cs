using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using SonoHUD.Core;
using SonoHUD.PluginContracts;

namespace SonoHUD.App.Plugins;

// Managed dependencies are loaded from the identical bytes that were shown for approval.
// A directory resolver would reopen mutable files between approval and lazy dependency resolution.
internal sealed class PluginLoadContext(PluginPackage package) : AssemblyLoadContext("SonoHUD:" + package.Manifest.Id,true)
{
    internal Assembly LoadEntry() => LoadFromStream(new MemoryStream(package.Files[package.EntryFile],false));
    protected override Assembly? Load(AssemblyName name)
    {
        foreach (var shared in new[]{typeof(ISonoHudPlugin).Assembly,typeof(LyricTrack).Assembly})
        {
            var identity = shared.GetName();
            if (name.Name != identity.Name) continue;
            if (name.Version?.Major != identity.Version?.Major) throw new FileLoadException("공유 계약의 주 버전이 다릅니다.");
            return shared;
        }
        string file = name.Name + ".dll";
        foreach (var part in package.Files)
            if (part.Key.Equals(file,StringComparison.OrdinalIgnoreCase))
                return LoadFromStream(new MemoryStream(part.Value,false));
        // Only the shared framework may supply an undeclared dependency.
        if (name.Name == "netstandard" || name.Name == "mscorlib" || name.Name == "System" || name.Name?.StartsWith("System.",StringComparison.Ordinal)==true ||
            name.Name is "Microsoft.CSharp" or "WindowsBase" or "PresentationCore" or "PresentationFramework") return null;
        throw new FileNotFoundException("확장 종속성이 없습니다: " + name.Name);
    }
}
