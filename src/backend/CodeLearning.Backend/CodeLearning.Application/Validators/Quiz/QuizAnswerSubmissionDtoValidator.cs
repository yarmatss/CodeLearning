using CodeLearning.Application.DTOs.Quiz;
using FluentValidation;

namespace CodeLearning.Application.Validators.Quiz;

public class QuizAnswerSubmissionDtoValidator : AbstractValidator<QuizAnswerSubmissionDto>
{
    public QuizAnswerSubmissionDtoValidator()
    {
        RuleFor(x => x.QuestionId)
            .NotEmpty()
            .WithMessage("Question ID is required");

        RuleFor(x => x.SelectedAnswerIds)
            .NotEmpty()
            .WithMessage("At least one answer must be selected");

        RuleForEach(x => x.SelectedAnswerIds)
            .NotEmpty()
            .WithMessage("Answer ID cannot be empty");
    }
}
