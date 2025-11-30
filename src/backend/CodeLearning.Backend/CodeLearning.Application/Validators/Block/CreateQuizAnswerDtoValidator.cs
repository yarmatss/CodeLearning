using CodeLearning.Application.DTOs.Block;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class CreateQuizAnswerDtoValidator : AbstractValidator<CreateQuizAnswerDto>
{
    public CreateQuizAnswerDtoValidator()
    {
        RuleFor(x => x.AnswerText)
            .NotEmpty().WithMessage("Answer text is required")
            .MaximumLength(500).WithMessage("Answer text must not exceed 500 characters");
    }
}
