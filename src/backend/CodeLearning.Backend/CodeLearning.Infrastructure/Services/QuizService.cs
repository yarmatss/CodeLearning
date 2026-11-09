using CodeLearning.Application.DTOs.Quiz;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Services.Learning;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

// Grading is all-or-nothing per question: a question earns its points only when the selected answers are
// exactly its correct answers. Any attempt completes the quiz block, whatever the score (one attempt per quiz).
// StudentQuizAttempt.Score stores the attempt's PERCENTAGE (0-100, rounded), not points.
public class QuizService(ApplicationDbContext context) : IQuizService
{
    public async Task<QuizAttemptResultDto> SubmitQuizAsync(Guid quizId, SubmitQuizDto dto, Guid studentId)
    {
        var quiz = await context.Quizzes
            .Include(q => q.Questions.OrderBy(qq => qq.OrderIndex))
                .ThenInclude(qq => qq.Answers)
            .Include(q => q.Block.Subchapter.Chapter.Course)
            .FirstOrDefaultAsync(q => q.Id == quizId)
            ?? throw new NotFoundException($"Quiz with ID {quizId} not found");

        var course = quiz.Block.Subchapter.Chapter.Course;

        var isEnrolled = await context.StudentCourseProgresses
            .AnyAsync(p => p.CourseId == course.Id && p.StudentId == studentId);

        if (!isEnrolled)
        {
            throw new ForbiddenException("You must be enrolled in this course to submit quiz");
        }

        if (course.Status != CourseStatus.Published)
        {
            throw new ForbiddenException("This course is not published at the moment");
        }

        var hasAttempt = await context.StudentQuizAttempts
            .AnyAsync(a => a.QuizId == quizId && a.StudentId == studentId);

        if (hasAttempt)
        {
            throw new BusinessRuleException("You have already attempted this quiz. Only one attempt is allowed.");
        }

        var selections = ValidateSubmission(quiz, dto);
        var (score, maxScore, questionResults) = Grade(quiz, selections);
        var percentage = ToPercentage(score, maxScore);
        var now = DateTimeOffset.UtcNow;

        var attempt = new StudentQuizAttempt
        {
            StudentId = studentId,
            Student = null!,
            QuizId = quizId,
            Quiz = quiz,
            Score = (int)Math.Round(percentage),
            Answers = selections
                .Select(s => new QuizAnswerData { QuestionId = s.Key, SelectedAnswerIds = s.Value })
                .ToList(),
            AttemptedAt = now
        };

        context.StudentQuizAttempts.Add(attempt);

        // The attempt and the block completion (with the enrollment's next block) commit together
        await ProgressWriter.MarkBlockCompletedAsync(context, studentId, course.Id, quiz.Block.Id);
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

    // Built from the answers stored with the attempt. If the quiz was edited after the attempt (possible
    // only while the course was unpublished), the stored percentage stays authoritative and is not re-graded
    public async Task<QuizAttemptResultDto?> GetQuizAttemptAsync(Guid quizId, Guid studentId)
    {
        var attempt = await context.StudentQuizAttempts
            .AsNoTracking()
            .Include(a => a.Quiz.Questions.OrderBy(qq => qq.OrderIndex))
                .ThenInclude(qq => qq.Answers)
            .FirstOrDefaultAsync(a => a.QuizId == quizId && a.StudentId == studentId);

        if (attempt == null)
        {
            return null;
        }

        var quiz = attempt.Quiz;
        var selections = attempt.Answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.First().SelectedAnswerIds.Distinct().ToList());

        var (score, maxScore, questionResults) = Grade(quiz, selections);
        var percentage = ToPercentage(score, maxScore);

        var questionsUnchanged = quiz.Questions.Select(q => q.Id).ToHashSet().SetEquals(selections.Keys);
        if (!questionsUnchanged)
        {
            percentage = attempt.Score;
            score = (int)Math.Round(attempt.Score * maxScore / 100.0);
        }

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

    private static double ToPercentage(int score, int maxScore) =>
        maxScore > 0 ? (double)score / maxScore * 100 : 0;

    // Returns the selected answer ids per question, de-duplicated
    private static Dictionary<Guid, List<Guid>> ValidateSubmission(Quiz quiz, SubmitQuizDto dto)
    {
        if (dto.Answers.Count != quiz.Questions.Count)
        {
            throw new BusinessRuleException(
                $"Expected {quiz.Questions.Count} answers but received {dto.Answers.Count}");
        }

        var questionIds = quiz.Questions.Select(q => q.Id).ToHashSet();
        var submittedQuestionIds = dto.Answers.Select(a => a.QuestionId).ToList();

        if (submittedQuestionIds.Distinct().Count() != submittedQuestionIds.Count
            || !questionIds.SetEquals(submittedQuestionIds))
        {
            throw new BusinessRuleException("Answer question IDs do not match quiz questions");
        }

        var selections = new Dictionary<Guid, List<Guid>>();

        foreach (var answer in dto.Answers)
        {
            var question = quiz.Questions.First(q => q.Id == answer.QuestionId);
            var selected = answer.SelectedAnswerIds.Distinct().ToList();

            if (selected.Count == 0)
            {
                throw new BusinessRuleException($"Question {question.Id} must have at least one answer selected");
            }

            var validAnswerIds = question.Answers.Select(a => a.Id).ToHashSet();

            if (selected.Any(id => !validAnswerIds.Contains(id)))
            {
                throw new BusinessRuleException($"Invalid answer IDs for question {question.Id}");
            }

            if (question.Type is QuestionType.SingleChoice or QuestionType.TrueFalse && selected.Count > 1)
            {
                throw new BusinessRuleException(
                    $"Question {question.Id} accepts a single choice but multiple answers were selected");
            }

            selections[question.Id] = selected;
        }

        return selections;
    }

    private static (int Score, int MaxScore, List<QuestionResultDto> Results) Grade(
        Quiz quiz,
        IReadOnlyDictionary<Guid, List<Guid>> selections)
    {
        var results = new List<QuestionResultDto>();
        var earnedPoints = 0;
        var maxPoints = quiz.Questions.Sum(q => q.Points);

        foreach (var question in quiz.Questions.OrderBy(q => q.OrderIndex))
        {
            var selected = selections.TryGetValue(question.Id, out var ids) ? ids : [];

            var correctAnswerIds = question.Answers.Where(a => a.IsCorrect).Select(a => a.Id).ToHashSet();
            var isCorrect = selected.Count > 0 && correctAnswerIds.SetEquals(selected);

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
                SelectedAnswerIds = selected,
                Answers = question.Answers
                    .OrderBy(a => a.OrderIndex)
                    .Select(a => new AnswerFeedbackDto
                    {
                        AnswerId = a.Id,
                        Text = a.Text,
                        IsCorrect = a.IsCorrect,
                        WasSelected = selected.Contains(a.Id)
                    }).ToList(),
                Explanation = question.Explanation
            });
        }

        return (earnedPoints, maxPoints, results);
    }
}
