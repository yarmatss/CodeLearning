using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Validators.Problem;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Validators;

public class ReorderTestCasesDtoValidatorTests
{
    private readonly ReorderTestCasesDtoValidator _validator;

    public ReorderTestCasesDtoValidatorTests()
    {
        _validator = new ReorderTestCasesDtoValidator();
    }

    [Fact]
    public void Validate_ValidDto_ShouldPass()
    {
        // Arrange
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds =
            [
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyTestCaseIdsList_ShouldFail()
    {
        // Arrange
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds = []
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TestCaseIds");
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("cannot be empty"));
    }

    [Fact]
    public void Validate_DuplicateTestCaseIds_ShouldFail()
    {
        // Arrange
        var duplicateId = Guid.NewGuid();
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds =
            [
                Guid.NewGuid(),
                duplicateId,
                Guid.NewGuid(),
                duplicateId // duplicate
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TestCaseIds");
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_MultipleDuplicates_ShouldFail()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds =
            [
                id1,
                id2,
                id1, // duplicate
                id2, // duplicate
                Guid.NewGuid()
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_SingleTestCaseId_ShouldPass()
    {
        // Arrange
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds = [Guid.NewGuid()]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TwoUniqueIds_ShouldPass()
    {
        // Arrange
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds =
            [
                Guid.NewGuid(),
                Guid.NewGuid()
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LargeNumberOfUniqueIds_ShouldPass()
    {
        // Arrange
        var testCaseIds = new List<Guid>();
        for (int i = 0; i < 100; i++)
        {
            testCaseIds.Add(Guid.NewGuid());
        }

        var dto = new ReorderTestCasesDto
        {
            TestCaseIds = testCaseIds
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllSameIds_ShouldFail()
    {
        // Arrange
        var sameId = Guid.NewGuid();
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds =
            [
                sameId,
                sameId,
                sameId,
                sameId
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_EmptyGuid_ShouldStillValidate()
    {
        // Arrange - Guid.Empty is technically a valid Guid, just semantically wrong
        var dto = new ReorderTestCasesDto
        {
            TestCaseIds =
            [
                Guid.Empty,
                Guid.NewGuid()
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        // Validator doesn't check for Guid.Empty, only for duplicates
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TwoDuplicatesInLargeList_ShouldFail()
    {
        // Arrange
        var testCaseIds = new List<Guid>();
        for (int i = 0; i < 50; i++)
        {
            testCaseIds.Add(Guid.NewGuid());
        }
        
        var duplicateId = testCaseIds[25]; // Pick one from middle
        testCaseIds.Add(duplicateId); // Add duplicate at end

        var dto = new ReorderTestCasesDto
        {
            TestCaseIds = testCaseIds
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Duplicate"));
    }
}
