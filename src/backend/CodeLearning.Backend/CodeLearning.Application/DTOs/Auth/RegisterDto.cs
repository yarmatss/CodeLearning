namespace CodeLearning.Application.DTOs.Auth;

public class RegisterDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string ConfirmPassword { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Role { get; set; } // "Student" or "Teacher"
}
