using System.Text.RegularExpressions;

namespace CodeLearning.Application.Extensions;

public static partial class YouTubeExtensions
{
    // Anchored: the whole string must be a YouTube watch or short link whose id is exactly 11 characters,
    // optionally followed by more query or fragment parameters. Shared by the validators and the service
    [GeneratedRegex(
        @"^(?:https?://)?(?:(?:www\.|m\.)?youtube\.com/watch\?(?:[^\s#]*&)?v=|youtu\.be/)(?<id>[A-Za-z0-9_-]{11})(?:[?&#][^\s]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VideoUrlRegex();

    public static string? ExtractYouTubeVideoId(this string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = VideoUrlRegex().Match(url.Trim());
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static bool IsValidYouTubeUrl(this string? url) => url.ExtractYouTubeVideoId() is not null;

    // The canonical form stored for every accepted link, whatever shape the author pasted
    public static string ToYouTubeWatchUrl(this string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        return $"https://www.youtube.com/watch?v={videoId}";
    }

    public static string ToYouTubeEmbedUrl(this string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        return $"https://www.youtube.com/embed/{videoId}";
    }
}
