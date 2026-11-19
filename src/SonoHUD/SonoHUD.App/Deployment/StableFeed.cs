using System.IO;

using System.Security.Cryptography;
using System.Text;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace SonoHUD.App.Deployment;

internal sealed class StableFeed : IUpdateSource
{
    const long MaxPackage = 512L * 1024 * 1024;
    readonly string packageId;
    readonly byte[][] publicKeys;
    readonly Func<string, CancellationToken, Task<Stream>> open;

    readonly Func<string, Stream> createDestination;

    internal StableFeed(string packageId, byte[] publicKey, Func<string, CancellationToken, Task<Stream>> open)
        : this(packageId, [publicKey], open) { }

    internal StableFeed(string packageId, IReadOnlyList<byte[]> keys, Func<string, CancellationToken, Task<Stream>> open, Func<string, Stream>? createDestination = null)
    {
        if (keys.Count is < 1 or > 2) throw new ArgumentException("배포 서명 키는 한 개 또는 두 개를 지정하세요.");
        publicKeys = keys.Select(key =>
        {
            using var signer = ECDsa.Create();
            signer.ImportSubjectPublicKeyInfo(key, out int read);
            if (read != key.Length || signer.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new ArgumentException("P-256 배포 서명 키를 확인하세요.");
            return signer.ExportSubjectPublicKeyInfo();
        }).ToArray();
        this.packageId = packageId;
        this.open = open;
        this.createDestination = createDestination ?? (path => File.Create(path));
    }

    internal static StableFeed FromHttps(string id, Uri origin, params byte[][] keys) => new(id, keys, ReleaseTransport.FixedOrigin(origin));

    internal static StableFeed FromGitHubReleases(string id, string owner, string repo, params byte[][] keys) =>
        new(id, keys, ReleaseTransport.GitHub(owner, repo));

    bool Signed(byte[] bytes, byte[] signature)
    {
        if (signature.Length != 64) return false;
        foreach (var key in publicKeys)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out _);
            if (ecdsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return true;
        }
        return false;
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        if (channel != "stable" || appId != packageId) throw new InvalidDataException("채널 또는 패키지 식별자가 다릅니다.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        byte[] bytes = await ReadSmall("releases.stable.json", 1024 * 1024, timeout.Token);
        byte[] signature = await ReadSmall("releases.stable.json.sig", 512, timeout.Token);
        if (!Signed(bytes, signature)) throw new InvalidDataException("업데이트 목록 서명을 확인할 수 없습니다.");
        var feed = VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(bytes));
        if (feed.Assets is null || feed.Assets.Length > 2048) throw new InvalidDataException("업데이트 목록 형식 오류");
        feed.Assets = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToArray();
        if (feed.Assets.Any(a => !Valid(a))) throw new InvalidDataException("안정 채널의 업데이트 파일 정보가 올바르지 않습니다.");
        return feed;
    }

    bool Valid(VelopackAsset a) => a.PackageId == packageId && a.Version is not null && !a.Version.IsPrerelease &&
        a.Size is > 0 and <= MaxPackage && a.SHA256 is { Length: 64 } && a.SHA256.All(Uri.IsHexDigit) &&
        a.FileName is { Length: > 0 and < 180 } && a.FileName == $"{packageId}-{a.Version}-stable-full.nupkg" &&
        a.FileName.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    async Task<byte[]> ReadSmall(string name, int maximum, CancellationToken token)
    {
        await using var stream = await open(name, token);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(block, token)) != 0)
        {
            if (buffer.Length + read > maximum) throw new InvalidDataException("업데이트 목록이 너무 큽니다.");
            buffer.Write(block, 0, read);
        }
        return buffer.ToArray();
    }

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
    {
        if (!Valid(releaseEntry)) throw new InvalidDataException("업데이트 파일 정보 오류");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await using var source = await open(releaseEntry.FileName, timeout.Token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var destination = createDestination(localFile);
        var block = new byte[81920];
        int read;
        long count = 0;
        while ((read = await source.ReadAsync(block, timeout.Token)) != 0)
        {
            count += read;
            if (count > releaseEntry.Size) throw new InvalidDataException("업데이트 크기가 일치하지 않습니다.");
            hash.AppendData(block, 0, read);
            await destination.WriteAsync(block.AsMemory(0, read), timeout.Token);
            progress((int)(100 * count / releaseEntry.Size));
        }
        if (count != releaseEntry.Size) throw new EndOfStreamException("업데이트 다운로드가 완료되지 않았습니다.");
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), releaseEntry.SHA256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("업데이트 파일의 무결성을 확인할 수 없습니다.");
        PackageIdentity.Verify(destination, releaseEntry);
    }
}

