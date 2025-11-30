using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Services;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace CodeLearning.Tests.Unit.Services;

public class CourseServiceTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly CourseService _courseService;
    private readonly SanitizationService _sanitizationService;
    private readonly UserManager<User> _userManager;
    private User _testInstructor = null!;

    public CourseServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _sanitizationService = new SanitizationService();
        _courseService = new CourseService(_fixture.DbContext, _sanitizationService);
        
        var userStore = new UserStore<User, IdentityRole<Guid>, Infrastructure.Data.ApplicationDbContext, Guid>(_fixture.DbContext);
        
        _userManager = new UserManager<User>(
            userStore,
            null!,
            new PasswordHasher<User>(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _testInstructor = TestDataBuilder.CreateValidUser(UserRole.Teacher);
        await _userManager.CreateAsync(_testInstructor);
        await _fixture.DbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Create Course Tests

    [Fact]
    public async Task CreateCourseAsync_WithValidData_ShouldCreateCourse()
    {
        // Arrange
        var dto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var result = await _courseService.CreateCourseAsync(dto, _testInstructor.Id);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be(dto.Title);
        result.Description.Should().Be(dto.Description);
        result.Status.Should().Be(CourseStatus.Draft);
        result.InstructorId.Should().Be(_testInstructor.Id);
        result.ChaptersCount.Should().Be(0);
        result.TotalBlocks.Should().Be(0);
        
        // Verify in database
        var courseInDb = await _fixture.DbContext.Courses.FindAsync(result.Id);
        courseInDb.Should().NotBeNull();
        courseInDb!.Title.Should().Be(dto.Title);
    }

    [Fact]
    public async Task CreateCourseAsync_WithInvalidInstructorId_ShouldThrowException()
    {
        // Arrange
        var dto = TestDataBuilder.CreateValidCreateCourseDto();
        var invalidInstructorId = Guid.NewGuid();

        // Act
        var act = async () => await _courseService.CreateCourseAsync(dto, invalidInstructorId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Instructor not found*");
    }

    #endregion

    #region Get Course Tests

    [Fact]
    public async Task GetCourseByIdAsync_WithExistingCourse_ShouldReturnCourse()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await _courseService.GetCourseByIdAsync(course.Id);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(course.Id);
        result.Title.Should().Be(course.Title);
        result.InstructorName.Should().Be($"{_testInstructor.FirstName} {_testInstructor.LastName}");
    }

    [Fact]
    public async Task GetCourseByIdAsync_WithNonExistingCourse_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var nonExistingId = Guid.NewGuid();

        // Act
        var act = async () => await _courseService.GetCourseByIdAsync(nonExistingId);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"Course with ID {nonExistingId} not found");
    }

    #endregion

    #region Update Course Tests

    [Fact]
    public async Task UpdateCourseAsync_WithValidData_ShouldUpdateCourse()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor, CourseStatus.Draft);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        var updateDto = TestDataBuilder.CreateValidCreateCourseDto("Updated Title");

        // Act
        var result = await _courseService.UpdateCourseAsync(course.Id, new Application.DTOs.Course.UpdateCourseDto 
        { 
            Title = updateDto.Title, 
            Description = updateDto.Description 
        }, _testInstructor.Id);

        // Assert
        result.Title.Should().Be(updateDto.Title);
        result.Description.Should().Be(updateDto.Description);

        // Verify in database
        var updatedCourse = await _fixture.DbContext.Courses.FindAsync(course.Id);
        updatedCourse!.Title.Should().Be(updateDto.Title);
    }

    [Fact]
    public async Task UpdateCourseAsync_WithPublishedCourse_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor, CourseStatus.Published);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        var updateDto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var act = async () => await _courseService.UpdateCourseAsync(course.Id, new Application.DTOs.Course.UpdateCourseDto 
        { 
            Title = updateDto.Title, 
            Description = updateDto.Description 
        }, _testInstructor.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot update a published course");
    }

    [Fact]
    public async Task UpdateCourseAsync_WithWrongInstructor_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        var otherInstructor = TestDataBuilder.CreateValidUser(UserRole.Teacher);
        await _userManager.CreateAsync(otherInstructor);

        var updateDto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var act = async () => await _courseService.UpdateCourseAsync(course.Id, new Application.DTOs.Course.UpdateCourseDto 
        { 
            Title = updateDto.Title, 
            Description = updateDto.Description 
        }, otherInstructor.Id);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("You can only update your own courses");
    }

    #endregion

    #region Publish Course Tests

    [Fact]
    public async Task PublishCourseAsync_WithoutChapters_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var act = async () => await _courseService.PublishCourseAsync(course.Id, _testInstructor.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must have at least one chapter*");
    }

    [Fact]
    public async Task PublishCourseAsync_WithCompleteHierarchy_ShouldPublishCourse()
    {
        // Arrange
        var (course, chapter, subchapter) = TestDataBuilder.CreateCourseHierarchy(_testInstructor);
        
        // Add a block to satisfy publish validation
        var block = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Test Block",
            SubchapterId = subchapter.Id,
            Subchapter = subchapter,
            Type = BlockType.Theory,
            OrderIndex = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        
        _fixture.DbContext.Courses.Add(course);
        _fixture.DbContext.Chapters.Add(chapter);
        _fixture.DbContext.Subchapters.Add(subchapter);
        _fixture.DbContext.CourseBlocks.Add(block);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await _courseService.PublishCourseAsync(course.Id, _testInstructor.Id);

        // Assert
        result.Status.Should().Be(CourseStatus.Published);
        result.PublishedAt.Should().NotBeNull();
        result.PublishedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Delete Course Tests

    [Fact]
    public async Task DeleteCourseAsync_WithDraftCourse_ShouldDeleteCourse()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        await _courseService.DeleteCourseAsync(course.Id, _testInstructor.Id);

        // Assert
        var deletedCourse = await _fixture.DbContext.Courses.FindAsync(course.Id);
        deletedCourse.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCourseAsync_WithPublishedCourse_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var course = TestDataBuilder.CreateValidCourse(_testInstructor, CourseStatus.Published);
        _fixture.DbContext.Courses.Add(course);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var act = async () => await _courseService.DeleteCourseAsync(course.Id, _testInstructor.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot delete a published course");
    }

    #endregion
}
