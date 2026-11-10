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
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one answer must be selected")
            .Must(ids => ids.Count <= 10).WithMessage("No more than 10 answers can be selected");

        RuleForEach(x => x.SelectedAnswerIds)
            .NotEmpty()
            .WithMessage("Answer ID cannot be empty");
    }
}
