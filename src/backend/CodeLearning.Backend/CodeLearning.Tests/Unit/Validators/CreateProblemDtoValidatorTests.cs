using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Validators.Problem;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Validators;

public class CreateProblemDtoValidatorTests
{
    private readonly CreateProblemDtoValidator _validator;

    public CreateProblemDtoValidatorTests()
    {
        _validator = new CreateProblemDtoValidator();
    }

    [Fact]
    public void Validate_ValidDto_ShouldPass()
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Two Sum Problem",
            Description = "Given an array of integers and a target sum, find two numbers that add up to the target.",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1 2",
                    ExpectedOutput = "3",
                    IsPublic = true
                }
            ],
            StarterCodes = [],
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
        var dto = new CreateProblemDto
        {
            Title = title!,
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true }
            ]
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
        var dto = new CreateProblemDto
        {
            Title = new string('A', 201),
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true }
            ]
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
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = description,
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("EASY")]
    [InlineData("Difficult")]
    [InlineData("")]
    public void Validate_InvalidDifficulty_ShouldFail(string difficulty)
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = difficulty,
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Difficulty");
    }

    [Fact]
    public void Validate_NoTestCases_ShouldFail()
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases = []
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TestCases");
    }

    [Fact]
    public void Validate_NoPublicTestCases_ShouldFail()
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = false },
                new CreateTestCaseDto { Input = "2", ExpectedOutput = "2", IsPublic = false }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TestCases");
    }

    [Fact]
    public void Validate_TestCaseWithEmptyExpectedOutput_ShouldFail()
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "", IsPublic = true }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ExpectedOutput"));
    }

    [Fact]
    public void Validate_StarterCodeWithEmptyCode_ShouldFail()
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true }
            ],
            StarterCodes =
            [
                new CreateStarterCodeDto { Code = "", LanguageId = Guid.NewGuid() }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Code"));
    }

    [Fact]
    public void Validate_StarterCodeWithEmptyLanguageId_ShouldFail()
    {
        // Arrange
        var dto = new CreateProblemDto
        {
            Title = "Valid Title",
            Description = "Valid description with more than 20 characters",
            Difficulty = "Easy",
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = true }
            ],
            StarterCodes =
            [
                new CreateStarterCodeDto { Code = "def solution():\n    pass", LanguageId = Guid.Empty }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("LanguageId"));
    }
}
