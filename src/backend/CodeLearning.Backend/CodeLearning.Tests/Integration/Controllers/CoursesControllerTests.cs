using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.DTOs.Course;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;

namespace CodeLearning.Tests.Integration.Controllers;

public class CoursesControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;

    public CoursesControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    #region Helper Methods

    private async Task<string> GetTeacherToken()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");

        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        
        return content?.AccessToken ?? throw new Exception("No teacher token received");
    }

    private async Task<string> GetStudentToken()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");

        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        
        return content?.AccessToken ?? throw new Exception("No student token received");
    }

    #endregion

    #region Create Course Tests

    [Fact]
    public async Task CreateCourse_AsTeacher_ShouldReturn201()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var response = await _client.PostAsJsonAsync("/api/courses", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<CourseResponseDto>();
        content.Should().NotBeNull();
        content!.Title.Should().Be(createDto.Title);
        content.Status.ToString().Should().Be("Draft");
    }

    [Fact]
    public async Task CreateCourse_AsStudent_ShouldReturn403()
    {
        // Arrange
        var token = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var response = await _client.PostAsJsonAsync("/api/courses", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCourse_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var createDto = TestDataBuilder.CreateValidCreateCourseDto();

        // Act
        var response = await _client.PostAsJsonAsync("/api/courses", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateCourse_EmptyTitle_ShouldReturn400()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = new CreateCourseDto
        {
            Title = "",
            Description = "Description"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/courses", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Courses Tests

    [Fact]
    public async Task GetMyCourses_AsTeacher_ShouldReturnCourses()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create a course first
        var createDto = TestDataBuilder.CreateValidCreateCourseDto("My Course");
        await _client.PostAsJsonAsync("/api/courses", createDto);

        // Act
        var response = await _client.GetAsync("/api/courses/my-courses");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var courses = await response.Content.ReadFromJsonAsync<List<CourseResponseDto>>();
        courses.Should().NotBeNullOrEmpty();
        courses!.Should().Contain(c => c.Title == "My Course");
    }

    [Fact]
    public async Task GetPublishedCourses_ShouldReturnOnlyPublished()
    {
        // Act
        var response = await _client.GetAsync("/api/courses/published");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var courses = await response.Content.ReadFromJsonAsync<List<CourseResponseDto>>();
        courses.Should().NotBeNull();
        // All returned courses should be Published
        courses!.Should().OnlyContain(c => c.Status.ToString() == "Published");
    }

    #endregion

    #region Update Course Tests

    [Fact]
    public async Task UpdateCourse_AsOwner_ShouldReturn200()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateCourseDto("Original Title");
        var createResponse = await _client.PostAsJsonAsync("/api/courses", createDto);
        var createdCourse = await createResponse.Content.ReadFromJsonAsync<CourseResponseDto>();

        var updateDto = new UpdateCourseDto
        {
            Title = "Updated Title",
            Description = "Updated Description"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/courses/{createdCourse!.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<CourseResponseDto>();
        updated!.Title.Should().Be(updateDto.Title);
    }

    #endregion

    #region Delete Course Tests

    [Fact]
    public async Task DeleteCourse_AsOwner_ShouldReturn200()
    {
        // Arrange
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateCourseDto("Course to Delete");
        var createResponse = await _client.PostAsJsonAsync("/api/courses", createDto);
        var createdCourse = await createResponse.Content.ReadFromJsonAsync<CourseResponseDto>();

        // Act
        var response = await _client.DeleteAsync($"/api/courses/{createdCourse!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it's deleted
        var getResponse = await _client.GetAsync($"/api/courses/{createdCourse.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
