namespace CodeLearning.Application.DTOs.Auth;

public class AuthResponseDto
{
    public required string UserId { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Role { get; set; }
    public required string Message { get; set; }
}
