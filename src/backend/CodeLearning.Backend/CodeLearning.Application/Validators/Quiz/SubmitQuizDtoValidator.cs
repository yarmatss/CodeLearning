using CodeLearning.Application.DTOs.Quiz;
using FluentValidation;

namespace CodeLearning.Application.Validators.Quiz;

public class SubmitQuizDtoValidator : AbstractValidator<SubmitQuizDto>
{
    public SubmitQuizDtoValidator()
    {
        RuleFor(x => x.Answers)
            .NotEmpty()
            .WithMessage("Quiz must have at least one answer");

        RuleForEach(x => x.Answers)
            .SetValidator(new QuizAnswerSubmissionDtoValidator());
    }
}
