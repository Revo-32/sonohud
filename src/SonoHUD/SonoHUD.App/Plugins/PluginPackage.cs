using System.Buffers.Binary;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SonoHUD.App.Plugins;

internal sealed record PluginManifest(string Id, string Name, string Version, int MinApi, int MaxApi,
    string Entry, string[] Capabilities, string[] Permissions, string? ProviderName, string? Disclosure, bool ImportLegacyLyricsPreferences = false)
{
    internal bool Compatible => MinApi <= 1 && MaxApi >= 1 && Capabilities.All(x => x == "lyrics");
    internal bool Lyrics => Capabilities.Contains("lyrics", StringComparer.Ordinal);
}

internal sealed record PluginPackage(PluginManifest Manifest, string EntryFile, string Hash, IReadOnlyDictionary<string, byte[]> Files)
{
    const int MaxBytes = 64 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, MaxDepth = 8 };

    internal static PluginPackage Read(string path)
    {
        var attributes = File.GetAttributes(path);
        CheckLink(attributes);
        string[] paths;
        if ((attributes & FileAttributes.Directory) != 0)
        {
            paths = Directory.EnumerateFileSystemEntries(path).Take(33).ToArray();
            if (paths.Length > 32) throw new InvalidDataException("확장 파일은 최대 32개입니다.");
        }
        else paths = [path];
        var parts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        PluginManifest? manifest = null;
        string? entry = null;
        int total = 0;
        foreach (string file in paths.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var info = new FileInfo(file);
            CheckLink(info.Attributes);
            string name = info.Name;
            bool dll = name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase);
            if ((info.Attributes & FileAttributes.Directory) != 0 || !dll && !name.EndsWith(".deps.json",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("확장 폴더에는 DLL과 deps.json만 넣어 주세요.");
            if (info.Length > MaxBytes - total || info.Length == 0) throw new InvalidDataException("확장 패키지 크기 제한을 초과했습니다.");
            using var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
            int length = checked((int)stream.Length);
            if (length > MaxBytes - total) throw new InvalidDataException("확장 파일 크기가 변경됐습니다.");
            byte[] bytes = new byte[length];
            stream.ReadExactly(bytes);
            total += length;
            parts.Add(name,bytes);
            Append(hash, Encoding.UTF8.GetBytes(name));
            Append(hash, bytes);
            if (!dll) continue;
            var item = ReadManifest(bytes);
            if (item is null) continue;
            if (manifest is not null) throw new InvalidDataException("확장 진입 DLL이 둘 이상입니다.");
            manifest = item;
            entry = name;
        }
        if (manifest is null || entry is null) throw new InvalidDataException("지원되는 확장 선언이 없습니다.");
        return new(manifest,entry,Convert.ToHexString(hash.GetHashAndReset()),parts);
    }

    static void Append(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    internal static void CheckLink(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("확장 경로에는 링크를 사용할 수 없습니다.");
    }

    static PluginManifest? ReadManifest(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes,false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("이 SDK는 관리형 DLL 확장을 지원합니다.");
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) return null;
        PluginManifest? found = null;
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(handle);
            if (attr.Constructor.Kind != HandleKind.MemberReference) continue;
            var ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
            if (!reader.StringComparer.Equals(type.Namespace,"System.Reflection") || !reader.StringComparer.Equals(type.Name,"AssemblyMetadataAttribute")) continue;
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.ReadUInt16()!=1 || blob.ReadSerializedString()!="SonoHUD.Plugin") continue;
            var value = blob.ReadSerializedString();
            if (found is not null || value is null || value.Length>4096) throw new InvalidDataException("확장 선언이 잘못되었습니다.");
            var m = JsonSerializer.Deserialize<PluginManifest>(value,Json) ?? throw new InvalidDataException("확장 선언이 없습니다.");
            if (m.Id is null || !Regex.IsMatch(m.Id,@"\A[a-z][a-z0-9_-]*(?:\.[a-z0-9_-]+)*\z",RegexOptions.CultureInvariant)
                || m.Id.Length>80 || m.Id.Split('.')[0].ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" || Regex.IsMatch(m.Id,@"^(com|lpt)[0-9](\.|$)",RegexOptions.IgnoreCase)
                || !Clean(m.Name,100) || !System.Version.TryParse(m.Version,out _) || !Clean(m.Entry,255)
                || m.MinApi<1 || m.MaxApi<m.MinApi || m.Capabilities is not { Length: <=16 } || m.Permissions is not { Length: <=16 }
                || !m.Capabilities.All(x=>Clean(x,64)) || !m.Permissions.All(x=>Clean(x,120))
                || (m.ProviderName is not null && !Clean(m.ProviderName,100)) || (m.Disclosure is not null && !Clean(m.Disclosure,500)))
                throw new InvalidDataException("확장 이름·ID·버전·권한 선언을 확인하세요.");
            found=m;
        }
        return found;
    }
    static bool Clean(string? value,int limit)=>!string.IsNullOrWhiteSpace(value) && value.Length<=limit && !value.Any(char.IsControl);
}
