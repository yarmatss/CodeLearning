using System.Text.RegularExpressions;

namespace CodeLearning.Application.Extensions;

public static partial class YouTubeExtensions
{
    [GeneratedRegex(@"(?:youtube\.com\/watch\?v=|youtu\.be\/)([a-zA-Z0-9_-]{11})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VideoIdRegex();

    // YouTube URL (youtube.com/watch?v=... or youtu.be/...)
    public static string? ExtractYouTubeVideoId(this string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = VideoIdRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IsValidYouTubeUrl(this string url)
    {
        return !string.IsNullOrWhiteSpace(url) && VideoIdRegex().IsMatch(url);
    }

    public static string ToYouTubeEmbedUrl(this string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        return $"https://www.youtube.com/embed/{videoId}";
    }
}
