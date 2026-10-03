namespace Shirobot.Plugin.RssSubscriber.Sources;

public interface ISourceProfile
{
    string Id { get; }
    string DisplayName { get; }
    bool Matches(Uri uri);
    SourceInput Normalize(Uri input);
}

public sealed record SourceInput(string FeedUrl, string? SuggestedFeedId = null)
{
    public static SourceInput Unchanged(Uri uri) => new(uri.AbsoluteUri);
}

public sealed record SourceSelection(ISourceProfile Profile, SourceInput Input);
