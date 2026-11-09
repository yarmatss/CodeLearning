using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CodeLearning.Tests.Integration.Controllers;

public class BlocksControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private string _teacherToken = null!;
    private string _studentToken = null!;
    private Guid _subchapterId;

    public BlocksControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        // Setup: Create Teacher + Student tokens
        _teacherToken = await GetTeacherTokenAsync();
        _studentToken = await GetStudentTokenAsync();

        // Setup: Create Course ? Chapter ? Subchapter
        _subchapterId = await CreateSubchapterAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Theory Block Tests

    [Fact]
    public async Task CreateTheoryBlock_AsTeacher_ShouldReturn201()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateTheoryBlockDto
        {
            Title = "Introduction to C#",
            Content = "# Hello World\n\nThis is **Markdown** content."

        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        content.Should().NotBeNull();
        content!.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateTheoryBlock_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var dto = new CreateTheoryBlockDto
        {
            Title = "Test",
            Content = "Content"
        };

        // Act
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateTheoryBlock_AsStudent_ShouldReturn403()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var dto = new CreateTheoryBlockDto
        {
            Title = "Test",
            Content = "Content"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateTheoryBlock_WithEmptyContent_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateTheoryBlockDto
        {
            Title = "Test",
            Content = "" // Empty content
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Content is required");
    }

    [Fact]
    // Markdown is stored as written (sanitized when rendered), so code samples with angle brackets survive
    public async Task CreateTheoryBlock_WithCodeSample_ShouldStoreContentVerbatim()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        const string markdown = "Use `List<int> xs = new List<int>();`\n\n```cpp\n#include <iostream>\nif (a < b && b > c) {}\n```\n> quote";
        var dto = new CreateTheoryBlockDto
        {
            Title = "Code sample",
            Content = markdown
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.TheoryContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.TheoryContent!.Content.Should().Be(markdown);
    }

    #endregion

    #region Video Block Tests

    [Fact]
    public async Task CreateVideoBlock_WithValidYouTubeURL_ShouldReturn201()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateVideoBlockDto
        {
            Title = "C# Tutorial Video",
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;

        // Verify VideoId extraction
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.VideoContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.VideoContent!.VideoId.Should().Be("dQw4w9WgXcQ");
        block.VideoContent.VideoUrl.Should().Be(dto.VideoUrl);
    }

    [Fact]
    public async Task CreateVideoBlock_WithInvalidURL_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateVideoBlockDto
        {
            Title = "Test",
            VideoUrl = "https://example.com/invalid-video"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid YouTube URL");
    }

    [Fact]
    public async Task CreateVideoBlock_WithShortURL_ShouldExtractVideoId()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateVideoBlockDto
        {
            Title = "Short URL Test",
            VideoUrl = "https://youtu.be/dQw4w9WgXcQ"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.VideoContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.VideoContent!.VideoId.Should().Be("dQw4w9WgXcQ");
    }

    [Fact]
    public async Task CreateVideoBlock_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var dto = new CreateVideoBlockDto
        {
            Title = "Test",
            VideoUrl = "https://www.youtube.com/watch?v=test123"
        };

        // Act
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Quiz Block Tests

    [Fact]
    public async Task CreateQuizBlock_WithValidData_ShouldReturn201()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateQuizBlockDto
        {
            Title = "C# Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "What is C#?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "Programming language", IsCorrect = true },
                        new() { AnswerText = "Database", IsCorrect = false },
                        new() { AnswerText = "Framework", IsCorrect = false }
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;

        // Verify in database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qq => qq.Answers)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.Quiz!.Questions.Should().HaveCount(1);
        block.Quiz.Questions.First().Answers.Should().HaveCount(3);
        block.Quiz.Questions.First().Answers.Count(a => a.IsCorrect).Should().Be(1);
    }

    [Fact]
    public async Task CreateQuizBlock_WithNoQuestions_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateQuizBlockDto
        {
            Title = "Empty Quiz",
            Questions = new List<CreateQuizQuestionDto>() // Empty
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("at least one question");
    }

    [Fact]
    public async Task CreateQuizBlock_SingleChoiceWithMultipleCorrect_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateQuizBlockDto
        {
            Title = "Invalid Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "Test?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "A", IsCorrect = true },
                        new() { AnswerText = "B", IsCorrect = true } // Multiple correct!
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("exactly one correct answer");
    }

    [Fact]
    public async Task CreateQuizBlock_WithNoCorrectAnswer_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateQuizBlockDto
        {
            Title = "Invalid Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "Test?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "A", IsCorrect = false },
                        new() { AnswerText = "B", IsCorrect = false } // No correct!
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("at least one correct answer");
    }

    [Fact]
    // Quiz text is plain text the frontend escapes on display, so it is stored without HTML encoding
    public async Task CreateQuizBlock_WithAngleBrackets_ShouldStoreTextVerbatim()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateQuizBlockDto
        {
            Title = "XSS Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "Which is valid: List<int> or List(int)?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "a < b && it's true", IsCorrect = true },
                        new() { AnswerText = "Wrong answer", IsCorrect = false } // Need 2+ answers
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qq => qq.Answers)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.Quiz!.Questions.First().Content.Should().Be("Which is valid: List<int> or List(int)?");
        block.Quiz.Questions.First().Answers.OrderBy(a => a.OrderIndex).First().Text.Should().Be("a < b && it's true");
    }

    #endregion

    #region Problem Block Tests

    [Fact]
    public async Task CreateProblemBlock_WithExistingProblem_ShouldReturn201()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create a Problem first
        var problemId = await CreateProblemAsync();

        var dto = new CreateProblemBlockDto
        {
            Title = "Two Sum Problem",
            ProblemId = problemId
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;

        // Verify
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.Problem)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.ProblemId.Should().Be(problemId);
        block.Problem.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateProblemBlock_WithNonExistingProblem_ShouldReturn404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateProblemBlockDto
        {
            Title = "Test",
            ProblemId = Guid.NewGuid() // Non-existing
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("not found");
    }

    [Fact]
    public async Task CreateProblemBlock_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var dto = new CreateProblemBlockDto
        {
            Title = "Test",
            ProblemId = Guid.NewGuid()
        };

        // Act
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Delete Block Tests

    [Fact]
    public async Task DeleteBlock_AsOwner_ShouldReturn200()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create block first
        var blockId = await CreateTheoryBlockAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify deleted
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks.FindAsync(blockId);
        block.Should().BeNull();
    }

    [Fact]
    public async Task DeleteBlock_QuizBlock_ShouldAlsoDeleteQuizQuestionsAndAnswers()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateSimpleQuizBlockAsync();
        var quizId = await GetQuizIdAsync(blockId);

        // Act
        var response = await _client.DeleteAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        (await dbContext.Quizzes.AnyAsync(q => q.Id == quizId)).Should().BeFalse();
        (await dbContext.QuizQuestions.AnyAsync(q => q.QuizId == quizId)).Should().BeFalse();
        (await dbContext.QuizAnswers.AnyAsync(a => a.Question.QuizId == quizId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteCourse_WithBlocks_ShouldDeleteBlockContent()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var theoryBlockId = await CreateTheoryBlockAsync();
        var quizId = await GetQuizIdAsync(await CreateSimpleQuizBlockAsync());

        Guid courseId;
        Guid theoryContentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
            var block = await dbContext.CourseBlocks.Include(b => b.Subchapter.Chapter).SingleAsync(b => b.Id == theoryBlockId);
            courseId = block.Subchapter.Chapter.CourseId;
            theoryContentId = block.TheoryContentId!.Value;
        }

        // Act
        var response = await _client.DeleteAsync($"/api/courses/{courseId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
            (await dbContext.TheoryContents.AnyAsync(t => t.Id == theoryContentId)).Should().BeFalse();
            (await dbContext.Quizzes.AnyAsync(q => q.Id == quizId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task DeleteBlock_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var blockId = Guid.NewGuid();

        // Act
        var response = await _factory.CreateAnonymousClient().DeleteAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Get Block Tests

    [Fact]
    public async Task GetBlockById_TheoryBlock_AsEnrolledStudent_ShouldReturnContent()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var createDto = new CreateTheoryBlockDto
        {
            Title = "C# Basics",
            Content = "# Introduction\n\nC# is a **powerful** language."
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        await PublishCourseAsync();
        await EnrollStudentAsync();

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        block.Should().NotBeNull();
        block!.Id.Should().Be(blockId);
        block.Title.Should().Be("C# Basics");
        block.Type.Should().Be(BlockType.Theory);
        block.TheoryContent.Should().NotBeNull();
        block.TheoryContent!.Content.Should().Contain("Introduction");
        block.TheoryContent.Content.Should().Contain("powerful");
    }

    [Fact]
    public async Task GetBlockById_VideoBlock_ShouldReturnVideoData()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var createDto = new CreateVideoBlockDto
        {
            Title = "Tutorial Video",
            VideoUrl = "https://www.youtube.com/watch?v=ABC123DEF45"
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        await PublishCourseAsync();
        await EnrollStudentAsync();

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        block.Should().NotBeNull();
        block!.Type.Should().Be(BlockType.Video);
        block.VideoContent.Should().NotBeNull();
        block.VideoContent!.VideoId.Should().Be("ABC123DEF45");
        block.VideoContent.VideoUrl.Should().Be(createDto.VideoUrl);
    }

    [Fact]
    public async Task GetBlockById_QuizBlock_AsEnrolledStudent_ShouldHideCorrectAnswersAndExplanation()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var createDto = new CreateQuizBlockDto
        {
            Title = "Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "What is 2+2?",
                    Type = "SingleChoice",
                    Points = 10,
                    Explanation = "Because two plus two is FOUR-EXPLAINED",
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "3", IsCorrect = false },
                        new() { AnswerText = "4", IsCorrect = true },
                        new() { AnswerText = "5", IsCorrect = false }
                    }
                }
            }
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        await PublishCourseAsync();
        await EnrollStudentAsync();

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        block.Should().NotBeNull();
        block!.Type.Should().Be(BlockType.Quiz);
        block.Quiz.Should().NotBeNull();
        block.Quiz!.Questions.Should().HaveCount(1);

        var question = block.Quiz.Questions.First();
        question.Content.Should().Contain("2+2");
        question.Answers.Should().HaveCount(3);
        question.Explanation.Should().BeNull();

        // Neither the correct flags nor the explanation may reach a student before submitting
        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().NotContain("isCorrect");
        jsonString.Should().NotContain("FOUR-EXPLAINED");
    }

    [Fact]
    public async Task GetBlockById_QuizBlock_AsCourseOwner_ShouldIncludeCorrectAnswersAndExplanation()
    {
        // Arrange
        var blockId = await CreateSimpleQuizBlockAsync(explanation: "Owner sees this");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        var question = block!.Quiz!.Questions.First();
        question.Answers.Should().ContainSingle(a => a.IsCorrect == true);
        question.Explanation.Should().Be("Owner sees this");
    }

    [Fact]
    public async Task GetBlockById_PublishedCourse_AsAnotherTeacher_ShouldReturn403()
    {
        // Arrange
        var blockId = await CreateSimpleQuizBlockAsync();
        await PublishCourseAsync();
        var anotherTeacherToken = await GetTeacherTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherTeacherToken);

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBlockById_PublishedCourse_AsNotEnrolledStudent_ShouldReturn403()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        await PublishCourseAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Test content");
    }

    [Fact]
    public async Task GetSubchapterBlocks_PublishedCourse_AsNotEnrolledStudent_ShouldReturn403()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        await CreateTheoryBlockAsync();
        await PublishCourseAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        // Act
        var response = await _client.GetAsync($"/api/subchapters/{_subchapterId}/blocks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBlockById_PublishedCourse_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        await PublishCourseAsync();
        var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubchapterBlocks_PublishedCourse_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        await CreateTheoryBlockAsync();
        await PublishCourseAsync();
        var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.GetAsync($"/api/subchapters/{_subchapterId}/blocks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBlockById_UnpublishedCourse_AsEnrolledStudent_ShouldReturn403()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        await PublishCourseAsync();
        await EnrollStudentAsync();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var unpublish = await _client.PostAsync($"/api/courses/{await GetCourseIdAsync()}/unpublish", null);
        unpublish.StatusCode.Should().Be(HttpStatusCode.OK);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        // Act
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBlockById_NonExisting_ShouldReturn404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Act
        var response = await _client.GetAsync($"/api/blocks/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBlockById_DraftCourse_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var response = await anonymousClient.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubchapterBlocks_DraftCourse_AsAnotherTeacher_ShouldReturn404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        await CreateTheoryBlockAsync();
        var anotherTeacherToken = await GetTeacherTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherTeacherToken);

        // Act
        var response = await _client.GetAsync($"/api/subchapters/{_subchapterId}/blocks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubchapterBlocks_DraftCourse_AsOwner_ShouldReturn200()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        await CreateTheoryBlockAsync();

        // Act
        var response = await _client.GetAsync($"/api/subchapters/{_subchapterId}/blocks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blocks = await response.Content.ReadFromJsonAsync<List<BlockResponseDto>>();
        blocks.Should().ContainSingle();
    }

    [Fact]
    public async Task GetSubchapterBlocks_AsEnrolledStudent_ShouldReturnOrderedList()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var theory = new CreateTheoryBlockDto { Title = "Theory 1", Content = "Content 1" };
        var video = new CreateVideoBlockDto { Title = "Video 1", VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" };
        var theory2 = new CreateTheoryBlockDto { Title = "Theory 2", Content = "Content 2" };

        await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", theory);
        await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", video);
        await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", theory2);

        await PublishCourseAsync();
        await EnrollStudentAsync();

        // Act
        var response = await _client.GetAsync($"/api/subchapters/{_subchapterId}/blocks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var blocks = await response.Content.ReadFromJsonAsync<List<BlockResponseDto>>();
        blocks.Should().NotBeNull();
        blocks!.Should().HaveCount(3);

        blocks[0].OrderIndex.Should().Be(1);
        blocks[0].Title.Should().Be("Theory 1");
        blocks[1].OrderIndex.Should().Be(2);
        blocks[1].Title.Should().Be("Video 1");
        blocks[2].OrderIndex.Should().Be(3);
        blocks[2].Title.Should().Be("Theory 2");
    }

    #endregion

    #region Update Block Tests

    [Fact]
    public async Task UpdateTheoryBlock_AsOwner_ShouldReturn200()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create block first
        var createDto = new CreateTheoryBlockDto
        {
            Title = "Original Title",
            Content = "Original content"
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        // Update
        var updateDto = new UpdateTheoryBlockDto
        {
            Title = "Updated Title",
            Content = "# Updated Content\n\nThis is **new** content."
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/theory", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify in database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.TheoryContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block.Should().NotBeNull();
        block!.Title.Should().Be("Updated Title");
        block.TheoryContent!.Content.Should().Contain("Updated Content");
        block.TheoryContent.Content.Should().Contain("new");
    }

    [Fact]
    public async Task UpdateTheoryBlock_WithCodeSample_ShouldStoreContentVerbatim()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var blockId = await CreateTheoryBlockAsync();

        var updateDto = new UpdateTheoryBlockDto
        {
            Title = "Updated",
            Content = "```java\nMap<String, List<Integer>> m = new HashMap<>();\n```"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/theory", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.TheoryContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.TheoryContent!.Content.Should().Be(updateDto.Content);
    }

    [Fact]
    public async Task UpdateTheoryBlock_NonExisting_ShouldReturn404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var updateDto = new UpdateTheoryBlockDto
        {
            Title = "Test",
            Content = "Content"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{Guid.NewGuid()}/theory", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateVideoBlock_AsOwner_ShouldReturn200()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create video block
        var createDto = new CreateVideoBlockDto
        {
            Title = "Original Video",
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        // Update
        var updateDto = new UpdateVideoBlockDto
        {
            Title = "Updated Video",
            VideoUrl = "https://www.youtube.com/watch?v=ABC123DEF45"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/video", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.VideoContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.Title.Should().Be("Updated Video");
        block.VideoContent!.VideoId.Should().Be("ABC123DEF45");
        block.VideoContent.VideoUrl.Should().Be(updateDto.VideoUrl);
    }

    [Fact]
    public async Task UpdateVideoBlock_InvalidUrl_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var createDto = new CreateVideoBlockDto
        {
            Title = "Video",
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        var updateDto = new UpdateVideoBlockDto
        {
            Title = "Updated",
            VideoUrl = "https://invalid-url.com/video"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/video", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateQuizBlock_AsOwner_ShouldReturn200()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create quiz block
        var createDto = new CreateQuizBlockDto
        {
            Title = "Original Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "Old question?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "A", IsCorrect = true },
                        new() { AnswerText = "B", IsCorrect = false }
                    }
                }
            }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = created!.Id;

        // Update with new question
        var updateDto = new UpdateQuizBlockDto
        {
            Title = "Updated Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "New question?",
                    Type = "SingleChoice",
                    Points = 15,
                    Explanation = "Answer 1 is correct because it is first.",
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "Answer 1", IsCorrect = true },
                        new() { AnswerText = "Answer 2", IsCorrect = false }
                    }
                }
            }
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/quiz", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var block = await dbContext.CourseBlocks
            .Include(b => b.Quiz)
                .ThenInclude(q => q!.Questions)
                    .ThenInclude(qq => qq.Answers)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.Title.Should().Be("Updated Quiz");
        block.Quiz!.Questions.Should().HaveCount(1);
        block.Quiz.Questions.First().Content.Should().Contain("New question");
        block.Quiz.Questions.First().Answers.Should().HaveCount(2);
        block.Quiz.Questions.First().Points.Should().Be(15);
        block.Quiz.Questions.First().Explanation.Should().Contain("Answer 1 is correct");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task UpdateBlockOrder_OutsideBlockRange_ShouldReturn400(int newOrderIndex)
    {
        // Arrange - two blocks, so only 1 and 2 are valid positions
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        await CreateTheoryBlockAsync();

        // Act
        var response = await _client.PatchAsJsonAsync(
            $"/api/subchapters/{_subchapterId}/blocks/{blockId}/order",
            new UpdateBlockOrderDto { NewOrderIndex = newOrderIndex });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateBlockOrder_MoveDown_ShouldReorder()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create 3 blocks
        var block1 = await CreateTheoryBlockAsync(); // OrderIndex = 1

        var block2Response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory",
            new CreateTheoryBlockDto { Title = "Block 2", Content = "C2" });
        var block2Created = await block2Response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var block2Id = block2Created!.Id; // OrderIndex = 2

        var block3Response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory",
            new CreateTheoryBlockDto { Title = "Block 3", Content = "C3" });
        var block3Created = await block3Response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var block3Id = block3Created!.Id; // OrderIndex = 3

        // Move block1 from position 1 to position 3
        var updateDto = new UpdateBlockOrderDto { NewOrderIndex = 3 };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{block1}/order", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var blocks = await dbContext.CourseBlocks
            .Where(b => b.SubchapterId == _subchapterId)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync();

        blocks.Should().HaveCount(3);
        blocks[0].Id.Should().Be(block2Id); // Was 2, now 1
        blocks[0].OrderIndex.Should().Be(1);
        blocks[1].Id.Should().Be(block3Id); // Was 3, now 2
        blocks[1].OrderIndex.Should().Be(2);
        blocks[2].Id.Should().Be(block1); // Was 1, now 3
        blocks[2].OrderIndex.Should().Be(3);
    }

    [Fact]
    public async Task UpdateBlockOrder_MoveUp_ShouldReorder()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var block1Response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory",
            new CreateTheoryBlockDto { Title = "Block 1", Content = "C1" });
        var block1Created = await block1Response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var block1Id = block1Created!.Id;

        var block2Response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory",
            new CreateTheoryBlockDto { Title = "Block 2", Content = "C2" });
        var block2Created = await block2Response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var block2Id = block2Created!.Id;

        var block3Response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory",
            new CreateTheoryBlockDto { Title = "Block 3", Content = "C3" });
        var block3Created = await block3Response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var block3Id = block3Created!.Id;

        // Move block3 from position 3 to position 1
        var updateDto = new UpdateBlockOrderDto { NewOrderIndex = 1 };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{block3Id}/order", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var blocks = await dbContext.CourseBlocks
            .Where(b => b.SubchapterId == _subchapterId)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync();

        blocks[0].Id.Should().Be(block3Id); // Was 3, now 1
        blocks[0].OrderIndex.Should().Be(1);
        blocks[1].Id.Should().Be(block1Id); // Was 1, now 2
        blocks[1].OrderIndex.Should().Be(2);
        blocks[2].Id.Should().Be(block2Id); // Was 2, now 3
        blocks[2].OrderIndex.Should().Be(3);
    }

    [Fact]
    public async Task UpdateBlock_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var blockId = Guid.NewGuid();
        var updateDto = new UpdateTheoryBlockDto { Title = "Test", Content = "Content" };

        // Act
        var response = await _factory.CreateAnonymousClient().PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/theory", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Ownership, Route And Problem Block Tests

    public static TheoryData<string, string> BlockMutations => new()
    {
        { "PUT", "theory" },
        { "PUT", "video" },
        { "PUT", "quiz" },
        { "PUT", "problem" },
        { "PATCH", "order" },
        { "DELETE", "" }
    };

    [Theory]
    [MemberData(nameof(BlockMutations))]
    public async Task BlockMutation_AsAnotherTeacher_ShouldReturn403(string method, string action)
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        var anotherTeacherToken = await GetTeacherTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherTeacherToken);

        // Act
        var response = await SendBlockMutationAsync(method, action, _subchapterId, blockId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var db = NewDbContext();
        (await db.CourseBlocks.AnyAsync(b => b.Id == blockId)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BlockMutations))]
    public async Task BlockMutation_UnderAnotherSubchapterRoute_ShouldReturn404(string method, string action)
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        var otherSubchapterId = await CreateSiblingSubchapterAsync();

        // Act
        var response = await SendBlockMutationAsync(method, action, otherSubchapterId, blockId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateTheoryBlock_AsAnotherTeacher_ShouldReturn403()
    {
        // Arrange
        var anotherTeacherToken = await GetTeacherTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherTeacherToken);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory",
            new CreateTheoryBlockDto { Title = "Intruder", Content = "x" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateProblemBlock_WithAnotherAuthorsProblem_ShouldReturn403()
    {
        // Arrange
        var foreignProblemId = await CreateProblemAsync(foreignAuthor: true);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem",
            new CreateProblemBlockDto { Title = "Borrowed", ProblemId = foreignProblemId });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var db = NewDbContext();
        (await db.CourseBlocks.AnyAsync(b => b.SubchapterId == _subchapterId)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProblemBlock_AsOwner_ShouldChangeTitleAndProblem()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var firstProblemId = await CreateProblemAsync();
        var secondProblemId = await CreateProblemAsync();
        var create = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem",
            new CreateProblemBlockDto { Title = "Old", ProblemId = firstProblemId });
        var blockId = (await create.Content.ReadFromJsonAsync<BlockCreatedResponseDto>())!.Id;

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/problem",
            new UpdateProblemBlockDto { Title = "New", ProblemId = secondProblemId });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        var block = await db.CourseBlocks.SingleAsync(b => b.Id == blockId);
        block.Title.Should().Be("New");
        block.ProblemId.Should().Be(secondProblemId);
    }

    [Fact]
    public async Task UpdateProblemBlock_WithAnotherAuthorsProblem_ShouldReturn403()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var ownProblemId = await CreateProblemAsync();
        var foreignProblemId = await CreateProblemAsync(foreignAuthor: true);
        var create = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem",
            new CreateProblemBlockDto { Title = "Mine", ProblemId = ownProblemId });
        var blockId = (await create.Content.ReadFromJsonAsync<BlockCreatedResponseDto>())!.Id;

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/problem",
            new UpdateProblemBlockDto { Title = "Mine", ProblemId = foreignProblemId });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateProblemBlock_OnTheoryBlock_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var blockId = await CreateTheoryBlockAsync();
        var problemId = await CreateProblemAsync();

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/problem",
            new UpdateProblemBlockDto { Title = "x", ProblemId = problemId });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateVideoBlock_WithShortLink_ShouldStoreCanonicalWatchUrl()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video",
            new CreateVideoBlockDto { Title = "Short", VideoUrl = "https://youtu.be/dQw4w9WgXcQ?t=42" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var blockId = (await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>())!.Id;
        await using var db = NewDbContext();
        var video = await db.CourseBlocks.Where(b => b.Id == blockId).Select(b => b.VideoContent!).SingleAsync();
        video.VideoUrl.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        video.VideoId.Should().Be("dQw4w9WgXcQ");
    }

    [Fact]
    public async Task CreateVideoBlock_WithUrlEmbeddedInText_ShouldReturn400()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video",
            new CreateVideoBlockDto { Title = "x", VideoUrl = "https://evil.example/?u=youtube.com/watch?v=dQw4w9WgXcQ" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteBlock_ShouldReindexRemainingBlocks()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        var first = await CreateTheoryBlockAsync();
        var second = await CreateTheoryBlockAsync();
        var third = await CreateTheoryBlockAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/subchapters/{_subchapterId}/blocks/{first}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        var indexes = await db.CourseBlocks.Where(b => b.SubchapterId == _subchapterId)
            .OrderBy(b => b.OrderIndex).Select(b => new { b.Id, b.OrderIndex }).ToListAsync();
        indexes.Select(i => i.Id).Should().Equal(second, third);
        indexes.Select(i => i.OrderIndex).Should().Equal(1, 2);
    }

    [Fact]
    public async Task UpdateQuizBlock_ShouldReplaceQuestionsAtomically()
    {
        // Arrange
        var blockId = await CreateSimpleQuizBlockAsync();
        var quizId = await GetQuizIdAsync(blockId);
        var updateDto = new UpdateQuizBlockDto
        {
            Title = "Updated",
            Questions =
            [
                new CreateQuizQuestionDto
                {
                    QuestionText = "New question",
                    Type = "TrueFalse",
                    Answers = [new() { AnswerText = "True", IsCorrect = true }, new() { AnswerText = "False" }]
                }
            ]
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/quiz", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        var questions = await db.QuizQuestions.Include(q => q.Answers).Where(q => q.QuizId == quizId).ToListAsync();
        questions.Should().ContainSingle(q => q.Content == "New question");
        questions.Single().Answers.Should().HaveCount(2);
    }

    #endregion

    #region Helper Methods

    private Task<HttpResponseMessage> SendBlockMutationAsync(string method, string action, Guid subchapterId, Guid blockId)
    {
        var baseUrl = $"/api/subchapters/{subchapterId}/blocks/{blockId}";

        return (method, action) switch
        {
            ("PUT", "theory") => _client.PutAsJsonAsync($"{baseUrl}/theory", new UpdateTheoryBlockDto { Title = "t", Content = "c" }),
            ("PUT", "video") => _client.PutAsJsonAsync($"{baseUrl}/video", new UpdateVideoBlockDto { Title = "t", VideoUrl = "https://youtu.be/dQw4w9WgXcQ" }),
            ("PUT", "quiz") => _client.PutAsJsonAsync($"{baseUrl}/quiz", new UpdateQuizBlockDto
            {
                Title = "t",
                Questions = [new() { QuestionText = "q", Type = "SingleChoice", Answers = [new() { AnswerText = "a", IsCorrect = true }, new() { AnswerText = "b" }] }]
            }),
            ("PUT", "problem") => _client.PutAsJsonAsync($"{baseUrl}/problem", new UpdateProblemBlockDto { Title = "t", ProblemId = Guid.NewGuid() }),
            ("PATCH", "order") => _client.PatchAsJsonAsync($"{baseUrl}/order", new UpdateBlockOrderDto { NewOrderIndex = 1 }),
            _ => _client.DeleteAsync(baseUrl)
        };
    }

    private async Task EnrollStudentAsync()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);
        var response = await _client.PostAsync($"/api/enrollments/courses/{await GetCourseIdAsync()}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> GetCourseIdAsync()
    {
        await using var db = NewDbContext();
        return await db.Subchapters.Where(s => s.Id == _subchapterId).Select(s => s.Chapter.CourseId).SingleAsync();
    }

    private async Task<Guid> CreateSiblingSubchapterAsync()
    {
        await using var db = NewDbContext();
        var chapterId = await db.Subchapters.Where(s => s.Id == _subchapterId).Select(s => s.ChapterId).SingleAsync();
        var subchapter = new Subchapter { Title = "Subchapter 1.2", OrderIndex = 2, ChapterId = chapterId, Chapter = null! };
        db.Subchapters.Add(subchapter);
        await db.SaveChangesAsync();
        return subchapter.Id;
    }

    private async Task<Guid> CreateSimpleQuizBlockAsync(string? explanation = null)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateQuizBlockDto
        {
            Title = "Ownership Quiz",
            Questions = new List<CreateQuizQuestionDto>
            {
                new()
                {
                    QuestionText = "What is 2+2?",
                    Type = "SingleChoice",
                    Points = 10,
                    Explanation = explanation,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "3", IsCorrect = false },
                        new() { AnswerText = "4", IsCorrect = true }
                    }
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/quiz", dto);
        var created = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        return created!.Id;
    }

    private async Task<string> GetTeacherTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        return response.GetAccessToken();
    }

    private async Task<string> GetStudentTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        return response.GetAccessToken();
    }

    private async Task<Guid> GetQuizIdAsync(Guid blockId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
        var block = await dbContext.CourseBlocks.SingleAsync(b => b.Id == blockId);
        return block.QuizId!.Value;
    }

    private async Task<Guid> CreateSubchapterAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        // Get teacher user
        var teacher = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Teacher);

        var course = new Course
        {
            Title = "Test Course",
            Description = "Test",
            Status = CourseStatus.Draft,
            InstructorId = teacher.Id,
            Instructor = teacher
        };

        var chapter = new Chapter
        {
            Title = "Chapter 1",
            OrderIndex = 1,
            Course = course
        };

        var subchapter = new Subchapter
        {
            Title = "Subchapter 1.1",
            OrderIndex = 1,
            Chapter = chapter
        };

        dbContext.Courses.Add(course);
        dbContext.Chapters.Add(chapter);
        dbContext.Subchapters.Add(subchapter);
        await dbContext.SaveChangesAsync();

        return subchapter.Id;
    }

    // Content of a draft course is visible only to its instructor
    private async Task PublishCourseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var course = await dbContext.Courses
            .FirstAsync(c => c.Chapters.Any(ch => ch.Subchapters.Any(s => s.Id == _subchapterId)));

        course.Status = CourseStatus.Published;
        course.PublishedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> CreateTheoryBlockAsync()
    {
        var dto = new CreateTheoryBlockDto
        {
            Title = "Test Block",
            Content = "Test content"
        };

        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);
        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        return responseDto!.Id;
    }

    // A fresh context per assertion, so reads never come from a change tracker that saw the writes
    private CodeLearning.Infrastructure.Data.ApplicationDbContext NewDbContext()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider
            .GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>().Database.GetConnectionString();
        return new CodeLearning.Infrastructure.Data.ApplicationDbContext(
            new DbContextOptionsBuilder<CodeLearning.Infrastructure.Data.ApplicationDbContext>().UseNpgsql(connectionString).Options);
    }

    // Authored by the course instructor, or by a different teacher when foreignAuthor is set
    private async Task<Guid> CreateProblemAsync(bool foreignAuthor = false)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        User author;
        if (foreignAuthor)
        {
            author = TestDataBuilder.CreateValidUser(UserRole.Teacher);
            dbContext.Users.Add(author);
        }
        else
        {
            author = await dbContext.Subchapters
                .Where(s => s.Id == _subchapterId)
                .Select(s => s.Chapter.Course.Instructor)
                .SingleAsync();
        }

        var problem = new Problem
        {
            Title = "Two Sum",
            Description = "Find two numbers that add up to target",
            Difficulty = DifficultyLevel.Easy,
            AuthorId = author.Id,
            Author = author
        };

        dbContext.Problems.Add(problem);
        await dbContext.SaveChangesAsync();

        return problem.Id;
    }

    #endregion
}
