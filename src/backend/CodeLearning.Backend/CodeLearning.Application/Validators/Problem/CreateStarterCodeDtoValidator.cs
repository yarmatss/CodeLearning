using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateStarterCodeDtoValidator : AbstractValidator<CreateStarterCodeDto>
{
    public CreateStarterCodeDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Starter code is required");

        RuleFor(x => x.LanguageId)
            .NotEmpty().WithMessage("Language is required");
    }
}
