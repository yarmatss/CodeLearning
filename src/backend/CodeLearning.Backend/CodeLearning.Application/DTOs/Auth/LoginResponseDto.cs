namespace CodeLearning.Application.DTOs.Auth;

// Tokens are delivered only as HttpOnly cookies; the expiry lets the client schedule a refresh
public class LoginResponseDto
{
    public required string UserId { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Role { get; set; }
    public required string Message { get; set; }
    public required DateTimeOffset AccessTokenExpiresAt { get; set; }
}
