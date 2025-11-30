using CodeLearning.Application.DTOs.Course;
using CodeLearning.Application.Validators.Course;
using CodeLearning.Tests.Helpers;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Validators;

public class CreateCourseDtoValidatorTests
{
    private readonly CreateCourseDtoValidator _validator;

    public CreateCourseDtoValidatorTests()
    {
        _validator = new CreateCourseDtoValidator();
    }

    #region Valid Cases

    [Fact]
    public void Validate_ValidDto_ShouldPass()
    {
        // Arrange
        var dto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)] // Minimum valid length
    [InlineData(100)] // Medium length
    [InlineData(200)] // Maximum valid length
    public void Validate_ValidTitleLengths_ShouldPass(int length)
    {
        // Arrange
        var dto = TestDataBuilder.CreateValidCreateCourseDto(title: new string('A', length));

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue("title with {0} characters should be valid", length);
    }

    #endregion

    #region Title Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrNullTitle_ShouldFail(string? title)
    {
        // Arrange
        var dto = new CreateCourseDto
        {
            Title = title!,
            Description = "Valid description"
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => 
            e.PropertyName == nameof(CreateCourseDto.Title) && 
            e.ErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TitleTooLong_ShouldFail()
    {
        // Arrange
        var dto = TestDataBuilder.CreateValidCreateCourseDto(title: new string('A', 201));

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => 
            e.PropertyName == nameof(CreateCourseDto.Title) && 
            e.ErrorMessage.Contains("200"));
    }

    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    public void Validate_TitleAtBoundary_ShouldPass(int length)
    {
        // Arrange - Boundary testing
        var dto = TestDataBuilder.CreateValidCreateCourseDto(title: new string('A', length));

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue("title at boundary {0} should be valid", length);
    }

    #endregion

    #region Description Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrNullDescription_ShouldFail(string? description)
    {
        // Arrange
        var dto = new CreateCourseDto
        {
            Title = "Valid title",
            Description = description!
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => 
            e.PropertyName == nameof(CreateCourseDto.Description) && 
            e.ErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DescriptionTooLong_ShouldFail()
    {
        // Arrange
        var dto = new CreateCourseDto
        {
            Title = "Valid title",
            Description = new string('A', 5001)
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => 
            e.PropertyName == nameof(CreateCourseDto.Description) && 
            e.ErrorMessage.Contains("5000"));
    }

    [Theory]
    [InlineData(4999)]
    [InlineData(5000)]
    public void Validate_DescriptionAtBoundary_ShouldPass(int length)
    {
        // Arrange - Boundary testing
        var dto = new CreateCourseDto
        {
            Title = "Valid title",
            Description = new string('A', length)
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeTrue("description at boundary {0} should be valid", length);
    }

    #endregion

    #region Multiple Errors

    [Fact]
    public void Validate_BothFieldsInvalid_ShouldReturnMultipleErrors()
    {
        // Arrange
        var dto = new CreateCourseDto
        {
            Title = "", // Invalid
            Description = "" // Invalid
        };

        // Act
        var result = _validator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateCourseDto.Title));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateCourseDto.Description));
    }

    #endregion
}
