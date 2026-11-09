using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateStarterCodeDtoValidator : AbstractValidator<CreateStarterCodeDto>
{
    public CreateStarterCodeDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Starter code is required")
            .MaximumLength(ProblemValidationRules.MaxStarterCodeLength).WithMessage("Starter code cannot exceed 100KB")
            .WithoutNulCharacters();

        RuleFor(x => x.LanguageId)
            .NotEmpty().WithMessage("Language is required");
    }
}
