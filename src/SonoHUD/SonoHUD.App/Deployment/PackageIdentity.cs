using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Velopack;

namespace SonoHUD.App.Deployment;

internal static class PackageIdentity
{
    internal static void Verify(Stream stream, VelopackAsset asset)
    {
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var manifests = zip.Entries.Where(entry => entry.FullName == entry.Name && entry.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (manifests.Length != 1 || manifests[0].Length is <= 0 or > 262144)
            throw new InvalidDataException("업데이트 명세가 유효하지 않습니다.");
        using var body = manifests[0].Open();
        using var reader = XmlReader.Create(body, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        var document = XDocument.Load(reader);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = document.Root?.Elements(ns + "metadata").SingleOrDefault();
        string? Value(string name) => metadata?.Elements(ns + name).SingleOrDefault()?.Value;
        if (document.Root?.Name.LocalName != "package" || Value("id") != asset.PackageId || Value("version") != asset.Version.ToString() ||
            Value("channel") != "stable" || Value("rid") != "win-x64" || Value("mainExe") != "SonoHUD.exe")
            throw new InvalidDataException("업데이트 대상 제품·버전·채널이 일치하지 않습니다.");
    }
}
