namespace CodeLearning.Application.DTOs.Auth;

// What a successful register, login or refresh hands to the controller: the user and the tokens it
// sets as HttpOnly cookies. Tokens never go into a response body
public sealed record AuthSessionResult(
    AuthResponseDto User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
