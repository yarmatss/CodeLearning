using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Course;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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
        return response.GetAccessToken();
    }

    private async Task<string> GetStudentToken()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");

        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        return response.GetAccessToken();
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
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync("/api/courses", createDto);

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

        var courses = await response.Content.ReadFromJsonAsync<PagedResult<CourseResponseDto>>();
        courses.Should().NotBeNull();
        courses!.Items.Should().OnlyContain(c => c.Status == CourseStatus.Published);
    }

    [Fact]
    public async Task GetPublishedCourses_ShouldPageAndCountBlocks()
    {
        // Arrange: three published courses, one with two blocks, and a draft that must not be listed
        var teacher = await SeedTeacherAsync();
        var withBlocks = await SeedCourseAsync(teacher, CourseStatus.Published, blocks: 2, publishedAt: DateTimeOffset.UtcNow);
        await SeedCourseAsync(teacher, CourseStatus.Published, publishedAt: DateTimeOffset.UtcNow.AddDays(-1));
        await SeedCourseAsync(teacher, CourseStatus.Published, publishedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await SeedCourseAsync(teacher, CourseStatus.Draft);
        var anonymous = _factory.CreateAnonymousClient();

        // Act
        var firstPage = await anonymous.GetFromJsonAsync<PagedResult<CourseResponseDto>>("/api/courses/published?page=1&pageSize=2");
        var secondPage = await anonymous.GetFromJsonAsync<PagedResult<CourseResponseDto>>("/api/courses/published?page=2&pageSize=2");

        // Assert
        firstPage!.TotalCount.Should().Be(3);
        firstPage.TotalPages.Should().Be(2);
        firstPage.Items.Should().HaveCount(2);
        firstPage.Items[0].Id.Should().Be(withBlocks);
        firstPage.Items[0].TotalBlocks.Should().Be(2);
        firstPage.Items[0].ChaptersCount.Should().Be(1);
        secondPage!.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("page=0&pageSize=0", 1, 1)]
    [InlineData("page=-5&pageSize=1000", 1, 100)]
    [InlineData("", 1, 20)]
    public async Task GetPublishedCourses_OutOfRangePaging_ShouldBeClamped(string query, int expectedPage, int expectedPageSize)
    {
        // Act
        var page = await _factory.CreateAnonymousClient()
            .GetFromJsonAsync<PagedResult<CourseResponseDto>>($"/api/courses/published?{query}");

        // Assert
        page!.Page.Should().Be(expectedPage);
        page.PageSize.Should().Be(expectedPageSize);
    }

    [Fact]
    public async Task GetMyStats_ShouldCountCoursesAndDistinctStudents()
    {
        // Arrange
        var token = await GetTeacherToken();
        var teacher = await GetUserByTokenAsync(token);
        var published1 = await SeedCourseAsync(teacher, CourseStatus.Published);
        var published2 = await SeedCourseAsync(teacher, CourseStatus.Published);
        await SeedCourseAsync(teacher, CourseStatus.Draft);
        await SeedCourseAsync(await SeedTeacherAsync(), CourseStatus.Published); // someone else's

        var student1 = await GetStudentToken();
        var student2 = await GetStudentToken();
        await EnrollAsync(student1, published1);
        await EnrollAsync(student1, published2);
        await EnrollAsync(student2, published1);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/courses/my/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = await response.Content.ReadFromJsonAsync<TeacherStatsDto>();
        stats!.PublishedCourses.Should().Be(2);
        stats.DraftCourses.Should().Be(1);
        stats.TotalStudents.Should().Be(2);
    }

    [Fact]
    public async Task GetMyStats_AsStudent_ShouldReturn403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetStudentToken());

        var response = await _client.GetAsync("/api/courses/my/stats");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    #region Draft Visibility Tests

    [Fact]
    public async Task GetCourseById_DraftCourse_AsOwner_ShouldReturn200()
    {
        // Arrange
        var courseId = await CreateDraftCourseAsync();

        // Act
        var response = await _client.GetAsync($"/api/courses/{courseId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCourseById_DraftCourse_WithoutAuth_ShouldReturn404()
    {
        // Arrange
        var courseId = await CreateDraftCourseAsync();

        // Act
        var response = await _factory.CreateAnonymousClient().GetAsync($"/api/courses/{courseId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCourseStructure_DraftCourse_AsAnotherTeacher_ShouldReturn404()
    {
        // Arrange
        var courseId = await CreateDraftCourseAsync();
        var anotherTeacherToken = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherTeacherToken);

        // Act
        var response = await _client.GetAsync($"/api/courses/{courseId}/structure");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCourseChapters_DraftCourse_AsStudent_ShouldReturn404()
    {
        // Arrange
        var courseId = await CreateDraftCourseAsync();
        var studentToken = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        // Act
        var response = await _client.GetAsync($"/api/courses/{courseId}/chapters");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Leaves the client authenticated as the course owner
    private async Task<Guid> CreateDraftCourseAsync()
    {
        var token = await GetTeacherToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync("/api/courses", TestDataBuilder.CreateValidCreateCourseDto());
        var createdCourse = await createResponse.Content.ReadFromJsonAsync<CourseResponseDto>();

        return createdCourse!.Id;
    }

    #endregion

    #region Publish, Unpublish And Ownership Tests

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("POST", "/publish")]
    [InlineData("POST", "/unpublish")]
    [InlineData("DELETE", "")]
    public async Task CourseMutation_AsAnotherTeacher_ShouldReturn403(string method, string suffix)
    {
        // Arrange
        var owner = await SeedTeacherAsync();
        var courseId = await SeedCourseAsync(owner, CourseStatus.Draft, blocks: 1);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTeacherToken());
        var url = $"/api/courses/{courseId}{suffix}";

        // Act
        var response = method switch
        {
            "PUT" => await _client.PutAsJsonAsync(url, new UpdateCourseDto { Title = "Hijack", Description = "x" }),
            "POST" => await _client.PostAsync(url, null),
            _ => await _client.DeleteAsync(url)
        };

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var db = NewDbContext();
        var course = await db.Courses.SingleAsync(c => c.Id == courseId);
        course.Title.Should().NotBe("Hijack");
        course.Status.Should().Be(CourseStatus.Draft);
    }

    [Fact]
    public async Task UnpublishCourse_ShouldReturnToDraftAndKeepEnrollments()
    {
        // Arrange
        var (token, teacher) = await RegisterTeacherAsync();
        var courseId = await SeedCourseAsync(teacher, CourseStatus.Published, blocks: 1);
        var studentToken = await GetStudentToken();
        await EnrollAsync(studentToken, courseId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var first = await _client.PostAsync($"/api/courses/{courseId}/unpublish", null);
        var second = await _client.PostAsync($"/api/courses/{courseId}/unpublish", null);

        // Assert: idempotent, and the enrollment survives
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        var course = await db.Courses.SingleAsync(c => c.Id == courseId);
        course.Status.Should().Be(CourseStatus.Draft);
        course.PublishedAt.Should().BeNull();
        (await db.StudentCourseProgresses.CountAsync(p => p.CourseId == courseId)).Should().Be(1);

        // The course can be edited again while unpublished
        var update = await _client.PutAsJsonAsync($"/api/courses/{courseId}", new UpdateCourseDto { Title = "Edited", Description = "d" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublishCourse_Twice_ShouldBeIdempotent()
    {
        // Arrange
        var (token, teacher) = await RegisterTeacherAsync();
        var courseId = await SeedCourseAsync(teacher, CourseStatus.Draft, blocks: 1);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var first = await _client.PostAsync($"/api/courses/{courseId}/publish", null);
        var second = await _client.PostAsync($"/api/courses/{courseId}/publish", null);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        (await db.Courses.SingleAsync(c => c.Id == courseId)).Status.Should().Be(CourseStatus.Published);
    }

    [Theory]
    [InlineData(false, true, "test case")]
    [InlineData(true, false, "starter code")]
    [InlineData(true, true, null)]
    public async Task PublishCourse_WithProblemBlock_ShouldRequireTestsAndStarterCode(
        bool withTestCase, bool withStarterCode, string? expectedError)
    {
        // Arrange
        var (token, teacher) = await RegisterTeacherAsync();
        var courseId = await SeedCourseWithProblemAsync(teacher, withTestCase, withStarterCode);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.PostAsync($"/api/courses/{courseId}/publish", null);

        // Assert
        if (expectedError is null)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(expectedError);
        await using var db = NewDbContext();
        (await db.Courses.SingleAsync(c => c.Id == courseId)).Status.Should().Be(CourseStatus.Draft);
    }

    [Fact]
    public async Task PublishCourse_WithStarterCodeOnlyInDisabledLanguage_ShouldReturn400()
    {
        // Arrange
        var (token, teacher) = await RegisterTeacherAsync();
        var courseId = await SeedCourseWithProblemAsync(teacher, withTestCase: true, withStarterCode: true, disableLanguage: true);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            // Act
            var response = await _client.PostAsync($"/api/courses/{courseId}/publish", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("enabled language");
        }
        finally
        {
            // Languages are seed data that survive the reset between tests
            await using var db = NewDbContext();
            await db.Languages.Where(l => l.Id == PythonLanguageId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsEnabled, true));
        }
    }

    [Fact]
    public async Task GetCourseStructure_Anonymous_ShouldListBlockOutlineWithoutContent()
    {
        // Arrange
        var teacher = await SeedTeacherAsync();
        var courseId = await SeedCourseAsync(teacher, CourseStatus.Published, blocks: 2);

        // Act
        var response = await _factory.CreateAnonymousClient().GetAsync($"/api/courses/{courseId}/structure");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var structure = await response.Content.ReadFromJsonAsync<CourseStructureDto>();
        var blocks = structure!.Chapters.Single().Subchapters.Single().Blocks;
        blocks.Select(b => b.OrderIndex).Should().Equal(1, 2);
        blocks.Should().OnlyContain(b => b.Type == BlockType.Theory);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(SecretTheoryContent);
    }

    [Fact]
    public async Task DeleteCourse_UnpublishedWithQuizAttempts_ShouldRemoveEverything()
    {
        // Arrange: a student attempted the quiz before the course was unpublished
        var (token, teacher) = await RegisterTeacherAsync();
        var courseId = await SeedCourseAsync(teacher, CourseStatus.Draft, blocks: 1, withQuiz: true);
        var studentToken = await GetStudentToken();
        var student = await GetUserByTokenAsync(studentToken);
        Guid quizId;
        await using (var db = NewDbContext())
        {
            quizId = await db.CourseBlocks.Where(b => b.Subchapter.Chapter.CourseId == courseId && b.QuizId != null)
                .Select(b => b.QuizId!.Value).SingleAsync();
            db.StudentQuizAttempts.Add(new StudentQuizAttempt
            {
                StudentId = student.Id, QuizId = quizId, Score = 100, AttemptedAt = DateTimeOffset.UtcNow,
                Student = null!, Quiz = null!
            });
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.DeleteAsync($"/api/courses/{courseId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = NewDbContext();
        (await verify.Quizzes.AnyAsync(q => q.Id == quizId)).Should().BeFalse();
        (await verify.StudentQuizAttempts.AnyAsync(a => a.QuizId == quizId)).Should().BeFalse();
        (await verify.TheoryContents.AnyAsync(t => t.Content == SecretTheoryContent)).Should().BeFalse();
    }

    #endregion

    #region Seeding Helpers

    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string SecretTheoryContent = "SECRET-THEORY-CONTENT";

    private ApplicationDbContext NewDbContext()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    }

    private async Task<User> SeedTeacherAsync()
    {
        await using var db = NewDbContext();
        var teacher = TestDataBuilder.CreateValidUser(UserRole.Teacher);
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        return teacher;
    }

    private async Task<(string Token, User Teacher)> RegisterTeacherAsync()
    {
        var token = await GetTeacherToken();
        return (token, await GetUserByTokenAsync(token));
    }

    // Reads the user id from the JWT payload
    private async Task<User> GetUserByTokenAsync(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
        var id = json.RootElement.EnumerateObject()
            .Where(p => p.Name is "sub" or "nameid" || p.Name.EndsWith("/nameidentifier", StringComparison.Ordinal))
            .Select(p => Guid.TryParse(p.Value.GetString(), out var g) ? g : Guid.Empty)
            .First(g => g != Guid.Empty);

        await using var db = NewDbContext();
        return await db.Users.SingleAsync(u => u.Id == id);
    }

    private async Task EnrollAsync(string studentToken, Guid courseId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/enrollments/courses/{courseId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> SeedCourseAsync(
        User teacher,
        CourseStatus status,
        int blocks = 0,
        DateTimeOffset? publishedAt = null,
        bool withQuiz = false)
    {
        await using var db = NewDbContext();
        db.Attach(teacher);

        var course = TestDataBuilder.CreateValidCourse(teacher, status);
        course.PublishedAt = status == CourseStatus.Published ? publishedAt ?? DateTimeOffset.UtcNow : null;
        var chapter = TestDataBuilder.CreateValidChapter(course);
        var subchapter = TestDataBuilder.CreateValidSubchapter(chapter);
        db.Courses.Add(course);
        db.Chapters.Add(chapter);
        db.Subchapters.Add(subchapter);

        for (var i = 1; i <= blocks; i++)
        {
            db.CourseBlocks.Add(new CourseBlock
            {
                Title = $"Block {i}",
                Type = BlockType.Theory,
                OrderIndex = i,
                Subchapter = subchapter,
                TheoryContent = new TheoryContent { Content = SecretTheoryContent, Block = null! }
            });
        }

        if (withQuiz)
        {
            var quiz = new Quiz { Block = null! };
            quiz.Questions.Add(new QuizQuestion
            {
                Content = "Q", Type = QuestionType.SingleChoice, OrderIndex = 1, Quiz = quiz,
                Answers = { new QuizAnswer { Text = "A", IsCorrect = true, OrderIndex = 1, Question = null! } }
            });
            db.CourseBlocks.Add(new CourseBlock
            {
                Title = "Quiz", Type = BlockType.Quiz, OrderIndex = blocks + 1, Subchapter = subchapter, Quiz = quiz
            });
        }

        await db.SaveChangesAsync();
        return course.Id;
    }

    private async Task<Guid> SeedCourseWithProblemAsync(
        User teacher,
        bool withTestCase,
        bool withStarterCode,
        bool disableLanguage = false)
    {
        var courseId = await SeedCourseAsync(teacher, CourseStatus.Draft);

        await using var db = NewDbContext();
        db.Attach(teacher);
        var subchapter = await db.Subchapters.SingleAsync(s => s.Chapter.CourseId == courseId);
        var problem = TestDataBuilder.CreateValidProblem(teacher);
        db.Problems.Add(problem);

        if (withTestCase)
        {
            db.TestCases.Add(TestDataBuilder.CreateValidTestCase(problem));
        }

        if (withStarterCode)
        {
            db.StarterCodes.Add(new StarterCode
            {
                Code = "print()", LanguageId = PythonLanguageId, Problem = problem, Language = null!
            });
        }

        if (disableLanguage)
        {
            await db.Languages.Where(l => l.Id == PythonLanguageId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsEnabled, false));
        }

        db.CourseBlocks.Add(new CourseBlock
        {
            Title = "Problem", Type = BlockType.Problem, OrderIndex = 1, Subchapter = subchapter, Problem = problem
        });

        await db.SaveChangesAsync();
        return courseId;
    }

    #endregion
}
