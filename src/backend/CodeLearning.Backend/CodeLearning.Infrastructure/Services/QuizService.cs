using CodeLearning.Application.DTOs.Quiz;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class QuizService(ApplicationDbContext context) : IQuizService
{
    public async Task<QuizAttemptResultDto> SubmitQuizAsync(Guid quizId, SubmitQuizDto dto, Guid studentId)
    {
        var quiz = await context.Quizzes
            .Include(q => q.Questions.OrderBy(qq => qq.OrderIndex))
                .ThenInclude(qq => qq.Answers)
            .Include(q => q.Block)
                .ThenInclude(b => b.Subchapter)
                    .ThenInclude(s => s.Chapter)
            .FirstOrDefaultAsync(q => q.Id == quizId)
            ?? throw new KeyNotFoundException($"Quiz with ID {quizId} not found");

        var courseId = quiz.Block.Subchapter.Chapter.CourseId;

        var isEnrolled = await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == courseId && p.StudentId == studentId);

        if (!isEnrolled)
        {
            throw new InvalidOperationException("You must be enrolled in this course to submit quiz");
        }

        var existingAttempt = await context.StudentQuizAttempts
            .FirstOrDefaultAsync(a => a.QuizId == quizId && a.StudentId == studentId);

        if (existingAttempt != null)
        {
            throw new InvalidOperationException("You have already attempted this quiz. Only one attempt is allowed.");
        }

        ValidateSubmission(quiz, dto);

        var (score, maxScore, questionResults) = GradeQuiz(quiz, dto);
        var percentage = maxScore > 0 ? (double)score / maxScore * 100 : 0;

        var answersJson = dto.Answers.Select(a => new QuizAnswerData
        {
            QuestionId = a.QuestionId,
            SelectedAnswerIds = a.SelectedAnswerIds
        }).ToList();

        var attempt = new StudentQuizAttempt
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Student = await context.Users.FindAsync(studentId)
                ?? throw new InvalidOperationException($"Student {studentId} not found"),
            QuizId = quizId,
            Quiz = quiz,
            Score = (int)Math.Round(percentage),
            Answers = answersJson,
            AttemptedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.StudentQuizAttempts.Add(attempt);

        var blockProgress = await context.StudentBlockProgresses
            .FirstOrDefaultAsync(bp => bp.BlockId == quiz.Block.Id && bp.StudentId == studentId);

        if (blockProgress == null)
        {
            blockProgress = new StudentBlockProgress
            {
                StudentId = studentId,
                BlockId = quiz.Block.Id,
                IsCompleted = true,
                CompletedAt = DateTimeOffset.UtcNow,
                Student = null!,
                Block = null!
            };
            context.StudentBlockProgresses.Add(blockProgress);
        }
        else if (!blockProgress.IsCompleted)
        {
            blockProgress.IsCompleted = true;
            blockProgress.CompletedAt = DateTimeOffset.UtcNow;
        }

        var courseProgress = await context.StudentCourseProgresses
            .FirstOrDefaultAsync(p => p.CourseId == courseId && p.StudentId == studentId);

        if (courseProgress != null)
        {
            courseProgress.LastActivityAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync();

        return new QuizAttemptResultDto
        {
            AttemptId = attempt.Id,
            QuizId = quizId,
            Score = score,
            MaxScore = maxScore,
            Percentage = Math.Round(percentage, 2),
            AttemptedAt = attempt.AttemptedAt,
            QuestionResults = questionResults
        };
    }

    public async Task<QuizAttemptResultDto?> GetQuizAttemptAsync(Guid quizId, Guid studentId)
    {
        var attempt = await context.StudentQuizAttempts
            .Include(a => a.Quiz)
                .ThenInclude(q => q.Questions.OrderBy(qq => qq.OrderIndex))
                    .ThenInclude(qq => qq.Answers)
            .FirstOrDefaultAsync(a => a.QuizId == quizId && a.StudentId == studentId);

        if (attempt == null)
        {
            return null;
        }

        var quiz = attempt.Quiz;
        var maxScore = quiz.Questions.Sum(q => q.Points);

        var questionResults = new List<QuestionResultDto>();

        foreach (var question in quiz.Questions)
        {
            var studentAnswer = attempt.Answers.FirstOrDefault(a => a.QuestionId == question.Id);
            var selectedAnswerIds = studentAnswer?.SelectedAnswerIds ?? new List<Guid>();

            var correctAnswerIds = question.Answers
                .Where(a => a.IsCorrect)
                .Select(a => a.Id)
                .OrderBy(id => id)
                .ToList();

            var selectedSorted = selectedAnswerIds.OrderBy(id => id).ToList();
            var isCorrect = correctAnswerIds.SequenceEqual(selectedSorted);

            questionResults.Add(new QuestionResultDto
            {
                QuestionId = question.Id,
                QuestionContent = question.Content,
                QuestionType = question.Type.ToString(),
                IsCorrect = isCorrect,
                Points = question.Points,
                SelectedAnswerIds = selectedAnswerIds,
                Answers = question.Answers.Select(a => new AnswerFeedbackDto
                {
                    AnswerId = a.Id,
                    Text = a.Text,
                    IsCorrect = a.IsCorrect,
                    WasSelected = selectedAnswerIds.Contains(a.Id)
                }).ToList(),
                Explanation = question.Explanation
            });
        }

        var score = questionResults.Where(qr => qr.IsCorrect).Sum(qr => qr.Points);
        var percentage = maxScore > 0 ? (double)score / maxScore * 100 : 0;

        return new QuizAttemptResultDto
        {
            AttemptId = attempt.Id,
            QuizId = quizId,
            Score = score,
            MaxScore = maxScore,
            Percentage = Math.Round(percentage, 2),
            AttemptedAt = attempt.AttemptedAt,
            QuestionResults = questionResults
        };
    }

    private static void ValidateSubmission(Quiz quiz, SubmitQuizDto dto)
    {
        if (dto.Answers.Count != quiz.Questions.Count)
        {
            throw new InvalidOperationException(
                $"Expected {quiz.Questions.Count} answers but received {dto.Answers.Count}");
        }

        var questionIds = quiz.Questions.Select(q => q.Id).ToHashSet();
        var submittedQuestionIds = dto.Answers.Select(a => a.QuestionId).ToHashSet();

        if (!questionIds.SetEquals(submittedQuestionIds))
        {
            throw new InvalidOperationException("Answer question IDs do not match quiz questions");
        }

        foreach (var answer in dto.Answers)
        {
            var question = quiz.Questions.First(q => q.Id == answer.QuestionId);

            if (answer.SelectedAnswerIds.Count == 0)
            {
                throw new InvalidOperationException($"Question {question.Id} must have at least one answer selected");
            }

            var validAnswerIds = question.Answers.Select(a => a.Id).ToHashSet();
            
            if (answer.SelectedAnswerIds.Any(id => !validAnswerIds.Contains(id)))
            {
                throw new InvalidOperationException($"Invalid answer IDs for question {question.Id}");
            }

            if (question.Type == QuestionType.SingleChoice && answer.SelectedAnswerIds.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Question {question.Id} is single choice but multiple answers were selected");
            }
        }
    }

    private static (int Score, int MaxScore, List<QuestionResultDto> Results) GradeQuiz(
        Quiz quiz,
        SubmitQuizDto dto)
    {
        var results = new List<QuestionResultDto>();
        var earnedPoints = 0;
        var maxPoints = quiz.Questions.Sum(q => q.Points);

        foreach (var question in quiz.Questions)
        {
            var studentAnswer = dto.Answers.First(a => a.QuestionId == question.Id);
            
            var correctAnswerIds = question.Answers
                .Where(a => a.IsCorrect)
                .Select(a => a.Id)
                .OrderBy(id => id)
                .ToList();

            var selectedAnswerIds = studentAnswer.SelectedAnswerIds.OrderBy(id => id).ToList();
            
            var isCorrect = correctAnswerIds.SequenceEqual(selectedAnswerIds);

            if (isCorrect)
            {
                earnedPoints += question.Points;
            }

            results.Add(new QuestionResultDto
            {
                QuestionId = question.Id,
                QuestionContent = question.Content,
                QuestionType = question.Type.ToString(),
                IsCorrect = isCorrect,
                Points = question.Points,
                SelectedAnswerIds = studentAnswer.SelectedAnswerIds,
                Answers = question.Answers.Select(a => new AnswerFeedbackDto
                {
                    AnswerId = a.Id,
                    Text = a.Text,
                    IsCorrect = a.IsCorrect,
                    WasSelected = studentAnswer.SelectedAnswerIds.Contains(a.Id)
                }).ToList(),
                Explanation = question.Explanation
            });
        }

        return (earnedPoints, maxPoints, results);
    }
}
