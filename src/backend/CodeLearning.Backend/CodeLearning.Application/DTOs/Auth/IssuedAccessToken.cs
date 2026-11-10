namespace CodeLearning.Application.DTOs.Auth;

public sealed record IssuedAccessToken(string Token, string Jti, DateTimeOffset ExpiresAt);
