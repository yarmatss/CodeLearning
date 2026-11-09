using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Constants;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CodeLearning.Tests.Integration.Controllers;

public class ProblemsControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JavaScriptLanguageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid JavaLanguageId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public ProblemsControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    #region Helper Methods

    private async Task<(string Token, Guid UserId)> RegisterAsync(string role)
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: role);
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var token = response.GetAccessToken();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await db.Users.Where(u => u.Email == registerDto.Email).Select(u => u.Id).SingleAsync();

        return (token, userId);
    }

    private async Task<string> GetTeacherToken() => (await RegisterAsync("Teacher")).Token;

    private async Task<string> GetStudentToken() => (await RegisterAsync("Student")).Token;

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // A valid problem always carries at least one starter code
    private static CreateProblemDto NewProblemDto(string? title = null, string difficulty = "Easy")
    {
        var dto = TestDataBuilder.CreateValidCreateProblemDto(title, difficulty);
        dto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(PythonLanguageId));
        return dto;
    }

    private async Task<ProblemResponseDto> CreateProblemWithCurrentToken(CreateProblemDto? dto = null)
    {
        var response = await _client.PostAsJsonAsync("/api/problems", dto ?? NewProblemDto());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<ProblemResponseDto>()
            ?? throw new Exception("Failed to create problem");
    }

    private async Task<ProblemResponseDto> CreateProblemAsTeacher(string? title = null)
    {
        UseToken(await GetTeacherToken());
        return await CreateProblemWithCurrentToken(NewProblemDto(title));
    }

    // Adds a course by the problem's author that uses the problem, optionally enrolling a student
    private async Task<Guid> AddCourseUsingProblemAsync(Guid problemId, CourseStatus status, Guid? enrolledStudentId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var problem = await db.Problems.Include(p => p.Author).SingleAsync(p => p.Id == problemId);
        var course = TestDataBuilder.CreateValidCourse(problem.Author, status);
        var chapter = TestDataBuilder.CreateValidChapter(course);
        var subchapter = TestDataBuilder.CreateValidSubchapter(chapter);

        db.Courses.Add(course);
        db.Chapters.Add(chapter);
        db.Subchapters.Add(subchapter);
        db.CourseBlocks.Add(new CourseBlock
        {
            Title = problem.Title,
            Type = BlockType.Problem,
            OrderIndex = 1,
            Subchapter = subchapter,
            ProblemId = problemId
        });

        if (enrolledStudentId is { } studentId)
        {
            db.StudentCourseProgresses.Add(new StudentCourseProgress
            {
                StudentId = studentId,
                Student = await db.Users.SingleAsync(u => u.Id == studentId),
                CourseId = course.Id,
                Course = course,
                EnrolledAt = DateTimeOffset.UtcNow,
                LastActivityAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return course.Id;
    }

    private async Task<PagedResult<ProblemListDto>> GetPageAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<PagedResult<ProblemListDto>>()
            ?? throw new Exception("Empty problem page");
    }

    #endregion

    #region Create Problem Tests

    [Fact]
    public async Task CreateProblem_AsTeacher_WithValidData_ShouldReturn201()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto("Two Sum");

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content.Should().NotBeNull();
        content!.Title.Should().Be("Two Sum");
        content.Difficulty.Should().Be("Easy");
        content.TestCases.Should().HaveCount(2);
        content.TestCases.Should().Contain(tc => tc.IsPublic);
        content.TestCases.Select(tc => tc.OrderIndex).Should().Equal(1, 2);
        content.IsUsedByPublishedCourse.Should().BeFalse();
    }

    [Fact]
    public async Task CreateProblem_WithStarterCodes_ShouldIncludeThem()
    {
        // Arrange
        UseToken(await GetTeacherToken());

        var createDto = NewProblemDto("Problem with Starter");
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId));

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content!.StarterCodes.Select(sc => sc.LanguageId)
            .Should().BeEquivalentTo([PythonLanguageId, JavaScriptLanguageId]);
    }

    [Fact]
    public async Task CreateProblem_WithoutStarterCodes_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = TestDataBuilder.CreateValidCreateProblemDto();

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithUnknownLanguage_ShouldReturn400AndNotCreate()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var unknownLanguageId = Guid.NewGuid();
        var createDto = NewProblemDto("Unknown language problem");
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(unknownLanguageId));

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert - the unknown language is reported, not silently dropped
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        error!.Detail.Should().Contain(unknownLanguageId.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Problems.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CreateProblem_WithDuplicateLanguages_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(PythonLanguageId));

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithDisabledLanguage_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        await SetLanguageEnabledAsync(JavaScriptLanguageId, false);

        try
        {
            var createDto = NewProblemDto();
            createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId));

            // Act
            var response = await _client.PostAsJsonAsync("/api/problems", createDto);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            error!.Detail.Should().Contain("JavaScript");
        }
        finally
        {
            await SetLanguageEnabledAsync(JavaScriptLanguageId, true);
        }
    }

    [Fact]
    public async Task CreateProblem_WithEmptyExpectedOutput_ShouldReturn201()
    {
        // Arrange - a program that prints nothing is a valid test
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.TestCases.Add(new CreateTestCaseDto { Input = "0", ExpectedOutput = "", IsPublic = false });

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateProblem_WithNulCharacterInDescription_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.Description += "\0";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert - rejected by validation instead of failing in PostgreSQL with a 500
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_ExceedingSandboxTimeBudget_ShouldReturn400()
    {
        // Arrange - Java: 300 s cap - 30 s compile - 26 s overhead leaves 244 s, so 25 tests of 10 s do not fit
        UseToken(await GetTeacherToken());
        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        createDto.TestCases = Enumerable.Range(0, 25)
            .Select(i => new CreateTestCaseDto { Input = $"{i}", ExpectedOutput = $"{i}", IsPublic = true })
            .ToList();
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaLanguageId));

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        error!.Detail.Should().Contain("at most 24 test cases");
    }

    [Fact]
    public async Task CreateProblem_AsStudent_ShouldReturn403()
    {
        // Arrange
        UseToken(await GetStudentToken());

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", NewProblemDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateProblem_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        using var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.PostAsJsonAsync("/api/problems", NewProblemDto());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProblem_WithEmptyTitle_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.Title = "";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithShortDescription_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.Description = "Too short";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithInvalidDifficulty_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.Difficulty = "Invalid";

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithNoTestCases_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.TestCases.Clear();

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProblem_WithNoPublicTestCases_ShouldReturn400()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        foreach (var testCase in createDto.TestCases)
        {
            testCase.IsPublic = false;
        }

        // Act
        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SetLanguageEnabledAsync(Guid languageId, bool enabled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Languages
            .Where(l => l.Id == languageId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsEnabled, enabled));
    }

    #endregion

    #region Get Problem Tests

    [Fact]
    public async Task GetProblemById_AsAuthor_ShouldReturn200WithHiddenTestCases()
    {
        // Arrange - CreateProblemAsTeacher leaves the author's token on the client
        var problem = await CreateProblemAsTeacher("Get By ID Test");

        // Act
        var response = await _client.GetAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content!.Id.Should().Be(problem.Id);
        content.Title.Should().Be("Get By ID Test");
        content.TestCases.Should().HaveCount(2);
        content.TestCases.Should().Contain(tc => !tc.IsPublic);
    }

    [Fact]
    public async Task GetProblemById_AsAnotherTeacher_ShouldReturn403()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.GetAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetProblemById_AsNotEnrolledStudent_ShouldReturn403()
    {
        // Arrange - the problem is in a published course, but the student is not enrolled
        var problem = await CreateProblemAsTeacher();
        await AddCourseUsingProblemAsync(problem.Id, CourseStatus.Published);
        UseToken(await GetStudentToken());

        // Act
        var response = await _client.GetAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetProblemById_AsEnrolledStudent_ShouldReturnOnlyPublicTestCases()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();
        var (studentToken, studentId) = await RegisterAsync("Student");
        await AddCourseUsingProblemAsync(problem.Id, CourseStatus.Published, studentId);
        UseToken(studentToken);

        // Act
        var response = await _client.GetAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content!.TestCases.Should().ContainSingle().Which.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task GetProblemById_EnrolledInDraftCourse_ShouldReturn403()
    {
        // Arrange - an unpublished course keeps its enrollments but hides its content
        var problem = await CreateProblemAsTeacher();
        var (studentToken, studentId) = await RegisterAsync("Student");
        await AddCourseUsingProblemAsync(problem.Id, CourseStatus.Draft, studentId);
        UseToken(studentToken);

        // Act
        var response = await _client.GetAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetProblemById_NonExistingProblem_ShouldReturn404()
    {
        // Arrange
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.GetAsync($"/api/problems/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProblemById_ShouldOrderTestCasesByOrderIndex()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();
        var reversed = problem.TestCases.Select(tc => tc.Id).Reverse().ToList();
        await _client.PutAsJsonAsync($"/api/problems/{problem.Id}/testcases/reorder",
            new ReorderTestCasesDto { TestCaseIds = reversed });

        // Act
        var content = await _client.GetFromJsonAsync<ProblemResponseDto>($"/api/problems/{problem.Id}");

        // Assert
        content!.TestCases.Select(tc => tc.Id).Should().Equal(reversed);
        content.TestCases.Select(tc => tc.OrderIndex).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetProblems_ShouldReturnOwnProblemsAsPage()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto("Problem 1");
        createDto.Description = new string('d', 500);
        await CreateProblemWithCurrentToken(createDto);
        await CreateProblemWithCurrentToken(NewProblemDto("Problem 2"));

        // Act
        var page = await GetPageAsync("/api/problems");

        // Assert
        page.TotalCount.Should().Be(2);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(ProblemListQuery.DefaultPageSize);
        page.TotalPages.Should().Be(1);
        page.Items.Should().HaveCount(2);

        var longOne = page.Items.Single(p => p.Title == "Problem 1");
        longOne.TestCasesCount.Should().Be(2);
        longOne.LanguagesCount.Should().Be(1);
        longOne.Description.Should().Be(new string('d', ProblemListDto.DescriptionPreviewLength) + "...");
    }

    [Fact]
    public async Task GetProblems_ShouldHideProblemsTheCallerCannotSee()
    {
        // Arrange - another teacher's problem, one in a published course the student is enrolled in,
        // and one in a published course the student is not enrolled in
        var otherTeacherProblem = await CreateProblemAsTeacher("Other teacher problem");
        var enrolledProblem = await CreateProblemAsTeacher("Enrolled problem");
        var notEnrolledProblem = await CreateProblemAsTeacher("Not enrolled problem");

        var (studentToken, studentId) = await RegisterAsync("Student");
        await AddCourseUsingProblemAsync(enrolledProblem.Id, CourseStatus.Published, studentId);
        await AddCourseUsingProblemAsync(notEnrolledProblem.Id, CourseStatus.Published);

        UseToken(studentToken);

        // Act
        var page = await GetPageAsync("/api/problems");

        // Assert
        page.Items.Select(p => p.Id).Should().Equal(enrolledProblem.Id);
        page.Items.Single().IsUsedByPublishedCourse.Should().BeTrue();
        page.Items.Should().NotContain(p => p.Id == otherTeacherProblem.Id || p.Id == notEnrolledProblem.Id);
    }

    [Fact]
    public async Task GetProblems_WithPaging_ShouldReturnRequestedPage()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        for (var i = 0; i < 3; i++)
        {
            await CreateProblemWithCurrentToken(NewProblemDto($"Paged problem {i}"));
        }

        // Act
        var first = await GetPageAsync("/api/problems?page=1&pageSize=2");
        var second = await GetPageAsync("/api/problems?page=2&pageSize=2");

        // Assert
        first.Items.Should().HaveCount(2);
        second.Items.Should().HaveCount(1);
        first.TotalCount.Should().Be(3);
        first.TotalPages.Should().Be(2);
        first.Items.Select(p => p.Id).Should().NotIntersectWith(second.Items.Select(p => p.Id));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("difficulty=Impossible")]
    public async Task GetProblems_WithInvalidQuery_ShouldReturn400(string query)
    {
        // Arrange
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.GetAsync($"/api/problems?{query}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetProblems_WithDifficultyFilter_ShouldReturnFiltered()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        await CreateProblemWithCurrentToken(NewProblemDto("Easy Problem"));
        await CreateProblemWithCurrentToken(NewProblemDto("Hard Problem", "Hard"));

        // Act
        var page = await GetPageAsync("/api/problems?difficulty=Easy");

        // Assert
        page.Items.Should().ContainSingle().Which.Title.Should().Be("Easy Problem");
    }

    [Fact]
    public async Task GetProblems_WithSearchFilter_ShouldReturnMatching()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        await CreateProblemWithCurrentToken(NewProblemDto("Array Sum Problem"));
        await CreateProblemWithCurrentToken(NewProblemDto("String Reverse Problem"));

        // Act
        var page = await GetPageAsync("/api/problems?search=Array");

        // Assert
        page.Items.Should().ContainSingle().Which.Title.Should().Be("Array Sum Problem");
    }

    [Fact]
    public async Task GetMyProblems_AsTeacher_ShouldReturnOnlyMyProblems()
    {
        // Arrange
        await CreateProblemAsTeacher("Another Problem");

        UseToken(await GetTeacherToken());
        await CreateProblemWithCurrentToken(NewProblemDto("My Problem"));

        // Act
        var page = await GetPageAsync("/api/problems/my");

        // Assert
        page.Items.Should().ContainSingle().Which.Title.Should().Be("My Problem");
        page.TotalCount.Should().Be(1);
    }

    #endregion

    #region Update and Delete Problem Tests

    [Fact]
    public async Task UpdateProblem_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Original Title");
        var updateDto = TestDataBuilder.CreateValidUpdateProblemDto("Updated Title", "Hard");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<ProblemResponseDto>();
        content!.Title.Should().Be("Updated Title");
        content.Difficulty.Should().Be("Hard");
    }

    [Fact]
    public async Task UpdateProblem_AsNonOwner_ShouldReturn403()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Original Problem");
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}",
            TestDataBuilder.CreateValidUpdateProblemDto("Hacked Title"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteProblem_AsOwner_WithNoSubmissions_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem to Delete");

        // Act
        var response = await _client.DeleteAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/problems/{problem.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProblem_AsNonOwner_ShouldReturn403()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem to Delete");
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.DeleteAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteProblem_UsedByDraftCourse_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();
        await AddCourseUsingProblemAsync(problem.Id, CourseStatus.Draft);

        // Act
        var response = await _client.DeleteAsync($"/api/problems/{problem.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Published Lock Tests

    public static TheoryData<string> LockedMutations =>
    [
        "update-problem",
        "delete-problem",
        "add-testcase",
        "bulk-add-testcases",
        "update-testcase",
        "delete-testcase",
        "reorder-testcases",
        "add-startercode",
        "delete-startercode"
    ];

    [Theory]
    [MemberData(nameof(LockedMutations))]
    public async Task Mutation_OnProblemUsedByPublishedCourse_ShouldReturn409(string mutation)
    {
        // Arrange - give the problem a second starter code so deleting one would otherwise be allowed
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId));
        var problem = await CreateProblemWithCurrentToken(createDto);
        await AddCourseUsingProblemAsync(problem.Id, CourseStatus.Published);

        // Act
        var response = await SendMutationAsync(mutation, problem);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        error!.Detail.Should().Contain("published course");

        var stored = await _client.GetFromJsonAsync<ProblemResponseDto>($"/api/problems/{problem.Id}");
        stored!.IsUsedByPublishedCourse.Should().BeTrue();
        stored.Title.Should().Be(problem.Title);
        stored.TestCases.Select(tc => tc.Id).Should().Equal(problem.TestCases.Select(tc => tc.Id));
        stored.StarterCodes.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(LockedMutations))]
    public async Task Mutation_AsNonOwner_ShouldReturn403(string mutation)
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId));
        var problem = await CreateProblemWithCurrentToken(createDto);

        UseToken(await GetTeacherToken());

        // Act
        var response = await SendMutationAsync(mutation, problem);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<HttpResponseMessage> SendMutationAsync(string mutation, ProblemResponseDto problem)
    {
        var testCase = problem.TestCases.First(tc => !tc.IsPublic);
        var starterCode = problem.StarterCodes.First();
        var newTestCase = new CreateTestCaseDto { Input = "7", ExpectedOutput = "7", IsPublic = true };

        return mutation switch
        {
            "update-problem" => _client.PutAsJsonAsync($"/api/problems/{problem.Id}",
                TestDataBuilder.CreateValidUpdateProblemDto("Changed title")),
            "delete-problem" => _client.DeleteAsync($"/api/problems/{problem.Id}"),
            "add-testcase" => _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases", newTestCase),
            "bulk-add-testcases" => _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases/bulk",
                new BulkAddTestCasesDto { TestCases = [newTestCase] }),
            "update-testcase" => _client.PutAsJsonAsync($"/api/problems/testcases/{testCase.Id}",
                new UpdateTestCaseDto { Input = "changed", ExpectedOutput = "changed", IsPublic = false }),
            "delete-testcase" => _client.DeleteAsync($"/api/problems/testcases/{testCase.Id}"),
            "reorder-testcases" => _client.PutAsJsonAsync($"/api/problems/{problem.Id}/testcases/reorder",
                new ReorderTestCasesDto { TestCaseIds = problem.TestCases.Select(tc => tc.Id).Reverse().ToList() }),
            "add-startercode" => _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes",
                TestDataBuilder.CreateValidCreateStarterCodeDto(JavaLanguageId)),
            "delete-startercode" => _client.DeleteAsync($"/api/problems/startercodes/{starterCode.Id}"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
    }

    [Fact]
    public async Task Mutation_OnProblemUsedOnlyByDraftCourse_ShouldSucceed()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();
        await AddCourseUsingProblemAsync(problem.Id, CourseStatus.Draft);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}",
            TestDataBuilder.CreateValidUpdateProblemDto("Still editable"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Test Case Management Tests

    [Fact]
    public async Task AddTestCase_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for TestCase");
        var testCaseDto = TestDataBuilder.CreateValidCreateTestCaseDto(isPublic: true);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases", testCaseDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<TestCaseResponseDto>();
        content!.Input.Should().Be(testCaseDto.Input);
        content.ExpectedOutput.Should().Be(testCaseDto.ExpectedOutput);
        content.OrderIndex.Should().Be(3);
    }

    [Fact]
    public async Task AddTestCase_WithTooLongInput_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for oversized TestCase");

        var testCaseDto = new CreateTestCaseDto
        {
            Input = new string('1', TestCaseLimits.MaxInputLength + 1),
            ExpectedOutput = "1",
            IsPublic = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases", testCaseDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkAddTestCases_ExceedingProblemLimit_ShouldReturn400()
    {
        // Arrange - the problem already has 2 test cases, so filling up to the limit overflows it
        var problem = await CreateProblemAsTeacher("Problem at the test case limit");

        var bulkDto = new BulkAddTestCasesDto
        {
            TestCases = Enumerable.Range(0, TestCaseLimits.MaxTestCasesPerProblem)
                .Select(i => new CreateTestCaseDto { Input = $"{i}", ExpectedOutput = $"{i}", IsPublic = true })
                .ToList()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases/bulk", bulkDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain($"at most {TestCaseLimits.MaxTestCasesPerProblem} test cases");
    }

    [Fact]
    public async Task BulkAddTestCases_ExceedingSandboxTimeBudget_ShouldReturn400()
    {
        // Arrange - a Java problem fits at most 24 tests in the 300 s sandbox cap
        UseToken(await GetTeacherToken());
        var createDto = TestDataBuilder.CreateValidCreateProblemDto();
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaLanguageId));
        var problem = await CreateProblemWithCurrentToken(createDto);

        var bulkDto = new BulkAddTestCasesDto
        {
            TestCases = Enumerable.Range(0, 26)
                .Select(i => new CreateTestCaseDto { Input = $"{i}", ExpectedOutput = $"{i}", IsPublic = true })
                .ToList()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases/bulk", bulkDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        error!.Detail.Should().Contain("Java").And.Contain("at most 24 test cases");
    }

    [Fact]
    public async Task BulkAddTestCases_WithMultipleTestCases_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Bulk Add");

        var bulkDto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto { Input = "10 20", ExpectedOutput = "30", IsPublic = true },
                new CreateTestCaseDto { Input = "5 15", ExpectedOutput = "20", IsPublic = false },
                new CreateTestCaseDto { Input = "100 200", ExpectedOutput = "300", IsPublic = true }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases/bulk", bulkDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<BulkAddTestCasesResult>();
        content!.Added.Should().Be(3);
        content.TestCases.Should().HaveCount(3);
        content.TestCases.Select(tc => tc.OrderIndex).Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task BulkAddTestCases_WithNoPublicTests_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Bulk Validation");

        var bulkDto = new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto { Input = "1", ExpectedOutput = "1", IsPublic = false },
                new CreateTestCaseDto { Input = "2", ExpectedOutput = "2", IsPublic = false }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases/bulk", bulkDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkAddTestCases_WithNullElement_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();
        var body = JsonContent.Create(new { testCases = new object?[] { new { input = "1", expectedOutput = "1", isPublic = true }, null } });

        // Act
        var response = await _client.PostAsync($"/api/problems/{problem.Id}/testcases/bulk", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTestCase_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Update");
        var testCase = problem.TestCases.First();

        var updateDto = new UpdateTestCaseDto { Input = "99 1", ExpectedOutput = "100", IsPublic = true };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/testcases/{testCase.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<TestCaseResponseDto>();
        content!.Input.Should().Be("99 1");
        content.ExpectedOutput.Should().Be("100");
    }

    [Fact]
    public async Task UpdateTestCase_MakingLastPublicPrivate_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Last Public");
        var publicTestCase = problem.TestCases.Single(tc => tc.IsPublic);

        var updateDto = new UpdateTestCaseDto
        {
            Input = publicTestCase.Input,
            ExpectedOutput = publicTestCase.ExpectedOutput,
            IsPublic = false
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/testcases/{publicTestCase.Id}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReorderTestCases_WithValidOrder_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Reorder");
        var testCaseIds = problem.TestCases.Select(tc => tc.Id).Reverse().ToList();

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}/testcases/reorder",
            new ReorderTestCasesDto { TestCaseIds = testCaseIds });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedProblem = await _client.GetFromJsonAsync<ProblemResponseDto>($"/api/problems/{problem.Id}");
        updatedProblem!.TestCases.Select(tc => tc.Id).Should().Equal(testCaseIds);
    }

    [Fact]
    public async Task ReorderTestCases_WithIncompleteList_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Incomplete Reorder");

        // Act
        var response = await _client.PutAsJsonAsync($"/api/problems/{problem.Id}/testcases/reorder",
            new ReorderTestCasesDto { TestCaseIds = [problem.TestCases.First().Id] });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteTestCase_ShouldReindexRemainingTestCases()
    {
        // Arrange - test cases 1..4, delete the second
        var problem = await CreateProblemAsTeacher();
        await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/testcases/bulk", new BulkAddTestCasesDto
        {
            TestCases =
            [
                new CreateTestCaseDto { Input = "3", ExpectedOutput = "3", IsPublic = true },
                new CreateTestCaseDto { Input = "4", ExpectedOutput = "4", IsPublic = true }
            ]
        });
        var before = await _client.GetFromJsonAsync<ProblemResponseDto>($"/api/problems/{problem.Id}");
        var toDelete = before!.TestCases.Single(tc => tc.OrderIndex == 2);

        // Act
        var response = await _client.DeleteAsync($"/api/problems/testcases/{toDelete.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await _client.GetFromJsonAsync<ProblemResponseDto>($"/api/problems/{problem.Id}");
        after!.TestCases.Select(tc => tc.OrderIndex).Should().Equal(1, 2, 3);
        after.TestCases.Select(tc => tc.Id).Should()
            .Equal(before.TestCases.Where(tc => tc.Id != toDelete.Id).Select(tc => tc.Id));
    }

    [Fact]
    public async Task DeleteTestCase_NonExisting_ShouldReturn404()
    {
        // Arrange
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.DeleteAsync($"/api/problems/testcases/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTestCase_WithSubmissionResults_ShouldReturn200AndKeepSubmission()
    {
        // Arrange: the author tests their own problem, which records a result for every test case
        var problem = await CreateProblemAsTeacher("Problem with graded submission");
        var hiddenTestCase = problem.TestCases.First(tc => !tc.IsPublic);

        Guid submissionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storedProblem = await dbContext.Problems.Include(p => p.TestCases).SingleAsync(p => p.Id == problem.Id);

            var submission = new Submission
            {
                ProblemId = storedProblem.Id,
                Problem = storedProblem,
                StudentId = storedProblem.AuthorId,
                Student = await dbContext.Users.SingleAsync(u => u.Id == storedProblem.AuthorId),
                LanguageId = PythonLanguageId,
                Language = await dbContext.Languages.SingleAsync(l => l.Id == PythonLanguageId),
                Code = "print(1)",
                Status = SubmissionStatus.Completed,
                Score = 50
            };

            foreach (var testCase in storedProblem.TestCases)
            {
                submission.TestResults.Add(new SubmissionTestResult
                {
                    Submission = submission,
                    TestCaseId = testCase.Id,
                    TestCase = testCase,
                    Status = TestResultStatus.Passed
                });
            }

            dbContext.Submissions.Add(submission);
            await dbContext.SaveChangesAsync();
            submissionId = submission.Id;
        }

        // Act
        var response = await _client.DeleteAsync($"/api/problems/testcases/{hiddenTestCase.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var submission = await dbContext.Submissions
                .Include(s => s.TestResults)
                .SingleAsync(s => s.Id == submissionId);

            submission.Score.Should().Be(50);
            submission.TestResults.Should().ContainSingle()
                .Which.TestCaseId.Should().NotBe(hiddenTestCase.Id);
        }
    }

    #endregion

    #region Starter Code Management Tests

    [Fact]
    public async Task AddStarterCode_AsOwner_ShouldReturn200()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for Starter Code");
        var starterCodeDto = TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes", starterCodeDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<StarterCodeResponseDto>();
        content!.Code.Should().Be(starterCodeDto.Code);
        content.LanguageId.Should().Be(JavaScriptLanguageId);
        content.LanguageName.Should().Be("JavaScript");
    }

    [Fact]
    public async Task AddStarterCode_DuplicateLanguage_ShouldReturn409()
    {
        // Arrange - the problem already has a Python starter code
        var problem = await CreateProblemAsTeacher("Problem for Duplicate Starter");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes",
            TestDataBuilder.CreateValidCreateStarterCodeDto(PythonLanguageId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddStarterCode_ConcurrentDuplicates_ShouldNeverReturn500()
    {
        // Arrange - racing requests may both pass the existence check; the unique index decides
        var problem = await CreateProblemAsTeacher();
        var dto = TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId);

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes", dto)));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddStarterCode_UnknownLanguage_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes",
            TestDataBuilder.CreateValidCreateStarterCodeDto(Guid.NewGuid()));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddStarterCode_ExceedingSandboxTimeBudget_ShouldReturn400()
    {
        // Arrange - 30 Python tests fit, but Java fits at most 24 tests in the sandbox budget
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.TestCases = Enumerable.Range(0, 30)
            .Select(i => new CreateTestCaseDto { Input = $"{i}", ExpectedOutput = $"{i}", IsPublic = true })
            .ToList();
        var problem = await CreateProblemWithCurrentToken(createDto);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes",
            TestDataBuilder.CreateValidCreateStarterCodeDto(JavaLanguageId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddStarterCode_WithTooLongCode_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher("Problem for oversized Starter Code");

        var starterCodeDto = new CreateStarterCodeDto
        {
            LanguageId = JavaScriptLanguageId,
            Code = new string('x', 100_001)
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/problems/{problem.Id}/startercodes", starterCodeDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteStarterCode_LastOne_ShouldReturn400()
    {
        // Arrange
        var problem = await CreateProblemAsTeacher();

        // Act
        var response = await _client.DeleteAsync($"/api/problems/startercodes/{problem.StarterCodes.Single().Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteStarterCode_WithAnotherRemaining_ShouldReturn200()
    {
        // Arrange
        UseToken(await GetTeacherToken());
        var createDto = NewProblemDto();
        createDto.StarterCodes.Add(TestDataBuilder.CreateValidCreateStarterCodeDto(JavaScriptLanguageId));
        var problem = await CreateProblemWithCurrentToken(createDto);

        // Act
        var response = await _client.DeleteAsync($"/api/problems/startercodes/{problem.StarterCodes.First().Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteStarterCode_NonExisting_ShouldReturn404()
    {
        // Arrange
        UseToken(await GetTeacherToken());

        // Act
        var response = await _client.DeleteAsync($"/api/problems/startercodes/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
