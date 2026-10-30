using System.ComponentModel.DataAnnotations;

namespace CodeLearning.Application.Settings;

public class SubmissionRateLimitSettings
{
    public const string SectionName = "SubmissionRateLimit";

    // Submissions allowed per user within one window
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; init; } = 60;
}
