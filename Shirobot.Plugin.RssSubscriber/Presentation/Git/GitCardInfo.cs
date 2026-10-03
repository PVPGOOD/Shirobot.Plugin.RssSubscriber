using Shirobot.Plugin.RssSubscriber.Feeds;

namespace Shirobot.Plugin.RssSubscriber.Presentation.Git;

public sealed record GitCardInfo(
    string EventType,
    string? Status,
    string? Repository,
    string? Reference)
{
    public static GitCardInfo From(FeedItem item)
    {
        var path = Uri.TryCreate(item.Link, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            : [];
        var routeIndex = Array.FindIndex(path, segment =>
            segment.Equals("pull", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("pulls", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("issues", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("commit", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("commits", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("compare", StringComparison.OrdinalIgnoreCase));
        var route = routeIndex >= 0 ? path[routeIndex].ToLowerInvariant() : string.Empty;
        var isPullRequest = route is "pull" or "pulls" ||
                            item.Title.Contains("pull request", StringComparison.OrdinalIgnoreCase);
        var isIssue = route == "issues" ||
                      item.Title.Contains(" issue", StringComparison.OrdinalIgnoreCase);
        var isCommit = route is "commit" or "commits" or "compare" ||
                       item.Title.Contains("pushed to", StringComparison.OrdinalIgnoreCase);

        var eventType = isPullRequest ? "Pull Request"
            : isIssue ? "Issue"
            : isCommit ? "Commit"
            : "Git activity";

        var title = item.Title;
        string? status = null;
        if (isPullRequest || isIssue)
        {
            if (title.Contains("merged", StringComparison.OrdinalIgnoreCase)) status = "MERGED";
            else if (title.Contains("closed", StringComparison.OrdinalIgnoreCase)) status = "CLOSED";
            else if (title.Contains("reopened", StringComparison.OrdinalIgnoreCase) ||
                     title.Contains("opened", StringComparison.OrdinalIgnoreCase) ||
                     title.Contains("created", StringComparison.OrdinalIgnoreCase)) status = "OPEN";
        }

        var repository = path.Length >= 2 ? $"{path[0]}/{path[1]}" : null;
        var reference = route switch
        {
            "pull" or "pulls" or "issues" when routeIndex + 1 < path.Length => "#" + path[routeIndex + 1],
            "commit" or "commits" when routeIndex + 1 < path.Length => ShortHash(path[routeIndex + 1]),
            "compare" when routeIndex + 1 < path.Length => "compare " + ShortHash(path[routeIndex + 1].Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path[routeIndex + 1]),
            _ => null
        };

        return new GitCardInfo(eventType, status, repository, reference);
    }

    public static string ResolveVariant(FeedSource feed, FeedItem item)
    {
        var info = From(item);
        return info.EventType switch
        {
            "Pull Request" => "pull_request",
            "Issue" => "issue",
            "Commit" => "commit",
            _ => "activity"
        };
    }

    private static string ShortHash(string hash) => hash.Length > 8 ? hash[..8] : hash;
}
