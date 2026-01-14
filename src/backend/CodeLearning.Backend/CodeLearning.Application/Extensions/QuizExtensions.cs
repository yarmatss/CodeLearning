using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Extensions;

public static class QuizExtensions
{
    public static QuizDto? ToDto(this Quiz? quiz, bool includeCorrectAnswers = false)
    {
        if (quiz is null) return null;

        return new QuizDto
        {
            Id = quiz.Id,
            Questions = quiz.Questions
                .OrderBy(q => q.OrderIndex)
                .Select(q => q.ToDto(includeCorrectAnswers))
                .ToList()
        };
    }

    public static QuizQuestionDto ToDto(this QuizQuestion question, bool includeCorrectAnswers = false)
    {
        return new QuizQuestionDto
        {
            Id = question.Id,
            Content = question.Content,
            Type = question.Type.ToString(),
            Points = question.Points,
            Explanation = question.Explanation,
            OrderIndex = question.OrderIndex,
            Answers = question.Answers
                .OrderBy(a => a.OrderIndex)
                .Select(a => a.ToDto(includeCorrectAnswers))
                .ToList()
        };
    }

    public static QuizAnswerDto ToDto(this QuizAnswer answer, bool includeCorrectAnswers = false)
    {
        return new QuizAnswerDto
        {
            Id = answer.Id,
            Text = answer.Text,
            OrderIndex = answer.OrderIndex,
            // Only include IsCorrect for instructors/admins
            IsCorrect = includeCorrectAnswers ? answer.IsCorrect : null
        };
    }
}
