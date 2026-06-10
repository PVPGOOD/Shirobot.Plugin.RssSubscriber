using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;

namespace Shirobot.Plugin.RssSubscriber.Feeds;

/// <summary>
/// 把 http(s) 图片 URL 下载并转成 base64:// 段，避免 adapter / QQ 服务端拉不到本机/内网地址。
/// </summary>
public sealed class ImageEmbedder
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private const int TimeoutSeconds = 15;

    private readonly HttpClient _httpClient;

    public ImageEmbedder(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ImageOutgoingSegment?> TryBuildAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is "base64" or "file")
        {
            return new ImageOutgoingSegment(url);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                BotLog.Warning($"[Rss] 下载图片失败 {url}: HTTP {(int)response.StatusCode}");
                return new ImageOutgoingSegment(url);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cts.Token)) > 0)
            {
                if (memory.Length + read > MaxBytes)
                {
                    BotLog.Warning($"[Rss] 图片超过 {MaxBytes / 1024 / 1024}MB 阈值，回退到原始 URL: {url}");
                    return new ImageOutgoingSegment(url);
                }

                memory.Write(buffer, 0, read);
            }

            if (memory.Length == 0)
            {
                return null;
            }

            var base64 = Convert.ToBase64String(memory.GetBuffer(), 0, (int)memory.Length);
            return new ImageOutgoingSegment("base64://" + base64);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] 下载图片异常，回退到原始 URL {url}: {ex.GetType().Name}: {ex.Message}");
            return new ImageOutgoingSegment(url);
        }
    }
}
