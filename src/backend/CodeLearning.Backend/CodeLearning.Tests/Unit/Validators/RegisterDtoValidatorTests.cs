using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Validators.Auth;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Validators;

public class RegisterDtoValidatorTests
{
    private readonly RegisterDtoValidator _validator;

    public RegisterDtoValidatorTests()
    {
        _validator = new RegisterDtoValidator();
    }

    [Fact]
    public void Validate_ValidDto_ShouldPass()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password123",
            ConfirmPassword = "Password123",
            FirstName = "John",
            LastName = "Doe",
            Role = "Student"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    public void Validate_InvalidEmail_ShouldFail(string email)
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = email,
            Password = "Password123",
            ConfirmPassword = "Password123",
            FirstName = "John",
            LastName = "Doe",
            Role = "Student"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("12345678")]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    public void Validate_WeakPassword_ShouldFail(string password)
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = "test@example.com",
            Password = password,
            ConfirmPassword = password,
            FirstName = "John",
            LastName = "Doe",
            Role = "Student"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Validate_PasswordMismatch_ShouldFail()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password123",
            ConfirmPassword = "DifferentPassword123",
            FirstName = "John",
            LastName = "Doe",
            Role = "Student"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmPassword");
    }

    [Theory]
    [InlineData("")]
    [InlineData("InvalidRole")]
    [InlineData("Admin")]
    public void Validate_InvalidRole_ShouldFail(string role)
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password123",
            ConfirmPassword = "Password123",
            FirstName = "John",
            LastName = "Doe",
            Role = role
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyFirstName_ShouldFail(string firstName)
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password123",
            ConfirmPassword = "Password123",
            FirstName = firstName,
            LastName = "Doe",
            Role = "Student"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FirstName");
    }

    [Fact]
    public void Validate_FirstNameTooLong_ShouldFail()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Email = "test@example.com",
            Password = "Password123",
            ConfirmPassword = "Password123",
            FirstName = new string('A', 51),
            LastName = "Doe",
            Role = "Student"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FirstName");
    }
}
