namespace Shirobot.Plugin.RssSubscriber.Feeds;

public interface IFeedPlatformProfile
{
    string Id { get; }
    string DisplayName { get; }
    bool Matches(string generator);
}

public sealed class WordPressFeedPlatformProfile : IFeedPlatformProfile
{
    public string Id => "wordpress";
    public string DisplayName => "WordPress";

    public bool Matches(string generator) =>
        generator.Contains("wordpress.org", StringComparison.OrdinalIgnoreCase) ||
        generator.Contains("wordpress", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Identifies the CMS or publishing platform declared by the RSS/Atom generator field.</summary>
public sealed class FeedPlatformRegistry
{
    private readonly IReadOnlyList<IFeedPlatformProfile> _profiles;

    public FeedPlatformRegistry(IEnumerable<IFeedPlatformProfile> profiles) => _profiles = profiles.ToArray();

    public static FeedPlatformRegistry CreateDefault() => new([new WordPressFeedPlatformProfile()]);

    public IFeedPlatformProfile? Resolve(string? generator) =>
        string.IsNullOrWhiteSpace(generator) ? null : _profiles.FirstOrDefault(profile => profile.Matches(generator));
}
