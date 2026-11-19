using System.IO;
using System.Net;
using System.Net.Http;

namespace SonoHUD.App.Deployment;

internal static class ReleaseTransport
{
    static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(90) };

    internal static Func<string, CancellationToken, Task<Stream>> FixedOrigin(Uri origin)
    {
        if (!Secure(origin) || origin.Query.Length != 0 || !origin.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("배포 주소가 올바르지 않습니다.");
        return (name, token) => Client.GetStreamAsync(new Uri(origin, name), token);
    }

    internal static Func<string, CancellationToken, Task<Stream>> GitHub(string owner, string repository, HttpClient? client = null)
    {
        static bool Segment(string value) => value.Length is > 0 and <= 100 && value is not "." and not ".." &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
        if (!Segment(owner) || !Segment(repository)) throw new ArgumentException("배포 저장소 이름을 확인하세요.");
        string prefix = $"/{owner}/{repository}/releases/";
        return async (name, token) =>
        {
            if (name.Length is 0 or >= 180 || name is "." or ".." || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                throw new InvalidDataException("배포 파일 이름을 확인하세요.");
            Uri target = new($"https://github.com{prefix}latest/download/{name}");
            for (int i = 0; i <= 4; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                using var response = await (client ?? Client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Preserve streaming and explicitly transfer the network body's ownership to the caller.
                    var content = response.Content;
                    response.Content = null;
                    try { return await content.ReadAsStreamAsync(token).ConfigureAwait(false); }
                    catch { content.Dispose(); throw; }
                }
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                {
                    response.EnsureSuccessStatusCode();
                    throw new InvalidDataException("업데이트 서버 응답 형식을 확인하세요.");
                }
                if (i == 4 || response.Headers.Location is not { } location || !Uri.TryCreate(target, location, out var next) || !Secure(next))
                    throw new InvalidDataException("배포 리디렉션을 확인할 수 없습니다.");
                string path = next.AbsolutePath;
                string tagged = prefix + "download/";
                bool sameRepository = target.Host == "github.com" && next.Host == "github.com" && next.Query.Length == 0 &&
                    path.StartsWith(tagged, StringComparison.Ordinal) && path[tagged.Length..].Split('/') is [var tag, var leaf] &&
                    tag.Length > 0 && leaf == name;
                bool releaseAsset = next.Host == "release-assets.githubusercontent.com" &&
                    path.StartsWith("/github-production-release-asset/", StringComparison.Ordinal);
                if (!sameRepository && !releaseAsset) throw new InvalidDataException("승인된 릴리스 주소로 연결할 수 없습니다.");
                target = next;
            }
            throw new InvalidDataException();
        };
    }

    static bool Secure(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
