using System.Net;
using System.Text.RegularExpressions;

namespace Shirobot.Plugin.RssSubscriber.Feeds;

public static class HtmlSanitizer
{
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex ImgSrcRegex = new(
        "<img[^>]*\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Strip(string? html, int maxLength)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var withoutTags = TagRegex.Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex.Replace(decoded, " ").Trim();

        if (maxLength > 0 && collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength] + "...";
        }

        return collapsed;
    }

    public static string? FindFirstImage(string? html, string? baseUri = null)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var match = ImgSrcRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var src = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }

        if (Uri.TryCreate(src, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (!string.IsNullOrWhiteSpace(baseUri) &&
            Uri.TryCreate(baseUri, UriKind.Absolute, out var baseAbsolute) &&
            Uri.TryCreate(baseAbsolute, src, out var combined))
        {
            return combined.ToString();
        }

        return null;
    }
}
