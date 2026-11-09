using CodeLearning.Application.DTOs.Quiz;
using CodeLearning.Application.Exceptions;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Services;
using CodeLearning.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Tests.Unit.Services;

public class QuizServiceTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly QuizService _quizService;
    private readonly UserManager<User> _userManager;
    private User _testStudent = null!;
    private User _testTeacher = null!;
    private Course _testCourse = null!;
    private Quiz _testQuiz = null!;
    private QuizQuestion _question1 = null!;
    private QuizQuestion _question2 = null!;
    private QuizAnswer _q1Answer1 = null!;
    private QuizAnswer _q1Answer2 = null!;
    private QuizAnswer _q2Answer1 = null!;
    private QuizAnswer _q2Answer2 = null!;

    public QuizServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _quizService = new QuizService(_fixture.DbContext);

        var userStore = new UserStore<User, IdentityRole<Guid>, ApplicationDbContext, Guid>(_fixture.DbContext);

        _userManager = new UserManager<User>(
            userStore,
            null!,
            new PasswordHasher<User>(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SubmitQuizAsync_ValidSingleChoiceAnswers_ReturnsCorrectScore()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        // Act
        var result = await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testQuiz.Id, result.QuizId);
        Assert.Equal(2, result.Score);
        Assert.Equal(2, result.MaxScore);
        Assert.Equal(100.0, result.Percentage);
        Assert.Equal(2, result.QuestionResults.Count);
        Assert.All(result.QuestionResults, qr => Assert.True(qr.IsCorrect));
    }

    [Fact]
    public async Task SubmitQuizAsync_PartiallyCorrectAnswers_ReturnsPartialScore()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer2.Id }
                }
            }
        };

        // Act
        var result = await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Assert
        Assert.Equal(1, result.Score);
        Assert.Equal(2, result.MaxScore);
        Assert.Equal(50.0, result.Percentage);
        Assert.True(result.QuestionResults[0].IsCorrect);
        Assert.False(result.QuestionResults[1].IsCorrect);
    }

    [Fact]
    public async Task SubmitQuizAsync_AllWrongAnswers_ReturnsZeroScore()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer2.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer2.Id }
                }
            }
        };

        // Act
        var result = await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Assert
        Assert.Equal(0, result.Score);
        Assert.Equal(2, result.MaxScore);
        Assert.Equal(0.0, result.Percentage);
        Assert.All(result.QuestionResults, qr => Assert.False(qr.IsCorrect));
    }

    [Fact]
    public async Task SubmitQuizAsync_SecondAttempt_ThrowsBusinessRuleException()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));

        Assert.Contains("already attempted", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_AfterUnenrollAndReenroll_StillRejectsSecondAttempt()
    {
        // Arrange
        var enrollmentService = new EnrollmentService(_fixture.DbContext);
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer2.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer2.Id }
                }
            }
        };

        await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Act - leaving and rejoining the course must not reset the one-attempt rule
        await enrollmentService.UnenrollAsync(_testCourse.Id, _testStudent.Id);
        await enrollmentService.EnrollAsync(_testCourse.Id, _testStudent.Id);

        // Assert
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));
        Assert.Contains("already attempted", exception.Message);

        await using var verifyContext = _fixture.CreateDbContext();
        var blockProgress = await verifyContext.StudentBlockProgresses
            .SingleAsync(bp => bp.StudentId == _testStudent.Id && bp.BlockId == _testQuiz.Block.Id);
        Assert.True(blockProgress.IsCompleted);
    }

    [Fact]
    public async Task SubmitQuizAsync_NotEnrolled_ThrowsForbiddenException()
    {
        // Arrange
        var unenrolledStudent = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Not",
            LastName = "Enrolled",
            Email = "notenrolled@test.com",
            UserName = "notenrolled@test.com",
            Role = UserRole.Student
        };
        await _userManager.CreateAsync(unenrolledStudent);
        await _fixture.DbContext.SaveChangesAsync();

        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, unenrolledStudent.Id));

        Assert.Contains("must be enrolled", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_WrongNumberOfAnswers_ThrowsBusinessRuleException()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                }
                // Missing answer for question 2
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));

        Assert.Contains("Expected 2 answers", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_MultipleAnswersForSingleChoice_ThrowsBusinessRuleException()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id, _q1Answer2.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));

        Assert.Contains("single choice", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_InvalidAnswerId_ThrowsBusinessRuleException()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { Guid.NewGuid() }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));

        Assert.Contains("Invalid answer IDs", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_CompletesBlockProgress()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        // Act
        await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Assert
        await using var verifyContext = _fixture.CreateDbContext();
        var blockProgress = await verifyContext.StudentBlockProgresses
            .FirstOrDefaultAsync(bp => bp.StudentId == _testStudent.Id && bp.Block.QuizId == _testQuiz.Id);

        Assert.NotNull(blockProgress);
        Assert.True(blockProgress.IsCompleted);
        Assert.NotNull(blockProgress.CompletedAt);
    }

    [Fact]
    public async Task GetQuizAttemptAsync_ExistingAttempt_ReturnsResult()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer2.Id }
                }
            }
        };

        await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Act
        var result = await _quizService.GetQuizAttemptAsync(_testQuiz.Id, _testStudent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testQuiz.Id, result.QuizId);
        Assert.Equal(1, result.Score);
        Assert.Equal(2, result.MaxScore);
        Assert.Equal(50.0, result.Percentage);
    }

    [Fact]
    public async Task GetQuizAttemptAsync_NoAttempt_ReturnsNull()
    {
        // Act
        var result = await _quizService.GetQuizAttemptAsync(_testQuiz.Id, _testStudent.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetQuizAttemptAsync_IncludesAnswerFeedback()
    {
        // Arrange
        var dto = new SubmitQuizDto
        {
            Answers = new List<QuizAnswerSubmissionDto>
            {
                new()
                {
                    QuestionId = _question1.Id,
                    SelectedAnswerIds = new List<Guid> { _q1Answer1.Id }
                },
                new()
                {
                    QuestionId = _question2.Id,
                    SelectedAnswerIds = new List<Guid> { _q2Answer1.Id }
                }
            }
        };

        await _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id);

        // Act
        var result = await _quizService.GetQuizAttemptAsync(_testQuiz.Id, _testStudent.Id);

        // Assert
        Assert.NotNull(result);

        foreach (var questionResult in result.QuestionResults)
        {
            Assert.NotEmpty(questionResult.Answers);
            Assert.All(questionResult.Answers, answer =>
            {
                Assert.NotEqual(Guid.Empty, answer.AnswerId);
                Assert.NotNull(answer.Text);
            });
        }
    }

    [Fact]
    public async Task SubmitQuizAsync_MismatchedQuestionIds_ThrowsBusinessRuleException()
    {
        var dto = Answers((_question1.Id, _q1Answer1.Id), (Guid.NewGuid(), _q2Answer1.Id));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));

        Assert.Contains("do not match", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_SameQuestionAnsweredTwice_ThrowsBusinessRuleException()
    {
        var dto = Answers((_question1.Id, _q1Answer1.Id), (_question1.Id, _q1Answer2.Id));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _quizService.SubmitQuizAsync(_testQuiz.Id, dto, _testStudent.Id));

        Assert.Contains("do not match", exception.Message);
    }

    [Fact]
    public async Task SubmitQuizAsync_MultipleChoice_ScoresOnlyTheExactSetOfCorrectAnswers()
    {
        // Arrange: question 2 becomes multiple choice with two correct answers out of three
        var extraCorrect = new QuizAnswer
        {
            Id = Guid.NewGuid(), Text = "3+3", IsCorrect = true, OrderIndex = 3,
            QuestionId = _question2.Id, Question = _question2
        };
        _question2.Type = QuestionType.MultipleChoice;
        _question2.Points = 3;
        _fixture.DbContext.QuizAnswers.Add(extraCorrect);
        await _fixture.DbContext.SaveChangesAsync();

        // Act: a subset of the correct answers earns nothing
        var subset = await _quizService.SubmitQuizAsync(_testQuiz.Id,
            new SubmitQuizDto
            {
                Answers =
                [
                    new() { QuestionId = _question1.Id, SelectedAnswerIds = [_q1Answer1.Id] },
                    new() { QuestionId = _question2.Id, SelectedAnswerIds = [_q2Answer1.Id, _q2Answer1.Id] }
                ]
            },
            _testStudent.Id);

        // Assert
        Assert.Equal(1, subset.Score);
        Assert.Equal(4, subset.MaxScore);
        Assert.Equal(25.0, subset.Percentage);
        Assert.False(subset.QuestionResults.Single(r => r.QuestionId == _question2.Id).IsCorrect);

        // A second student selecting exactly the correct set gets the points
        var other = await CreateEnrolledStudentAsync();
        var exact = await _quizService.SubmitQuizAsync(_testQuiz.Id,
            Answers((_question1.Id, _q1Answer2.Id), (_question2.Id, _q2Answer1.Id), (_question2.Id, extraCorrect.Id), merge: true),
            other.Id);

        Assert.Equal(3, exact.Score);
        Assert.Equal(75.0, exact.Percentage);

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.StudentQuizAttempts.SingleAsync(a => a.StudentId == other.Id);
        Assert.Equal(75, stored.Score); // the stored score is the percentage
    }

    [Fact]
    public async Task SubmitQuizAsync_ResultIncludesExplanation()
    {
        var result = await _quizService.SubmitQuizAsync(_testQuiz.Id,
            Answers((_question1.Id, _q1Answer1.Id), (_question2.Id, _q2Answer1.Id)), _testStudent.Id);

        Assert.Equal("Basic math", result.QuestionResults[0].Explanation);
    }

    [Fact]
    public async Task SubmitQuizAsync_SetsCurrentBlockToNextIncompleteBlock()
    {
        // Arrange: a theory block after the quiz
        var subchapterId = _testQuiz.Block.SubchapterId;
        var nextBlock = new CourseBlock
        {
            Id = Guid.NewGuid(), Title = "After quiz", Type = BlockType.Theory, OrderIndex = 2,
            SubchapterId = subchapterId, Subchapter = _testQuiz.Block.Subchapter
        };
        _fixture.DbContext.CourseBlocks.Add(nextBlock);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        await _quizService.SubmitQuizAsync(_testQuiz.Id,
            Answers((_question1.Id, _q1Answer2.Id), (_question2.Id, _q2Answer2.Id)), _testStudent.Id);

        // Assert: any attempt completes the quiz block, so the bookmark moves past it
        await using var verifyContext = _fixture.CreateDbContext();
        var enrollment = await verifyContext.StudentCourseProgresses
            .SingleAsync(p => p.StudentId == _testStudent.Id && p.CourseId == _testCourse.Id);
        Assert.Equal(nextBlock.Id, enrollment.CurrentBlockId);
    }

    [Fact]
    public async Task GetQuizAttemptAsync_AfterQuizEdited_KeepsStoredPercentage()
    {
        // Arrange: 50% attempt, then the quiz is edited (as possible while the course is unpublished)
        await _quizService.SubmitQuizAsync(_testQuiz.Id,
            Answers((_question1.Id, _q1Answer1.Id), (_question2.Id, _q2Answer2.Id)), _testStudent.Id);

        var replacement = new QuizQuestion
        {
            Id = Guid.NewGuid(), Content = "New", Type = QuestionType.SingleChoice, OrderIndex = 3, Points = 1,
            QuizId = _testQuiz.Id, Quiz = _testQuiz,
            Answers = { new QuizAnswer { Id = Guid.NewGuid(), Text = "x", IsCorrect = true, OrderIndex = 1, Question = null! } }
        };
        _fixture.DbContext.QuizQuestions.Remove(_question2);
        _fixture.DbContext.QuizQuestions.Add(replacement);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await _quizService.GetQuizAttemptAsync(_testQuiz.Id, _testStudent.Id);

        // Assert: not silently re-graded against the edited questions
        Assert.NotNull(result);
        Assert.Equal(50.0, result.Percentage);
        Assert.Equal(1, result.Score);
        Assert.Equal(2, result.MaxScore);
    }

    private static SubmitQuizDto Answers(params (Guid QuestionId, Guid AnswerId)[] answers) =>
        Answers(answers, merge: false);

    private static SubmitQuizDto Answers((Guid QuestionId, Guid AnswerId) a, (Guid QuestionId, Guid AnswerId) b,
        (Guid QuestionId, Guid AnswerId) c, bool merge) =>
        Answers([a, b, c], merge);

    // merge groups answers of the same question into one submission entry (multiple choice)
    private static SubmitQuizDto Answers((Guid QuestionId, Guid AnswerId)[] answers, bool merge) => new()
    {
        Answers = merge
            ? answers.GroupBy(a => a.QuestionId)
                .Select(g => new QuizAnswerSubmissionDto { QuestionId = g.Key, SelectedAnswerIds = g.Select(x => x.AnswerId).ToList() })
                .ToList()
            : answers
                .Select(a => new QuizAnswerSubmissionDto { QuestionId = a.QuestionId, SelectedAnswerIds = [a.AnswerId] })
                .ToList()
    };

    private async Task<User> CreateEnrolledStudentAsync()
    {
        var student = new User
        {
            Id = Guid.NewGuid(), FirstName = "Other", LastName = "Student",
            Email = "other@test.com", UserName = "other@test.com", Role = UserRole.Student
        };
        await _userManager.CreateAsync(student);

        _fixture.DbContext.StudentCourseProgresses.Add(new StudentCourseProgress
        {
            StudentId = student.Id, Student = student, CourseId = _testCourse.Id, Course = _testCourse,
            EnrolledAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow
        });
        await _fixture.DbContext.SaveChangesAsync();
        return student;
    }

    private async Task SeedTestDataAsync()
    {
        _testTeacher = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Teacher",
            LastName = "Test",
            Email = "teacher@test.com",
            UserName = "teacher@test.com",
            Role = UserRole.Teacher
        };

        _testStudent = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Student",
            LastName = "Test",
            Email = "student@test.com",
            UserName = "student@test.com",
            Role = UserRole.Student
        };

        await _userManager.CreateAsync(_testTeacher);
        await _userManager.CreateAsync(_testStudent);

        _testCourse = new Course
        {
            Id = Guid.NewGuid(),
            Title = "Test Course",
            Description = "Test Description",
            Status = CourseStatus.Published,
            InstructorId = _testTeacher.Id,
            Instructor = _testTeacher
        };

        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            Title = "Test Chapter",
            OrderIndex = 1,
            CourseId = _testCourse.Id,
            Course = _testCourse
        };

        var subchapter = new Subchapter
        {
            Id = Guid.NewGuid(),
            Title = "Test Subchapter",
            OrderIndex = 1,
            ChapterId = chapter.Id,
            Chapter = chapter
        };

        _testQuiz = new Quiz
        {
            Id = Guid.NewGuid(),
            Block = null!
        };

        _question1 = new QuizQuestion
        {
            Id = Guid.NewGuid(),
            Content = "What is 2+2?",
            Type = QuestionType.SingleChoice,
            OrderIndex = 1,
            Explanation = "Basic math",
            QuizId = _testQuiz.Id,
            Quiz = _testQuiz
        };

        _q1Answer1 = new QuizAnswer
        {
            Id = Guid.NewGuid(),
            Text = "4",
            IsCorrect = true,
            OrderIndex = 1,
            QuestionId = _question1.Id,
            Question = _question1
        };

        _q1Answer2 = new QuizAnswer
        {
            Id = Guid.NewGuid(),
            Text = "3",
            IsCorrect = false,
            OrderIndex = 2,
            QuestionId = _question1.Id,
            Question = _question1
        };

        _question2 = new QuizQuestion
        {
            Id = Guid.NewGuid(),
            Content = "What is 3+3?",
            Type = QuestionType.SingleChoice,
            OrderIndex = 2,
            Explanation = "More math",
            QuizId = _testQuiz.Id,
            Quiz = _testQuiz
        };

        _q2Answer1 = new QuizAnswer
        {
            Id = Guid.NewGuid(),
            Text = "6",
            IsCorrect = true,
            OrderIndex = 1,
            QuestionId = _question2.Id,
            Question = _question2
        };

        _q2Answer2 = new QuizAnswer
        {
            Id = Guid.NewGuid(),
            Text = "5",
            IsCorrect = false,
            OrderIndex = 2,
            QuestionId = _question2.Id,
            Question = _question2
        };

        var block = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Test Quiz Block",
            Type = BlockType.Quiz,
            OrderIndex = 1,
            SubchapterId = subchapter.Id,
            Subchapter = subchapter,
            QuizId = _testQuiz.Id,
            Quiz = _testQuiz
        };

        _testQuiz.Block = block;

        var enrollment = new StudentCourseProgress
        {
            Id = Guid.NewGuid(),
            StudentId = _testStudent.Id,
            Student = _testStudent,
            CourseId = _testCourse.Id,
            Course = _testCourse,
            EnrolledAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        _fixture.DbContext.Courses.Add(_testCourse);
        _fixture.DbContext.Chapters.Add(chapter);
        _fixture.DbContext.Subchapters.Add(subchapter);
        _fixture.DbContext.Quizzes.Add(_testQuiz);
        _fixture.DbContext.QuizQuestions.AddRange(_question1, _question2);
        _fixture.DbContext.QuizAnswers.AddRange(_q1Answer1, _q1Answer2, _q2Answer1, _q2Answer2);
        _fixture.DbContext.CourseBlocks.Add(block);
        _fixture.DbContext.StudentCourseProgresses.Add(enrollment);
        await _fixture.DbContext.SaveChangesAsync();
    }
}
