using CodeLearning.Application.DTOs.Chapter;
using CodeLearning.Application.DTOs.Subchapter;
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

namespace CodeLearning.Tests.Integration.Controllers;

// Chapters and subchapters: rename, reorder and delete are owner-only, check the parent id in the route,
// and deleting leaves no block content behind
public class ChaptersControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IntegrationTestWebAppFactory _factory;
    private string _ownerToken = null!;
    private Guid _courseId;
    private Guid _chapter1Id;
    private Guid _chapter2Id;
    private Guid _subchapter1Id;
    private Guid _subchapter2Id;

    public ChaptersControllerTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _ownerToken = await RegisterAsync("Teacher");
        await SeedCourseAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _ownerToken);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Chapters

    [Fact]
    public async Task AddChapter_ShouldAppendWithNextOrderIndex()
    {
        var response = await _client.PostAsJsonAsync($"/api/courses/{_courseId}/chapters", new CreateChapterDto { Title = "Third" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<ChapterResponseDto>())!.OrderIndex.Should().Be(3);
    }

    [Fact]
    public async Task AddChapter_ConcurrentRequests_ShouldGetDistinctOrderIndexes()
    {
        var requests = Enumerable.Range(0, 5)
            .Select(i => _client.PostAsJsonAsync($"/api/courses/{_courseId}/chapters", new CreateChapterDto { Title = $"C{i}" }));

        var responses = await Task.WhenAll(requests);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        await using var db = NewDbContext();
        var indexes = await db.Chapters.Where(c => c.CourseId == _courseId).Select(c => c.OrderIndex).OrderBy(i => i).ToListAsync();
        indexes.Should().Equal(1, 2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public async Task RenameChapter_AsOwner_ShouldReturnUpdatedChapter()
    {
        var response = await _client.PutAsJsonAsync($"/api/courses/{_courseId}/chapters/{_chapter1Id}", new UpdateChapterDto { Title = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var chapter = await response.Content.ReadFromJsonAsync<ChapterResponseDto>();
        chapter!.Title.Should().Be("Renamed");
        chapter.SubchaptersCount.Should().Be(2);
    }

    [Fact]
    public async Task RenameChapter_EmptyTitle_ShouldReturn400()
    {
        var response = await _client.PutAsJsonAsync($"/api/courses/{_courseId}/chapters/{_chapter1Id}", new UpdateChapterDto { Title = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task ChapterMutation_AsAnotherTeacher_ShouldReturn403(string method)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAsync("Teacher"));

        var response = await SendChapterMutationAsync(method, _courseId, _chapter1Id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task ChapterMutation_UnderAnotherCourseRoute_ShouldReturn404(string method)
    {
        var otherCourseId = await SeedOwnedEmptyCourseAsync();

        var response = await SendChapterMutationAsync(method, otherCourseId, _chapter1Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChapterMutation_WithoutAuth_ShouldReturn401()
    {
        var response = await _factory.CreateAnonymousClient()
            .PutAsJsonAsync($"/api/courses/{_courseId}/chapters/{_chapter1Id}", new UpdateChapterDto { Title = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteChapter_ShouldLeaveNoOrphanedContentAndReindex()
    {
        var (theoryContentId, videoContentId, quizId) = await GetContentIdsAsync(_chapter1Id);

        var response = await _client.DeleteAsync($"/api/courses/{_courseId}/chapters/{_chapter1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        (await db.TheoryContents.AnyAsync(t => t.Id == theoryContentId)).Should().BeFalse();
        (await db.VideoContents.AnyAsync(v => v.Id == videoContentId)).Should().BeFalse();
        (await db.Quizzes.AnyAsync(q => q.Id == quizId)).Should().BeFalse();
        (await db.QuizQuestions.AnyAsync(q => q.QuizId == quizId)).Should().BeFalse();
        (await db.Subchapters.AnyAsync(s => s.ChapterId == _chapter1Id)).Should().BeFalse();
        (await db.Chapters.SingleAsync(c => c.Id == _chapter2Id)).OrderIndex.Should().Be(1);
    }

    [Fact]
    public async Task ReorderChapter_ShouldSwapIndexes()
    {
        var response = await _client.PatchAsJsonAsync($"/api/courses/{_courseId}/chapters/{_chapter2Id}/order", 1);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        (await db.Chapters.SingleAsync(c => c.Id == _chapter1Id)).OrderIndex.Should().Be(2);
        (await db.Chapters.SingleAsync(c => c.Id == _chapter2Id)).OrderIndex.Should().Be(1);
    }

    #endregion

    #region Subchapters

    [Fact]
    public async Task RenameSubchapter_AsOwner_ShouldReturnUpdatedSubchapter()
    {
        var response = await _client.PutAsJsonAsync($"/api/chapters/{_chapter1Id}/subchapters/{_subchapter1Id}", new UpdateSubchapterDto { Title = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subchapter = await response.Content.ReadFromJsonAsync<SubchapterResponseDto>();
        subchapter!.Title.Should().Be("Renamed");
        subchapter.BlocksCount.Should().Be(3);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task SubchapterMutation_AsAnotherTeacher_ShouldReturn403(string method)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RegisterAsync("Teacher"));

        var response = await SendSubchapterMutationAsync(method, _chapter1Id, _subchapter1Id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task SubchapterMutation_UnderAnotherChapterRoute_ShouldReturn404(string method)
    {
        var response = await SendSubchapterMutationAsync(method, _chapter2Id, _subchapter1Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSubchapter_ShouldLeaveNoOrphanedContentAndReindex()
    {
        var (theoryContentId, videoContentId, quizId) = await GetContentIdsAsync(_chapter1Id);

        var response = await _client.DeleteAsync($"/api/chapters/{_chapter1Id}/subchapters/{_subchapter1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NewDbContext();
        (await db.TheoryContents.AnyAsync(t => t.Id == theoryContentId)).Should().BeFalse();
        (await db.VideoContents.AnyAsync(v => v.Id == videoContentId)).Should().BeFalse();
        (await db.Quizzes.AnyAsync(q => q.Id == quizId)).Should().BeFalse();
        (await db.CourseBlocks.AnyAsync(b => b.SubchapterId == _subchapter1Id)).Should().BeFalse();
        (await db.Subchapters.SingleAsync(s => s.Id == _subchapter2Id)).OrderIndex.Should().Be(1);
    }

    [Fact]
    public async Task DeleteSubchapter_PublishedCourse_ShouldReturn400()
    {
        await using (var db = NewDbContext())
        {
            await db.Courses.Where(c => c.Id == _courseId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Published));
        }

        var response = await _client.DeleteAsync($"/api/chapters/{_chapter1Id}/subchapters/{_subchapter1Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Helpers

    private Task<HttpResponseMessage> SendChapterMutationAsync(string method, Guid courseId, Guid chapterId)
    {
        var url = $"/api/courses/{courseId}/chapters/{chapterId}";
        return method switch
        {
            "PUT" => _client.PutAsJsonAsync(url, new UpdateChapterDto { Title = "x" }),
            "PATCH" => _client.PatchAsJsonAsync($"{url}/order", 1),
            "POST" => _client.PostAsJsonAsync($"/api/courses/{courseId}/chapters", new CreateChapterDto { Title = "x" }),
            _ => _client.DeleteAsync(url)
        };
    }

    private Task<HttpResponseMessage> SendSubchapterMutationAsync(string method, Guid chapterId, Guid subchapterId)
    {
        var url = $"/api/chapters/{chapterId}/subchapters/{subchapterId}";
        return method switch
        {
            "PUT" => _client.PutAsJsonAsync(url, new UpdateSubchapterDto { Title = "x" }),
            "PATCH" => _client.PatchAsJsonAsync($"{url}/order", 1),
            "POST" => _client.PostAsJsonAsync($"/api/chapters/{chapterId}/subchapters", new CreateSubchapterDto { Title = "x" }),
            _ => _client.DeleteAsync(url)
        };
    }

    private async Task<string> RegisterAsync(string role)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(role: role));
        return response.GetAccessToken();
    }

    private ApplicationDbContext NewDbContext()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
    }

    private async Task<(Guid TheoryContentId, Guid VideoContentId, Guid QuizId)> GetContentIdsAsync(Guid chapterId)
    {
        await using var db = NewDbContext();
        var blocks = await db.CourseBlocks.Where(b => b.Subchapter.ChapterId == chapterId).ToListAsync();
        return (blocks.Single(b => b.TheoryContentId != null).TheoryContentId!.Value,
            blocks.Single(b => b.VideoContentId != null).VideoContentId!.Value,
            blocks.Single(b => b.QuizId != null).QuizId!.Value);
    }

    private async Task<Guid> SeedOwnedEmptyCourseAsync()
    {
        await using var db = NewDbContext();
        var instructorId = await db.Courses.Where(c => c.Id == _courseId).Select(c => c.InstructorId).SingleAsync();
        var course = new Course { Title = "Other", Description = "d", InstructorId = instructorId, Instructor = null! };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course.Id;
    }

    // Owner's draft course: chapter 1 (subchapter 1 with theory, video and quiz blocks; subchapter 2), chapter 2
    private async Task SeedCourseAsync()
    {
        await using var db = NewDbContext();
        var owner = await db.Users.SingleAsync(u => u.Role == UserRole.Teacher);

        var course = new Course { Title = "Course", Description = "d", InstructorId = owner.Id, Instructor = owner };
        var chapter1 = new Chapter { Title = "Ch 1", OrderIndex = 1, Course = course };
        var chapter2 = new Chapter { Title = "Ch 2", OrderIndex = 2, Course = course };
        var subchapter1 = new Subchapter { Title = "Sub 1", OrderIndex = 1, Chapter = chapter1 };
        var subchapter2 = new Subchapter { Title = "Sub 2", OrderIndex = 2, Chapter = chapter1 };

        var quiz = new Quiz { Block = null! };
        quiz.Questions.Add(new QuizQuestion
        {
            Content = "Q", Type = QuestionType.SingleChoice, OrderIndex = 1, Quiz = quiz,
            Answers = { new QuizAnswer { Text = "A", IsCorrect = true, OrderIndex = 1, Question = null! } }
        });

        db.AddRange(course, chapter1, chapter2, subchapter1, subchapter2);
        db.CourseBlocks.AddRange(
            new CourseBlock
            {
                Title = "Theory", Type = BlockType.Theory, OrderIndex = 1, Subchapter = subchapter1,
                TheoryContent = new TheoryContent { Content = "t", Block = null! }
            },
            new CourseBlock
            {
                Title = "Video", Type = BlockType.Video, OrderIndex = 2, Subchapter = subchapter1,
                VideoContent = new VideoContent { VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", VideoId = "dQw4w9WgXcQ", Block = null! }
            },
            new CourseBlock { Title = "Quiz", Type = BlockType.Quiz, OrderIndex = 3, Subchapter = subchapter1, Quiz = quiz });

        await db.SaveChangesAsync();

        _courseId = course.Id;
        _chapter1Id = chapter1.Id;
        _chapter2Id = chapter2.Id;
        _subchapter1Id = subchapter1.Id;
        _subchapter2Id = subchapter2.Id;
    }

    #endregion
}
