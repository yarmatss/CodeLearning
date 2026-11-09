using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.DTOs.Submission;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;


namespace CodeLearning.Tests.Integration.Controllers;

public class SubmissionsControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private readonly Guid _pythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _javascriptLanguageId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public SubmissionsControllerTests(IntegrationTestWebAppFactory factory)
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

    private async Task<ProblemResponseDto> CreateProblem(string token, string? title = null)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createDto = TestDataBuilder.CreateValidCreateProblemDto(title);
        createDto.StarterCodes.Add(new CreateStarterCodeDto
        {
            LanguageId = _pythonLanguageId,
            Code = "def solution():\n    pass"
        });
        createDto.StarterCodes.Add(new CreateStarterCodeDto
        {
            LanguageId = _javascriptLanguageId,
            Code = "function solution() {\n}"
        });

        var response = await _client.PostAsJsonAsync("/api/problems", createDto);

        return await response.Content.ReadFromJsonAsync<ProblemResponseDto>()
            ?? throw new Exception("Failed to create problem");
    }

    // Students may only submit to problems used in a course they are enrolled in
    private async Task EnrollInCourseWithProblem(ProblemResponseDto problem, string studentToken, CourseStatus status = CourseStatus.Published)
    {
        Guid courseId;

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var instructor = await dbContext.Users.FirstAsync(u => u.Id == problem.AuthorId);

            var course = new Course
            {
                Title = "Course with problem",
                Description = "Course used by submission tests",
                Status = CourseStatus.Published,
                PublishedAt = DateTimeOffset.UtcNow,
                InstructorId = instructor.Id,
                Instructor = instructor
            };

            var chapter = new Chapter { Title = "Chapter 1", OrderIndex = 1, Course = course };
            var subchapter = new Subchapter { Title = "Subchapter 1.1", OrderIndex = 1, Chapter = chapter };
            var block = new CourseBlock
            {
                Title = problem.Title,
                Type = BlockType.Problem,
                OrderIndex = 1,
                Subchapter = subchapter,
                ProblemId = problem.Id
            };

            dbContext.Courses.Add(course);
            dbContext.Chapters.Add(chapter);
            dbContext.Subchapters.Add(subchapter);
            dbContext.CourseBlocks.Add(block);
            await dbContext.SaveChangesAsync();

            courseId = course.Id;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);
        var response = await _client.PostAsync($"/api/enrollments/courses/{courseId}", null);
        response.EnsureSuccessStatusCode();

        if (status != CourseStatus.Published)
        {
            // Unpublishing keeps the enrollment but hides the course content
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Courses
                .Where(c => c.Id == courseId)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Status, status));
        }
    }

    #endregion

    #region Submit Code Tests

    [Fact]
    public async Task SubmitCode_ValidPythonCode_ShouldReturn202Accepted()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken, "Two Sum");

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = @"
def solution():
    a, b = map(int, input().split())
    print(a + b)
"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var submission = await response.Content.ReadFromJsonAsync<SubmissionResponseDto>();
        Assert.NotNull(submission);
        Assert.NotEqual(Guid.Empty, submission.Id);
        Assert.Equal(problem.Id, submission.ProblemId);
        Assert.Equal(_pythonLanguageId, submission.LanguageId);
        Assert.Equal(SubmissionStatus.Pending, submission.Status);
        Assert.Equal(0, submission.Score);
    }

    [Fact]
    public async Task SubmitCode_ValidSubmission_ShouldPublishSubmissionCreated()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);
        var submission = await response.Content.ReadFromJsonAsync<SubmissionResponseDto>();

        // Assert - delivered from the outbox once the submission is saved
        var harness = _factory.Services.GetTestHarness();
        var published = await harness.Published.Any<SubmissionCreated>(
            m => m.Context.Message.SubmissionId == submission!.Id);

        Assert.True(published);
    }

    [Fact]
    public async Task SubmitCode_ValidJavaScriptCode_ShouldReturn202Accepted()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken, "Add Numbers");

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _javascriptLanguageId,
            Code = @"
function solution() {
    const input = readInput();
    const [a, b] = input.split(' ').map(Number);
    console.log(a + b);
}
"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var submission = await response.Content.ReadFromJsonAsync<SubmissionResponseDto>();
        Assert.NotNull(submission);
        Assert.Equal(_javascriptLanguageId, submission.LanguageId);
    }

    [Fact]
    public async Task SubmitCode_WithoutAuthentication_ShouldReturn401()
    {
        // Arrange
        var submitDto = new SubmitCodeDto
        {
            ProblemId = Guid.NewGuid(),
            LanguageId = _pythonLanguageId,
            Code = "def solution(): pass"
        };

        using var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCode_NonExistingProblem_ShouldReturn404()
    {
        // Arrange
        var studentToken = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = Guid.NewGuid(),
            LanguageId = _pythonLanguageId,
            Code = "def solution(): pass"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(error);
        Assert.Contains("not found", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitCode_InvalidLanguageId_ShouldReturn400()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = Guid.NewGuid(),
            Code = "def solution(): pass"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCode_EmptyCode_ShouldReturn400()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCode_NotEnrolledInCourseWithProblem_ShouldReturn403()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(error);
        Assert.Contains("enrolled", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitCode_EnrolledInUnpublishedCourse_ShouldReturn403()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken, CourseStatus.Draft);

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCode_UnsupportedLanguage_ShouldListLanguageNames()
    {
        // Arrange - the problem has Python and JavaScript starter codes only
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Code = "class Solution {}"
        });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("JavaScript", error!.Detail);
        Assert.Contains("Python", error.Detail);
        Assert.DoesNotContain(_pythonLanguageId.ToString(), error.Detail);
    }

    [Fact]
    public async Task SubmitCode_WithNulCharacters_ShouldStripThemAndReturn202()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)\0\0"
        });

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var submission = await response.Content.ReadFromJsonAsync<SubmissionResponseDto>();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.Submissions.SingleAsync(s => s.Id == submission!.Id);
        Assert.Equal("print(1)", stored.Code);
    }

    [Fact]
    public async Task SubmitCode_OnlyNulCharacters_ShouldReturn400()
    {
        // Arrange - the author may submit without a course
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "\0\0"
        });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCode_AsProblemAuthor_ShouldReturn202Accepted()
    {
        // Arrange - authors may test their own problems without a course
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCode_ExceedingRateLimit_ShouldReturn429()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var permitLimit = _factory.Services
            .GetRequiredService<IOptions<SubmissionRateLimitSettings>>().Value.PermitLimit;

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        };

        for (var i = 0; i < permitLimit; i++)
        {
            var accepted = await _client.PostAsJsonAsync("/api/submissions", submitDto);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        // Act
        var response = await _client.PostAsJsonAsync("/api/submissions", submitDto);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"), "429 response should tell the client when to retry");

        // Another user's limit is independent
        var otherStudentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, otherStudentToken);
        var otherResponse = await _client.PostAsJsonAsync("/api/submissions", submitDto);
        Assert.Equal(HttpStatusCode.Accepted, otherResponse.StatusCode);
    }

    #endregion

    #region Get Submission Tests

    [Fact]
    public async Task GetSubmission_ExistingSubmission_ShouldReturn200()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "def solution(): pass"
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/submissions", submitDto);
        var submission = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponseDto>();

        // Act
        var response = await _client.GetAsync($"/api/submissions/{submission!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SubmissionResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(submission.Id, result.Id);
        Assert.Equal(problem.Id, result.ProblemId);
    }

    [Fact]
    public async Task GetSubmission_NonExistingSubmission_ShouldReturn404()
    {
        // Arrange
        var studentToken = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        var nonExistingId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/submissions/{nonExistingId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSubmission_DifferentStudent_ShouldReturn404()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);

        // Student 1 submits
        var student1Token = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, student1Token);

        var submitDto = new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "def solution(): pass"
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/submissions", submitDto);
        var submission = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponseDto>();

        // Student 2 tries to access
        var student2Token = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", student2Token);

        // Act
        var response = await _client.GetAsync($"/api/submissions/{submission!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSubmission_WithoutAuthentication_ShouldReturn401()
    {
        // Arrange
        using var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.GetAsync($"/api/submissions/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Get My Submissions Tests

    [Fact]
    public async Task GetMySubmissions_MultipleSubmissions_ShouldReturnAll()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken, "Multiple Submissions Test");

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        // Submit multiple times
        var codes = new[]
        {
            "def solution(): print(1)",
            "def solution(): print(2)",
            "def solution(): print(3)"
        };

        foreach (var code in codes)
        {
            var submitDto = new SubmitCodeDto
            {
                ProblemId = problem.Id,
                LanguageId = _pythonLanguageId,
                Code = code
            };

            await _client.PostAsJsonAsync("/api/submissions", submitDto);
        }

        // Act
        var response = await _client.GetAsync($"/api/submissions/problem/{problem.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var submissions = await response.Content.ReadFromJsonAsync<List<SubmissionResponseDto>>();
        Assert.NotNull(submissions);
        Assert.Equal(3, submissions.Count);
        Assert.All(submissions, s => Assert.Equal(problem.Id, s.ProblemId));
    }

    [Fact]
    public async Task GetMySubmissions_GradedSubmission_ShouldReportTestCounts()
    {
        // Arrange
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken, "Graded Submission Test");

        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);

        var submitResponse = await _client.PostAsJsonAsync("/api/submissions", new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        });
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponseDto>();

        // Grade it as the runner would: only the first test case passes
        int testCaseCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var submission = await dbContext.Submissions.FirstAsync(sub => sub.Id == submitted!.Id);
            var testCases = await dbContext.TestCases
                .Where(tc => tc.ProblemId == problem.Id)
                .OrderBy(tc => tc.OrderIndex)
                .ToListAsync();

            testCaseCount = testCases.Count;
            submission.Status = SubmissionStatus.Completed;
            dbContext.SubmissionTestResults.AddRange(testCases.Select((tc, i) => new SubmissionTestResult
            {
                SubmissionId = submission.Id,
                TestCaseId = tc.Id,
                Status = i == 0 ? TestResultStatus.Passed : TestResultStatus.Failed,
                Submission = submission,
                TestCase = tc
            }));
            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/submissions/problem/{problem.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var submissions = await response.Content.ReadFromJsonAsync<List<SubmissionResponseDto>>();
        var listed = Assert.Single(submissions!);
        Assert.True(testCaseCount > 1);
        Assert.Equal(testCaseCount, listed.TotalTestCases);
        Assert.Equal(1, listed.PassedTestCases);
    }

    [Fact]
    public async Task GetMySubmissions_NoProblem_ShouldReturnEmptyList()
    {
        // Arrange
        var studentToken = await GetStudentToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentToken);

        // Act
        var response = await _client.GetAsync($"/api/submissions/problem/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var submissions = await response.Content.ReadFromJsonAsync<List<SubmissionResponseDto>>();
        Assert.NotNull(submissions);
        Assert.Empty(submissions);
    }

    [Fact]
    public async Task GetMySubmissions_WithoutAuthentication_ShouldReturn401()
    {
        // Arrange
        using var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.GetAsync($"/api/submissions/problem/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Hidden Test Result Tests

    // Seeds a graded submission with a passing public result and a failing hidden result whose
    // output carries a marker; neither the hidden test's data nor the marker may reach the student
    private async Task<(Guid SubmissionId, Guid HiddenTestCaseId, string HiddenInput)> SeedGradedSubmissionWithHiddenFailure(
        ProblemResponseDto problem, string marker)
    {
        var submitResponse = await _client.PostAsJsonAsync("/api/submissions", new SubmitCodeDto
        {
            ProblemId = problem.Id,
            LanguageId = _pythonLanguageId,
            Code = "print(1)"
        });
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponseDto>();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var submission = await dbContext.Submissions.SingleAsync(s => s.Id == submitted!.Id);
        var testCases = await dbContext.TestCases.Where(tc => tc.ProblemId == problem.Id).ToListAsync();
        var publicTest = testCases.Single(tc => tc.IsPublic);
        var hiddenTest = testCases.Single(tc => !tc.IsPublic);

        submission.Status = SubmissionStatus.Completed;
        submission.Score = 50;
        dbContext.SubmissionTestResults.AddRange(
            new SubmissionTestResult
            {
                Submission = submission,
                TestCase = publicTest,
                TestCaseId = publicTest.Id,
                Status = TestResultStatus.Passed,
                ActualOutput = publicTest.ExpectedOutput
            },
            new SubmissionTestResult
            {
                Submission = submission,
                TestCase = hiddenTest,
                TestCaseId = hiddenTest.Id,
                Status = TestResultStatus.Failed,
                ActualOutput = marker,
                ErrorMessage = marker
            });
        await dbContext.SaveChangesAsync();

        return (submission.Id, hiddenTest.Id, hiddenTest.Input);
    }

    [Fact]
    public async Task GetSubmission_WithHiddenFailingResult_ShouldNeverExposeIt()
    {
        // Arrange
        const string marker = "HIDDEN-RESULT-MARKER";
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);
        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);
        var (submissionId, hiddenTestCaseId, hiddenInput) = await SeedGradedSubmissionWithHiddenFailure(problem, marker);

        // Act
        var response = await _client.GetAsync($"/api/submissions/{submissionId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, body);
        Assert.DoesNotContain(hiddenTestCaseId.ToString(), body);
        Assert.DoesNotContain(hiddenInput, body);

        var result = await response.Content.ReadFromJsonAsync<SubmissionResponseDto>();
        var visible = Assert.Single(result!.TestResults!);
        Assert.True(visible.IsPublic);
        Assert.Equal(TestResultStatus.Passed, visible.Status);
    }

    [Fact]
    public async Task GetMySubmissions_WithHiddenFailingResult_ShouldNeverExposeIt()
    {
        // Arrange
        const string marker = "HIDDEN-LIST-MARKER";
        var teacherToken = await GetTeacherToken();
        var problem = await CreateProblem(teacherToken);
        var studentToken = await GetStudentToken();
        await EnrollInCourseWithProblem(problem, studentToken);
        var (_, hiddenTestCaseId, hiddenInput) = await SeedGradedSubmissionWithHiddenFailure(problem, marker);

        // Act
        var response = await _client.GetAsync($"/api/submissions/problem/{problem.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, body);
        Assert.DoesNotContain(hiddenTestCaseId.ToString(), body);
        Assert.DoesNotContain(hiddenInput, body);
    }

    #endregion
}
