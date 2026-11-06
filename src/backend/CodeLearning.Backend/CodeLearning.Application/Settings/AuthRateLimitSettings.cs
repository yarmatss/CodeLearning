using System.ComponentModel.DataAnnotations;

namespace CodeLearning.Application.Settings;

public class AuthRateLimitSettings
{
    public const string SectionName = "AuthRateLimit";

    // Login, register and refresh requests allowed per client IP within one window
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; init; } = 60;
}
