using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shirobot.Plugin.RssSubscriber.Feeds;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Bilibili;

/// <summary>Enriches Bilibili video cards from the public video detail endpoint.</summary>
public sealed partial class BilibiliVideoDataProvider(HttpClient http) : ICardDataProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (CardDataSupplement Data, DateTimeOffset ExpiresAt)> _cache = new();

    public string SourceId => "bilibili";
    public string VariantId => "video";

    public async Task<CardDataSupplement?> TryLoadAsync(FeedItem item, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(item.Link, UriKind.Absolute, out var itemUri)) return null;
        var segments = itemUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var videoIndex = Array.FindIndex(segments, segment => segment.Equals("video", StringComparison.OrdinalIgnoreCase));
        if (videoIndex < 0 || videoIndex + 1 >= segments.Length) return null;
        var bvid = segments[videoIndex + 1];
        if (!BvidPattern().IsMatch(bvid)) return null;

        if (_cache.TryGetValue(bvid, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Data;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var url = $"https://api.bilibili.com/x/web-interface/view?bvid={Uri.EscapeDataString(bvid)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
            request.Headers.Referrer = new Uri("https://www.bilibili.com/");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0 ||
                !root.TryGetProperty("data", out var data))
                return null;

            var stat = data.TryGetProperty("stat", out var statElement) ? statElement : default;
            var owner = data.TryGetProperty("owner", out var ownerElement) ? ownerElement : default;
            var seconds = ReadInt(data, "duration");
            var supplement = new CardDataSupplement(
                AuthorName: ReadString(owner, "name"),
                AvatarUrl: ReadString(owner, "face"),
                CoverUrl: ReadString(data, "pic"),
                DurationText: seconds > 0 ? FormatDuration(seconds) : null,
                ViewCountText: FormatCount(ReadLong(stat, "view")),
                DanmakuCountText: FormatCount(ReadLong(stat, "danmaku")),
                LikeCountText: FormatCount(ReadLong(stat, "like")),
                CoinCountText: FormatCount(ReadLong(stat, "coin")),
                FavoriteCountText: FormatCount(ReadLong(stat, "favorite")),
                ShareCountText: FormatCount(ReadLong(stat, "share")),
                CategoryText: ReadString(data, "tname"));

            _cache[bvid] = (supplement, DateTimeOffset.UtcNow.Add(CacheLifetime));
            return supplement;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"[Rss] Bilibili 视频信息加载失败 bvid={bvid}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int ReadInt(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result) ? result : 0;

    private static long ReadLong(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.TryGetInt64(out var result) ? result : 0;

    private static string? FormatCount(long count)
    {
        if (count < 0) return null;
        if (count >= 100_000_000) return (count / 100_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "亿";
        if (count >= 10_000) return (count / 10_000d).ToString("0.#", CultureInfo.InvariantCulture) + "万";
        return count.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(int totalSeconds)
    {
        var duration = TimeSpan.FromSeconds(totalSeconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    [GeneratedRegex("^BV[0-9A-Za-z]{10}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BvidPattern();
}
