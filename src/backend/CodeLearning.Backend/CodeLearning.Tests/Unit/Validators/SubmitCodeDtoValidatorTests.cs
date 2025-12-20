using CodeLearning.Application.DTOs.Submission;
using CodeLearning.Application.Validators.Submission;

using FluentValidation.TestHelper;

namespace CodeLearning.Tests.Unit.Validators;

public class SubmitCodeDtoValidatorTests
{
    private readonly SubmitCodeDtoValidator _validator;

    public SubmitCodeDtoValidatorTests()
    {
        _validator = new SubmitCodeDtoValidator();
    }

    [Fact]
    public void Validate_ValidDto_ShouldNotHaveErrors()
    {
        // Arrange
        var dto = new SubmitCodeDto
        {
            ProblemId = Guid.NewGuid(),
            LanguageId = Guid.NewGuid(),
            Code = "def solution():\n    pass"
        };

        // Act
        var result = _validator.TestValidate(dto);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyProblemId_ShouldHaveError()
    {
        // Arrange
        var dto = new SubmitCodeDto
        {
            ProblemId = Guid.Empty,
            LanguageId = Guid.NewGuid(),
            Code = "code"
        };

        // Act
        var result = _validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProblemId)
            .WithErrorMessage("Problem ID is required");
    }

    [Fact]
    public void Validate_EmptyLanguageId_ShouldHaveError()
    {
        // Arrange
        var dto = new SubmitCodeDto
        {
            ProblemId = Guid.NewGuid(),
            LanguageId = Guid.Empty,
            Code = "code"
        };

        // Act
        var result = _validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.LanguageId)
            .WithErrorMessage("Language ID is required");
    }

    [Fact]
    public void Validate_EmptyCode_ShouldHaveError()
    {
        // Arrange
        var dto = new SubmitCodeDto
        {
            ProblemId = Guid.NewGuid(),
            LanguageId = Guid.NewGuid(),
            Code = ""
        };

        // Act
        var result = _validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Code)
            .WithErrorMessage("Code is required");
    }

    [Fact]
    public void Validate_CodeTooLong_ShouldHaveError()
    {
        // Arrange
        var dto = new SubmitCodeDto
        {
            ProblemId = Guid.NewGuid(),
            LanguageId = Guid.NewGuid(),
            Code = new string('x', 100001) // 100KB + 1
        };

        // Act
        var result = _validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Code)
            .WithErrorMessage("Code cannot exceed 100KB");
    }
}
