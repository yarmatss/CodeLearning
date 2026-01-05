using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Extensions;

public static class QuizExtensions
{
    public static QuizDto? ToDto(this Quiz? quiz)
    {
        if (quiz is null) return null;

        return new QuizDto
        {
            Id = quiz.Id,
            Questions = quiz.Questions
                .OrderBy(q => q.OrderIndex)
                .Select(q => q.ToDto())
                .ToList()
        };
    }

    public static QuizQuestionDto ToDto(this QuizQuestion question)
    {
        return new QuizQuestionDto
        {
            Id = question.Id,
            Content = question.Content,
            Type = question.Type.ToString(),
            Points = question.Points,
            OrderIndex = question.OrderIndex,
            Answers = question.Answers
                .OrderBy(a => a.OrderIndex)
                .Select(a => a.ToDto())
                .ToList()
        };
    }

    public static QuizAnswerDto ToDto(this QuizAnswer answer)
    {
        return new QuizAnswerDto
        {
            Id = answer.Id,
            Text = answer.Text,
            OrderIndex = answer.OrderIndex
            // IsCorrect is not exposed to students
        };
    }
}
