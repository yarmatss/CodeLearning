using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

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
    public async Task CreateTheoryBlock_WithXSSContent_ShouldSanitize()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        var dto = new CreateTheoryBlockDto
        {
            Title = "XSS Test",
            Content = "<script>alert('XSS')</script><p>Safe content</p>"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify in database that <script> was removed
        var responseDto = await response.Content.ReadFromJsonAsync<BlockCreatedResponseDto>();
        var blockId = responseDto!.Id;
        
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
        
        var block = await dbContext.CourseBlocks
            .Include(b => b.TheoryContent)
            .FirstOrDefaultAsync(b => b.Id == blockId);

        block!.TheoryContent!.Content.Should().NotContain("<script>");
        block.TheoryContent.Content.Should().Contain("Safe content");
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
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", dto);

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
    public async Task CreateQuizBlock_WithXSSInQuestion_ShouldSanitize()
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
                    QuestionText = "<script>alert('XSS')</script>What is C#?",
                    Type = "SingleChoice",
                    Points = 10,
                    Answers = new List<CreateQuizAnswerDto>
                    {
                        new() { AnswerText = "Answer <script>hack()</script>", IsCorrect = true },
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

        block!.Quiz!.Questions.First().Content.Should().NotContain("<script>");
        block.Quiz.Questions.First().Answers.First().Text.Should().NotContain("<script>");
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
        var response = await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/problem", dto);

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
    public async Task DeleteBlock_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var blockId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Get Block Tests

    [Fact]
    public async Task GetBlockById_TheoryBlock_ShouldReturnContent()
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

        // Act (no auth needed - public endpoint)
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        block.Should().NotBeNull();
        block!.Id.Should().Be(blockId);
        block.Title.Should().Be("C# Basics");
        block.Type.ToString().Should().Be("Theory");
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

        // Act
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        block.Should().NotBeNull();
        block!.Type.ToString().Should().Be("Video");
        block.VideoContent.Should().NotBeNull();
        block.VideoContent!.VideoId.Should().Be("ABC123DEF45");
        block.VideoContent.VideoUrl.Should().Be(createDto.VideoUrl);
    }

    [Fact]
    public async Task GetBlockById_QuizBlock_ShouldHideCorrectAnswers()
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

        // Act - Student views quiz
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/api/blocks/{blockId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var block = await response.Content.ReadFromJsonAsync<BlockResponseDto>();
        block.Should().NotBeNull();
        block!.Type.ToString().Should().Be("Quiz");
        block.Quiz.Should().NotBeNull();
        block.Quiz!.Questions.Should().HaveCount(1);
        
        var question = block.Quiz.Questions.First();
        question.Content.Should().Contain("2+2");
        question.Answers.Should().HaveCount(3);
        
        // Verify IsCorrect is NOT in JSON (security check)
        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().NotContain("isCorrect"); // JSON property names are camelCase
    }

    [Fact]
    public async Task GetBlockById_NonExisting_ShouldReturn404()
    {
        // Act
        var response = await _client.GetAsync($"/api/blocks/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSubchapterBlocks_ShouldReturnOrderedList()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);

        // Create multiple blocks
        var theory = new CreateTheoryBlockDto { Title = "Theory 1", Content = "Content 1" };
        var video = new CreateVideoBlockDto { Title = "Video 1", VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" }; // Valid YouTube ID
        var theory2 = new CreateTheoryBlockDto { Title = "Theory 2", Content = "Content 2" };

        await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", theory);
        await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/video", video);
        await _client.PostAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/theory", theory2);

        // Act
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/api/subchapters/{_subchapterId}/blocks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var blocks = await response.Content.ReadFromJsonAsync<List<BlockResponseDto>>();
        blocks.Should().NotBeNull();
        blocks!.Should().HaveCount(3);
        
        // Verify order
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
    public async Task UpdateTheoryBlock_WithXSS_ShouldSanitize()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken);
        
        var blockId = await CreateTheoryBlockAsync();

        var updateDto = new UpdateTheoryBlockDto
        {
            Title = "Updated",
            Content = "<script>alert('XSS')</script><p>Safe content</p>"
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

        block!.TheoryContent!.Content.Should().NotContain("<script>");
        block.TheoryContent.Content.Should().Contain("Safe content");
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
        var response = await _client.PutAsJsonAsync($"/api/subchapters/{_subchapterId}/blocks/{blockId}/theory", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Helper Methods

    private async Task<string> GetTeacherTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<Application.DTOs.Auth.LoginResponseDto>();
        return content?.AccessToken ?? throw new Exception("No token");
    }

    private async Task<string> GetStudentTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        var content = await response.Content.ReadFromJsonAsync<Application.DTOs.Auth.LoginResponseDto>();
        return content?.AccessToken ?? throw new Exception("No token");
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

    private async Task<Guid> CreateProblemAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var teacher = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Teacher);

        var problem = new Problem
        {
            Title = "Two Sum",
            Description = "Find two numbers that add up to target",
            Difficulty = DifficultyLevel.Easy,
            AuthorId = teacher.Id,
            Author = teacher
        };

        dbContext.Problems.Add(problem);
        await dbContext.SaveChangesAsync();

        return problem.Id;
    }

    #endregion
}
