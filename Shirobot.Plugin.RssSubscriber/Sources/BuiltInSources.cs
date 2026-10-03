using Shirobot.Plugin.RssSubscriber.Feeds;

namespace Shirobot.Plugin.RssSubscriber.Sources;

public sealed class GeneralSourceProfile : ISourceProfile
{
    public string Id => "general";
    public string DisplayName => "通用 RSS";
    public bool Matches(Uri uri) => true;
    public SourceInput Normalize(Uri input) => SourceInput.Unchanged(input);
}

public sealed class XSourceProfile : ISourceProfile
{
    public string Id => "x";
    public string DisplayName => "X / Twitter";
    public bool Matches(Uri uri) => SourceRegistry.HostIs(uri, "x.com") ||
                                    SourceRegistry.HostIs(uri, "twitter.com") ||
                                    IsRssHubTwitterUserRoute(uri);
    public SourceInput Normalize(Uri input) => SourceInput.Unchanged(input);

    private static bool IsRssHubTwitterUserRoute(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 &&
               segments[0].Equals("twitter", StringComparison.OrdinalIgnoreCase) &&
               segments[1].Equals("user", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class BilibiliSourceProfile : ISourceProfile
{
    public string Id => "bilibili";
    public string DisplayName => "Bilibili";
    public bool Matches(Uri uri) => SourceRegistry.HostIs(uri, "bilibili.com") ||
                                    SourceRegistry.HostIs(uri, "b23.tv");
    public SourceInput Normalize(Uri input) => SourceInput.Unchanged(input);
}

public sealed class GitHubSourceProfile : ISourceProfile
{
    public string Id => "github";
    public string DisplayName => "GitHub";
    public bool Matches(Uri uri) => SourceRegistry.HostIs(uri, "github.com");

    public SourceInput Normalize(Uri input)
    {
        if (!SourceRegistry.HostIs(input, "github.com")) return SourceInput.Unchanged(input);
        var parts = input.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[1].EndsWith(".atom", StringComparison.OrdinalIgnoreCase))
            return SourceInput.Unchanged(input);
        var builder = new UriBuilder(input.Scheme, input.Host)
        {
            Path = $"/{parts[0]}/{parts[1]}/commits.atom",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return new SourceInput(builder.Uri.AbsoluteUri, FeedIdGenerator.Sanitize(parts[0]));
    }
}

public sealed class GiteaSourceProfile : ISourceProfile
{
    public string Id => "gitea";
    public string DisplayName => "Gitea";

    public bool Matches(Uri uri) =>
        SourceRegistry.HostIs(uri, "gitea.com") ||
        uri.IdnHost.StartsWith("git.", StringComparison.OrdinalIgnoreCase);

    public SourceInput Normalize(Uri input) => SourceInput.Unchanged(input);
}
