using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Validators.Problem;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Validators;

public class BulkAddTestCasesDtoValidatorTests
{
    private readonly BulkAddTestCasesDtoValidator _validator;

    public BulkAddTestCasesDtoValidatorTests()
    {
        _validator = new BulkAddTestCasesDtoValidator();
    }

    [Fact]
    public void Validate_ValidDto_ShouldPass()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1 2",
                    ExpectedOutput = "3",
                    IsPublic = true
                },
                new CreateTestCaseDto
                {
                    Input = "5 10",
                    ExpectedOutput = "15",
                    IsPublic = false
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyTestCasesList_ShouldFail()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases = []
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TestCases");
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("At least one test case is required"));
    }

    [Fact]
    public void Validate_NoPublicTestCases_ShouldFail()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1",
                    ExpectedOutput = "1",
                    IsPublic = false
                },
                new CreateTestCaseDto
                {
                    Input = "2",
                    ExpectedOutput = "2",
                    IsPublic = false
                },
                new CreateTestCaseDto
                {
                    Input = "3",
                    ExpectedOutput = "3",
                    IsPublic = false
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TestCases");
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("At least one test case must be public"));
    }

    [Fact]
    public void Validate_OnlyOnePublicTestCase_ShouldPass()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1",
                    ExpectedOutput = "1",
                    IsPublic = true
                },
                new CreateTestCaseDto
                {
                    Input = "2",
                    ExpectedOutput = "2",
                    IsPublic = false
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AllPublicTestCases_ShouldPass()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1",
                    ExpectedOutput = "1",
                    IsPublic = true
                },
                new CreateTestCaseDto
                {
                    Input = "2",
                    ExpectedOutput = "2",
                    IsPublic = true
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TestCaseWithEmptyExpectedOutput_ShouldFail()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = "1",
                    ExpectedOutput = "",
                    IsPublic = true
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Expected output is required"));
    }

    [Fact]
    public void Validate_TestCaseWithNullInput_ShouldFail()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = null!,
                    ExpectedOutput = "1",
                    IsPublic = true
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Input is required"));
    }

    [Fact]
    public void Validate_MultipleInvalidTestCases_ShouldFailWithMultipleErrors()
    {
        // Arrange
        var dto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto
                {
                    Input = null!,
                    ExpectedOutput = "",
                    IsPublic = false
                },
                new CreateTestCaseDto
                {
                    Input = "",
                    ExpectedOutput = null!,
                    IsPublic = false
                }
            ]
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("At least one test case must be public"));
    }

    [Fact]
    public void Validate_LargeNumberOfTestCases_WithAtLeastOnePublic_ShouldPass()
    {
        // Arrange
        var testCases = new List<CreateTestCaseDto>();
        
        // Add 99 private test cases
        for (int i = 0; i < 99; i++)
        {
            testCases.Add(new CreateTestCaseDto
            {
                Input = i.ToString(),
                ExpectedOutput = (i * 2).ToString(),
                IsPublic = false
            });
        }
        
        // Add 1 public test case
        testCases.Add(new CreateTestCaseDto
        {
            Input = "100",
            ExpectedOutput = "200",
            IsPublic = true
        });

        var dto = new BulkAddTestCasesDto
        {
            TestCases = testCases
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
