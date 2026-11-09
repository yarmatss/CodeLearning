using System.ComponentModel.DataAnnotations;

namespace CodeLearning.Application.Settings;

public class AuthRateLimitSettings
{
    public const string SectionName = "AuthRateLimit";

    // Register requests allowed per client IP, and login requests allowed per client IP and email,
    // within one window. Refresh is not limited
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; init; } = 60;

    // Ceiling on login requests per client IP across all emails, so one address cannot try passwords
    // against many accounts. Defaults to five times PermitLimit
    [Range(1, int.MaxValue)]
    public int? LoginPerIpPermitLimit { get; init; }

    public int EffectiveLoginPerIpPermitLimit =>
        LoginPerIpPermitLimit ?? (int)Math.Min(int.MaxValue, PermitLimit * 5L);
}
