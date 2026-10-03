using Shirobot.Plugin.RssSubscriber.Feeds;

namespace Shirobot.Plugin.RssSubscriber.Sources;

public sealed class SourceRegistry
{
    private readonly IReadOnlyList<ISourceProfile> _profiles;
    private readonly ISourceProfile _fallback = new GeneralSourceProfile();

    public SourceRegistry(IEnumerable<ISourceProfile> profiles)
    {
        _profiles = profiles.ToArray();
        var duplicate = _profiles.GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"重复的 RSS 来源 ID: {duplicate.Key}", nameof(profiles));
    }

    public static SourceRegistry CreateDefault() => new([
        new GitHubSourceProfile(), new GiteaSourceProfile(), new BilibiliSourceProfile(), new XSourceProfile()
    ]);

    public SourceSelection ResolveInput(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("RSS 地址必须是 HTTP(S) URL。", nameof(url));
        var profile = Match(uri) ?? _fallback;
        return new SourceSelection(profile, profile.Normalize(uri));
    }

    public ISourceProfile ResolveItem(FeedSource feed, FeedItem item)
    {
        if (Uri.TryCreate(item.Link, UriKind.Absolute, out var articleUri) &&
            articleUri.Scheme is "http" or "https" && Match(articleUri) is { } articleProfile)
            return articleProfile;
        if (!string.IsNullOrWhiteSpace(feed.SourceId) &&
            _profiles.FirstOrDefault(profile => profile.Id.Equals(feed.SourceId, StringComparison.OrdinalIgnoreCase)) is { } saved)
            return saved;
        if (Uri.TryCreate(feed.Url, UriKind.Absolute, out var feedUri) && Match(feedUri) is { } feedProfile)
            return feedProfile;
        return _fallback;
    }

    private ISourceProfile? Match(Uri uri) => _profiles.FirstOrDefault(profile => profile.Matches(uri));

    internal static bool HostIs(Uri uri, string domain) =>
        uri.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        uri.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
