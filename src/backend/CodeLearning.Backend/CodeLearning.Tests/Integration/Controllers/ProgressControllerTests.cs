using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeLearning.Application.DTOs.Progress;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CodeLearning.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for ProgressController
/// Tests block completion and progress tracking
/// </summary>
public class ProgressControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private string _studentToken = null!;
    private Guid _courseId;
    private Guid _block1Id;
    private Guid _block2Id;
    private Guid _block3Id;

    public ProgressControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        _studentToken = await GetStudentTokenAsync();
        (_courseId, _block1Id, _block2Id, _block3Id) = await CreateCourseWithBlocksAsync();
        
        // Enroll student
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);
        await _client.PostAsync($"/api/enrollments/courses/{_courseId}", null);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Complete Block Tests

    [Fact]
    public async Task CompleteBlock_FirstTime_ShouldReturn200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CompleteBlockResponseDto>();
        result.Should().NotBeNull();
        result!.BlockId.Should().Be(_block1Id);
        result.NextBlockId.Should().NotBeNull(); // Should be block2
        result.Message.Should().Contain("completed successfully");
        result.CourseCompleted.Should().BeFalse();

        // Verify in database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();
        var studentId = await GetCurrentStudentIdAsync(dbContext);

        var progress = await dbContext.StudentBlockProgresses
            .FirstOrDefaultAsync(bp => bp.BlockId == _block1Id && bp.StudentId == studentId);

        progress.Should().NotBeNull();
        progress!.IsCompleted.Should().BeTrue();
        progress.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CompleteBlock_AlreadyCompleted_ShouldReturn200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        // Complete first time
        await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        // Complete again
        var response = await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CompleteBlockResponseDto>();
        result!.Message.Should().Contain("already completed");
    }

    [Fact]
    public async Task CompleteBlock_LastBlock_ShouldMarkCourseComplete()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        // Complete all blocks
        await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);
        await _client.PostAsync($"/api/progress/blocks/{_block2Id}/complete", null);
        
        var response = await _client.PostAsync($"/api/progress/blocks/{_block3Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CompleteBlockResponseDto>();
        result!.CourseCompleted.Should().BeTrue();
        result.NextBlockId.Should().BeNull();
        result.Message.Should().Contain("Congratulations");
    }

    [Fact]
    public async Task CompleteBlock_NotEnrolled_ShouldReturn400()
    {
        // Create new student
        var newStudentToken = await GetStudentTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newStudentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("must be enrolled");
    }

    [Fact]
    public async Task CompleteBlock_NonExistingBlock_ShouldReturn404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{Guid.NewGuid()}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompleteBlock_WithoutAuth_ShouldReturn401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Get Course Progress Tests

    [Fact]
    public async Task GetCourseProgress_NoProgress_ShouldReturn0Percent()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var progress = await response.Content.ReadFromJsonAsync<CourseProgressDto>();
        progress.Should().NotBeNull();
        progress!.CourseId.Should().Be(_courseId);
        progress.CompletedBlocksCount.Should().Be(0);
        progress.TotalBlocksCount.Should().Be(3);
        progress.ProgressPercentage.Should().Be(0);
        progress.Chapters.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetCourseProgress_PartialProgress_ShouldCalculateCorrectly()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        // Complete 1 out of 3 blocks
        await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var progress = await response.Content.ReadFromJsonAsync<CourseProgressDto>();
        progress!.CompletedBlocksCount.Should().Be(1);
        progress.TotalBlocksCount.Should().Be(3);
        progress.ProgressPercentage.Should().BeApproximately(33.33, 0.1);
        
        // Check block details
        var allBlocks = progress.Chapters
            .SelectMany(ch => ch.Subchapters)
            .SelectMany(sub => sub.Blocks)
            .ToList();

        allBlocks.Should().HaveCount(3);
        allBlocks.Count(b => b.IsCompleted).Should().Be(1);
        allBlocks.First(b => b.BlockId == _block1Id).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task GetCourseProgress_NotEnrolled_ShouldReturn400()
    {
        var newStudentToken = await GetStudentTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newStudentToken);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Next Block Tests

    [Fact]
    public async Task GetNextBlock_NoProgress_ShouldReturnFirstBlock()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}/next-block");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(_block1Id.ToString());
    }

    [Fact]
    public async Task GetNextBlock_AfterCompletion_ShouldReturnSecondBlock()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}/next-block");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(_block2Id.ToString());
    }

    [Fact]
    public async Task GetNextBlock_AllCompleted_ShouldReturnNull()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);
        await _client.PostAsync($"/api/progress/blocks/{_block2Id}/complete", null);
        await _client.PostAsync($"/api/progress/blocks/{_block3Id}/complete", null);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}/next-block");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Course completed");
        content.Should().Contain("\"nextBlockId\":null");
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

    private async Task<(Guid courseId, Guid block1Id, Guid block2Id, Guid block3Id)> CreateCourseWithBlocksAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        // Create teacher if not exists
        var teacher = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Teacher);
        if (teacher == null)
        {
            // Teacher token creation will create the user
            await GetTeacherTokenAsync();
            teacher = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Teacher);
        }

        var course = new Course
        {
            Title = "Test Course",
            Description = "Test course for progress",
            Status = CourseStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow,
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

        var theory1 = new TheoryContent { Content = "Content 1", Block = null! };
        var theory2 = new TheoryContent { Content = "Content 2", Block = null! };
        var video1 = new VideoContent { VideoUrl = "https://youtube.com/watch?v=test1", VideoId = "test1", Block = null! };

        var block1 = new CourseBlock
        {
            Title = "Block 1",
            Type = BlockType.Theory,
            OrderIndex = 1,
            Subchapter = subchapter,
            TheoryContent = theory1
        };

        var block2 = new CourseBlock
        {
            Title = "Block 2",
            Type = BlockType.Theory,
            OrderIndex = 2,
            Subchapter = subchapter,
            TheoryContent = theory2
        };

        var block3 = new CourseBlock
        {
            Title = "Block 3",
            Type = BlockType.Video,
            OrderIndex = 3,
            Subchapter = subchapter,
            VideoContent = video1
        };

        dbContext.Courses.Add(course);
        dbContext.Chapters.Add(chapter);
        dbContext.Subchapters.Add(subchapter);
        dbContext.TheoryContents.AddRange(theory1, theory2);
        dbContext.VideoContents.Add(video1);
        dbContext.CourseBlocks.AddRange(block1, block2, block3);

        await dbContext.SaveChangesAsync();

        return (course.Id, block1.Id, block2.Id, block3.Id);
    }

    private async Task<Guid> GetCurrentStudentIdAsync(CodeLearning.Infrastructure.Data.ApplicationDbContext dbContext)
    {
        var student = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Student);
        return student.Id;
    }

    #endregion
}
