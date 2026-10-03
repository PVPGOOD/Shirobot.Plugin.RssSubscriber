using System.Net;
using System.Net.Http.Headers;
using Shirobot.Plugin.RssSubscriber.Config;
using Shirobot.Plugin.RssSubscriber.Feeds;
using Xunit;

namespace Shirobot.Plugin.RssSubscriber.Tests;

public sealed class RssFetcherTests
{
    [Fact]
    public async Task ConditionalRequestReturnsNotModifiedWithoutParsing()
    {
        var requestedEtag = string.Empty;
        DateTimeOffset? requestedDate = null;
        using var http = new HttpClient(new Handler(request =>
        {
            requestedEtag = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString() ?? string.Empty;
            requestedDate = request.Headers.IfModifiedSince;
            return new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                Headers = { ETag = new EntityTagHeaderValue("\"version-2\"") }
            };
        })) { Timeout = Timeout.InfiniteTimeSpan };
        var config = new RssPluginConfig { AllowPrivateUrls = true };
        var fetcher = new FeedFetcher(http, () => config);
        var modified = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await fetcher.FetchAsync(
            "https://example.test/feed", "\"version-1\"", modified, CancellationToken.None);

        Assert.True(result.NotModified);
        Assert.Empty(result.Items);
        Assert.Equal("\"version-1\"", requestedEtag);
        Assert.Equal(modified, requestedDate);
        Assert.Equal("\"version-2\"", result.ETag);
    }

    [Fact]
    public async Task RedirectTargetIsCheckedBeforeRequest()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Interlocked.Increment(ref requests);
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://127.0.0.1/private") }
            };
        })) { Timeout = Timeout.InfiniteTimeSpan };
        var config = new RssPluginConfig { AllowPrivateUrls = false };
        var fetcher = new FeedFetcher(http, () => config);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fetcher.FetchAsync("https://8.8.8.8/feed", CancellationToken.None));

        Assert.Contains("内网/回环", error.Message);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task RelativeRedirectUsesFinalUrlForItemLinks()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/feed")
                return new HttpResponseMessage(HttpStatusCode.MovedPermanently)
                {
                    Headers = { Location = new Uri("/nested/feed", UriKind.Relative) }
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<rss><channel><item><guid>one</guid><title>One</title><link>article</link></item></channel></rss>")
            };
        })) { Timeout = Timeout.InfiniteTimeSpan };
        var config = new RssPluginConfig { AllowPrivateUrls = true };
        var fetcher = new FeedFetcher(http, () => config);

        var result = await fetcher.FetchAsync("https://example.test/feed", CancellationToken.None);

        Assert.Equal("https://example.test/nested/article", Assert.Single(result.Items).Link);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
