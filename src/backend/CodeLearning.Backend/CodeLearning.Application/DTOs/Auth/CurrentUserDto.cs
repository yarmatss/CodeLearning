namespace CodeLearning.Application.DTOs.Auth;

public class CurrentUserDto
{
    public required string UserId { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Role { get; set; }

    // When the access token that authenticated this request expires (its exp claim, UTC)
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
}
