using System.ComponentModel.DataAnnotations;

namespace CodeLearning.Application.Settings;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    // HMAC-SHA256 needs a key of at least 256 bits; supplied via environment (.env), never appsettings
    [Required]
    [MinLength(32)]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int AccessTokenExpirationMinutes { get; init; } = 60;

    [Range(1, int.MaxValue)]
    public int RefreshTokenExpirationDays { get; init; } = 7;
}
