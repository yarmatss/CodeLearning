using CodeLearning.Application.DTOs.Progress;
using CodeLearning.Application.DTOs.Quiz;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Services.Learning;
using System.Text.Json;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CodeLearning.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for ProgressController
/// Tests block completion and progress tracking
/// </summary>
public class ProgressControllerTests : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
        result.NextBlockId.Should().Be(_block2Id);
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
    public async Task CompleteBlock_NotEnrolled_ShouldReturn403()
    {
        // Create new student
        var newStudentToken = await GetStudentTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newStudentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        var response = await _factory.CreateAnonymousClient().PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompleteBlock_ProblemBlockWithoutAcceptedSubmission_ShouldReturn400()
    {
        var (blockId, problemId) = await AddProblemBlockAsync();
        await AddSubmissionAsync(problemId, SubmissionStatus.Completed, score: 50);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{blockId}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Pass all test cases");
    }

    [Fact]
    public async Task CompleteBlock_ProblemBlockWithAcceptedSubmission_ShouldReturn200()
    {
        var (blockId, problemId) = await AddProblemBlockAsync();
        await AddSubmissionAsync(problemId, SubmissionStatus.Completed, score: 100);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{blockId}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CompleteBlockResponseDto>();
        result!.BlockId.Should().Be(blockId);
    }

    [Fact]
    public async Task CompleteBlock_QuizBlockWithoutAttempt_ShouldReturn400()
    {
        var blockId = await AddQuizBlockAsync();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{blockId}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Submit the quiz");
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
    public async Task GetCourseProgress_NotEnrolled_ShouldReturn403()
    {
        var newStudentToken = await GetStudentTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newStudentToken);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    #region Access, CurrentBlockId And Server-Side Completion Tests

    [Fact]
    public async Task GetNextBlock_NotEnrolled_ShouldReturn403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetStudentTokenAsync());

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}/next-block");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CompleteBlock_UnpublishedCourse_ShouldReturn403()
    {
        await using (var db = NewDbContext())
        {
            await db.Courses.Where(c => c.Id == _courseId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Draft));
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCourseProgress_BlockType_ShouldUseBlockTypeEnumLikeBlockResponse()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await _client.GetAsync($"/api/progress/courses/{_courseId}");
        var json = await response.Content.ReadAsStringAsync();
        var progress = JsonSerializer.Deserialize<CourseProgressDto>(json, JsonSerializerOptions.Web);

        var blocks = progress!.Chapters.SelectMany(c => c.Subchapters).SelectMany(s => s.Blocks).ToList();
        blocks.Single(b => b.BlockId == _block1Id).Type.Should().Be(BlockType.Theory);
        blocks.Single(b => b.BlockId == _block3Id).Type.Should().Be(BlockType.Video);
        // No global string enum converter: BlockType is serialized as its number, as in BlockResponseDto
        json.Should().Contain("\"type\":1");
    }

    [Fact]
    public async Task SubmitQuiz_BeforeEarlierBlocks_ShouldPointCurrentBlockAtFirstIncompleteBlock()
    {
        var (quizId, questionId, correctAnswerId) = await AddAnsweredQuizBlockAsync(orderIndex: 4);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);

        var response = await SubmitQuizAsync(quizId, questionId, correctAnswerId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var progress = await _client.GetFromJsonAsync<CourseProgressDto>($"/api/progress/courses/{_courseId}");
        progress!.CurrentBlockId.Should().Be(_block1Id);
        progress.CompletedBlocksCount.Should().Be(1);
    }

    [Fact]
    public async Task SubmitQuiz_AsLastIncompleteBlock_ShouldClearCurrentBlockAndCompleteCourse()
    {
        var (quizId, questionId, correctAnswerId) = await AddAnsweredQuizBlockAsync(orderIndex: 4);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);
        await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null);
        await _client.PostAsync($"/api/progress/blocks/{_block2Id}/complete", null);
        await _client.PostAsync($"/api/progress/blocks/{_block3Id}/complete", null);

        var response = await SubmitQuizAsync(quizId, questionId, correctAnswerId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var progress = await _client.GetFromJsonAsync<CourseProgressDto>($"/api/progress/courses/{_courseId}");
        progress!.CurrentBlockId.Should().BeNull();
        progress.ProgressPercentage.Should().Be(100);
    }

    [Fact]
    public async Task CompleteBlock_ShouldMoveCurrentBlockToNextIncomplete()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);
        await _client.PostAsync($"/api/progress/blocks/{_block2Id}/complete", null);

        await using var db = NewDbContext();
        var studentId = await GetCurrentStudentIdAsync(db);
        var enrollment = await db.StudentCourseProgresses.SingleAsync(p => p.CourseId == _courseId && p.StudentId == studentId);
        enrollment.CurrentBlockId.Should().Be(_block1Id);
    }

    [Fact]
    public async Task Reenroll_ShouldRestoreSolvedProblemBlocks()
    {
        var (problemBlockId, problemId) = await AddProblemBlockAsync();
        await AddSubmissionAsync(problemId, SubmissionStatus.Completed, score: 100);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _studentToken);
        (await _client.PostAsync($"/api/progress/blocks/{problemBlockId}/complete", null)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/progress/blocks/{_block1Id}/complete", null)).EnsureSuccessStatusCode();

        (await _client.DeleteAsync($"/api/enrollments/courses/{_courseId}")).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/enrollments/courses/{_courseId}", null)).EnsureSuccessStatusCode();

        var progress = await _client.GetFromJsonAsync<CourseProgressDto>($"/api/progress/courses/{_courseId}");
        var blocks = progress!.Chapters.SelectMany(c => c.Subchapters).SelectMany(s => s.Blocks).ToList();
        blocks.Single(b => b.BlockId == problemBlockId).IsCompleted.Should().BeTrue();
        blocks.Single(b => b.BlockId == _block1Id).IsCompleted.Should().BeFalse(); // viewing is not restored
        progress.CurrentBlockId.Should().Be(_block1Id);
    }

    [Fact]
    public async Task CompleteSolvedProblemBlocks_ShouldCompleteBlocksOfEnrolledCoursesOnly()
    {
        var (problemBlockId, problemId) = await AddProblemBlockAsync();
        await AddSubmissionAsync(problemId, SubmissionStatus.Completed, score: 100);

        int completed;
        Guid studentId;
        await using (var db = NewDbContext())
        {
            studentId = await GetCurrentStudentIdAsync(db);
            completed = await ProgressWriter.CompleteSolvedProblemBlocksAsync(db, studentId, problemId);
        }

        completed.Should().Be(1);
        await using var verify = NewDbContext();
        (await verify.StudentBlockProgresses.SingleAsync(bp => bp.BlockId == problemBlockId && bp.StudentId == studentId))
            .IsCompleted.Should().BeTrue();

        // Calling again is a no-op, and a user who is not enrolled gets nothing
        await using var again = NewDbContext();
        (await ProgressWriter.CompleteSolvedProblemBlocksAsync(again, studentId, problemId)).Should().Be(0);
        (await ProgressWriter.CompleteSolvedProblemBlocksAsync(again, Guid.NewGuid(), problemId)).Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private CodeLearning.Infrastructure.Data.ApplicationDbContext NewDbContext()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider
            .GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>().Database.GetConnectionString();
        return new CodeLearning.Infrastructure.Data.ApplicationDbContext(
            new DbContextOptionsBuilder<CodeLearning.Infrastructure.Data.ApplicationDbContext>().UseNpgsql(connectionString).Options);
    }

    private Task<HttpResponseMessage> SubmitQuizAsync(Guid quizId, Guid questionId, Guid answerId) =>
        _client.PostAsJsonAsync($"/api/quizzes/{quizId}/submit", new SubmitQuizDto
        {
            Answers = [new QuizAnswerSubmissionDto { QuestionId = questionId, SelectedAnswerIds = [answerId] }]
        });

    private async Task<(Guid QuizId, Guid QuestionId, Guid CorrectAnswerId)> AddAnsweredQuizBlockAsync(int orderIndex)
    {
        await using var db = NewDbContext();
        var subchapterId = await db.CourseBlocks.Where(b => b.Id == _block1Id).Select(b => b.SubchapterId).FirstAsync();

        var quiz = new Quiz { Block = null! };
        var question = new QuizQuestion { Content = "2+2?", Type = QuestionType.SingleChoice, OrderIndex = 1, Quiz = quiz };
        var correct = new QuizAnswer { Text = "4", IsCorrect = true, OrderIndex = 1, Question = question };
        var wrong = new QuizAnswer { Text = "5", IsCorrect = false, OrderIndex = 2, Question = question };
        question.Answers.Add(correct);
        question.Answers.Add(wrong);
        quiz.Questions.Add(question);

        db.CourseBlocks.Add(new CourseBlock
        {
            Title = "Quiz", Type = BlockType.Quiz, OrderIndex = orderIndex,
            SubchapterId = subchapterId, Subchapter = null!, Quiz = quiz
        });
        await db.SaveChangesAsync();

        return (quiz.Id, question.Id, correct.Id);
    }

    private async Task<string> GetStudentTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Student");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        return response.GetAccessToken();
    }

    private async Task<string> GetTeacherTokenAsync()
    {
        var registerDto = TestDataBuilder.CreateValidRegisterDto(role: "Teacher");
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerDto);
        return response.GetAccessToken();
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

    private static async Task<Guid> GetCurrentStudentIdAsync(CodeLearning.Infrastructure.Data.ApplicationDbContext dbContext)
    {
        var student = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Student);
        return student.Id;
    }

    private async Task<(Guid BlockId, Guid ProblemId)> AddProblemBlockAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var subchapterId = await dbContext.CourseBlocks
            .Where(b => b.Id == _block1Id)
            .Select(b => b.SubchapterId)
            .FirstAsync();
        var teacher = await dbContext.Users.FirstAsync(u => u.Role == UserRole.Teacher);

        var problem = new Problem
        {
            Title = "Sum",
            Description = "Add two numbers",
            Difficulty = DifficultyLevel.Easy,
            AuthorId = teacher.Id,
            Author = teacher
        };

        var block = new CourseBlock
        {
            Title = "Problem Block",
            Type = BlockType.Problem,
            OrderIndex = 4,
            SubchapterId = subchapterId,
            Subchapter = null!,
            Problem = problem
        };

        dbContext.Problems.Add(problem);
        dbContext.CourseBlocks.Add(block);
        await dbContext.SaveChangesAsync();

        return (block.Id, problem.Id);
    }

    private async Task AddSubmissionAsync(Guid problemId, SubmissionStatus status, int score)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var studentId = await GetCurrentStudentIdAsync(dbContext);

        dbContext.Submissions.Add(new Submission
        {
            ProblemId = problemId,
            StudentId = studentId,
            LanguageId = PythonLanguageId,
            Code = "print(sum(map(int, input().split())))",
            Status = status,
            Score = score,
            Problem = null!,
            Student = null!,
            Language = null!
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> AddQuizBlockAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeLearning.Infrastructure.Data.ApplicationDbContext>();

        var subchapterId = await dbContext.CourseBlocks
            .Where(b => b.Id == _block1Id)
            .Select(b => b.SubchapterId)
            .FirstAsync();

        var quiz = new Quiz { Block = null! };
        var block = new CourseBlock
        {
            Title = "Quiz Block",
            Type = BlockType.Quiz,
            OrderIndex = 4,
            SubchapterId = subchapterId,
            Subchapter = null!,
            Quiz = quiz
        };
        quiz.Block = block;

        dbContext.Quizzes.Add(quiz);
        dbContext.CourseBlocks.Add(block);
        await dbContext.SaveChangesAsync();

        return block.Id;
    }

    #endregion
}
