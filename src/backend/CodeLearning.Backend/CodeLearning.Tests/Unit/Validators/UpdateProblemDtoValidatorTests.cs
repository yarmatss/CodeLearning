using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Validators.Problem;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Validators;

public class UpdateProblemDtoValidatorTests
{
    private readonly UpdateProblemDtoValidator _validator;

    public UpdateProblemDtoValidatorTests()
    {
        _validator = new UpdateProblemDtoValidator();
    }

    [Fact]
    public void Validate_ValidDto_ShouldPass()
    {
        // Arrange
        var dto = new UpdateProblemDto
        {
            Title = "Updated Problem Title",
            Description = "Updated description with sufficient length to pass validation",
            Difficulty = "Medium",
            TagIds = []
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
    [InlineData(null)]
    public void Validate_EmptyTitle_ShouldFail(string? title)
    {
        // Arrange
        var dto = new UpdateProblemDto
        {
            Title = title!,
            Description = "Valid description with more than 20 characters",
            Difficulty = "Medium"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validate_TitleTooLong_ShouldFail()
    {
        // Arrange
        var dto = new UpdateProblemDto
        {
            Title = new string('A', 201),
            Description = "Valid description with more than 20 characters",
            Difficulty = "Medium"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short")]
    [InlineData("Less than twenty")]
    public void Validate_DescriptionTooShort_ShouldFail(string description)
    {
        // Arrange
        var dto = new UpdateProblemDto
        {
            Title = "Valid Title",
            Description = description,
            Difficulty = "Medium"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("MEDIUM")]
    [InlineData("Difficult")]
    [InlineData("")]
    public void Validate_InvalidDifficulty_ShouldFail(string difficulty)
    {
        // Arrange
        var dto = new UpdateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = difficulty
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Difficulty");
    }

    [Theory]
    [InlineData("Easy")]
    [InlineData("Medium")]
    [InlineData("Hard")]
    public void Validate_ValidDifficulty_ShouldPass(string difficulty)
    {
        // Arrange
        var dto = new UpdateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = difficulty
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
