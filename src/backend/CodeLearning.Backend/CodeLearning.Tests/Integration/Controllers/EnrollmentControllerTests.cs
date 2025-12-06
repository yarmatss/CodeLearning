using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeLearning.Application.DTOs.Enrollment;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CodeLearning.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for EnrollmentController
/// Tests course enrollment, unenrollment, and progress tracking
/// </summary>
public class EnrollmentControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private string _studentToken = null!;
    private string _teacherToken = null!;
    private Guid _publishedCourseId;
    private Guid _draftCourseId;

    public EnrollmentControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        _studentToken = await GetStudentTokenAsync();
        _teacherToken = await GetTeacherTokenAsync();
        (_publishedCourseId, _draftCourseId) = await CreateCoursesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Enroll Tests

    [Fact]
    public async Task Enroll_InPublishedCourse_ShouldReturn200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponseDto>();
        result.Should().NotBeNull();
        result!.CourseId.Should().Be(_publishedCourseId);
        result.Message.Should().Contain("Successfully enrolled");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
        var studentId = await GetCurrentStudentIdAsync(dbContext);
        
        var enrollment = await dbContext.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == _publishedCourseId && p.StudentId == studentId);

        enrollment.Should().NotBeNull();
    }

    [Fact]
    public async Task Enroll_InDraftCourse_ShouldReturn400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/enrollments/courses/{_draftCourseId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("unpublished");
    }

    [Fact]
    public async Task Enroll_AlreadyEnrolled_ShouldReturn400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);
        var response = await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Already enrolled");
    }

    [Fact]
    public async Task Enroll_NonExistingCourse_ShouldReturn404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/enrollments/courses/{Guid.NewGuid()}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Enroll_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Enroll_AsTeacher_ShouldReturn403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var response = await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Unenroll Tests

    [Fact]
    public async Task Unenroll_FromEnrolledCourse_ShouldReturn200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);
        var response = await _client.DeleteAsync($"/api/enrollments/courses/{_publishedCourseId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
        var studentId = await GetCurrentStudentIdAsync(dbContext);
        
        var enrollment = await dbContext.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == _publishedCourseId && p.StudentId == studentId);

        enrollment.Should().BeNull();
    }

    [Fact]
    public async Task Unenroll_NotEnrolled_ShouldReturn404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.DeleteAsync($"/api/enrollments/courses/{_publishedCourseId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Get My Courses Tests

    [Fact]
    public async Task GetMyCourses_WithEnrollments_ShouldReturnList()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);
        var response = await _client.GetAsync("/api/enrollments/my-courses");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var courses = await response.Content.ReadFromJsonAsync<List<EnrolledCourseDto>>();
        courses.Should().NotBeNull();
        courses!.Should().HaveCount(1);
        
        var course = courses.First();
        course.CourseId.Should().Be(_publishedCourseId);
        course.CompletedBlocksCount.Should().Be(0);
        course.TotalBlocksCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetMyCourses_NoEnrollments_ShouldReturnEmptyList()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.GetAsync("/api/enrollments/my-courses");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var courses = await response.Content.ReadFromJsonAsync<List<EnrolledCourseDto>>();
        courses.Should().NotBeNull();
        courses!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyCourses_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.GetAsync("/api/enrollments/my-courses");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Get Enrollment Status Tests

    [Fact]
    public async Task GetEnrollmentStatus_Enrolled_ShouldReturnTrue()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);
        await _client.PostAsync($"/api/enrollments/courses/{_publishedCourseId}", null);

        var response = await _client.GetAsync($"/api/enrollments/courses/{_publishedCourseId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"isEnrolled\":true");
    }

    [Fact]
    public async Task GetEnrollmentStatus_NotEnrolled_ShouldReturnFalse()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.GetAsync($"/api/enrollments/courses/{_publishedCourseId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"isEnrolled\":false");
    }

    #endregion

    #region Helper Methods

    private async Task<string> GetStudentTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<Application.DTOs.Auth.LoginResponseDto>();
        return content?.AccessToken ?? throw new Exception("No token");
    }

    private async Task<string> GetTeacherTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<Application.DTOs.Auth.LoginResponseDto>();
        return content?.AccessToken ?? throw new Exception("No token");
    }

    private async Task<(Guid publishedCourseId, Guid draftCourseId)> CreateCoursesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var teacher = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Teacher);

        var publishedCourse = new Course
        {
            Title = "Published Course",
            Description = "Test published course",
            Status = CourseStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow,
            InstructorId = teacher.Id,
            Instructor = teacher
        };

        var chapter = new Chapter
        {
            Title = "Chapter 1",
            OrderIndex = 1,
            Course = publishedCourse
        };

        var subchapter = new Subchapter
        {
            Title = "Subchapter 1.1",
            OrderIndex = 1,
            Chapter = chapter
        };

        var theoryContent = new TheoryContent
        {
            Content = "Test theory content",
            Block = null!
        };

        var block1 = new CourseBlock
        {
            Title = "Theory Block",
            Type = BlockType.Theory,
            OrderIndex = 1,
            Subchapter = subchapter,
            TheoryContent = theoryContent
        };

        var videoContent = new VideoContent
        {
            VideoUrl = "https://www.youtube.com/watch?v=test123456",
            VideoId = "test123456",
            Block = null!
        };

        var block2 = new CourseBlock
        {
            Title = "Video Block",
            Type = BlockType.Video,
            OrderIndex = 2,
            Subchapter = subchapter,
            VideoContent = videoContent
        };

        var draftCourse = new Course
        {
            Title = "Draft Course",
            Description = "Test draft course",
            Status = CourseStatus.Draft,
            InstructorId = teacher.Id,
            Instructor = teacher
        };

        dbContext.Courses.AddRange(publishedCourse, draftCourse);
        dbContext.Chapters.Add(chapter);
        dbContext.Subchapters.Add(subchapter);
        dbContext.TheoryContents.Add(theoryContent);
        dbContext.VideoContents.Add(videoContent);
        dbContext.CourseBlocks.AddRange(block1, block2);

        await dbContext.SaveChangesAsync();

        return (publishedCourse.Id, draftCourse.Id);
    }

    private async Task<Guid> GetCurrentStudentIdAsync(CodeLearning.Infrastructure.Data.ApplicationDbContext dbContext)
    {
        var student = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Student);
        return student.Id;
    }

    #endregion
}
