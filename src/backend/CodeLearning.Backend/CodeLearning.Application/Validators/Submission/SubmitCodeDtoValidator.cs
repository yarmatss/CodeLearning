using CodeLearning.Application.DTOs.Submission;
using FluentValidation;

namespace CodeLearning.Application.Validators.Submission;

public class SubmitCodeDtoValidator : AbstractValidator<SubmitCodeDto>
{
    public SubmitCodeDtoValidator()
    {
        RuleFor(x => x.ProblemId)
            .NotEmpty().WithMessage("Problem ID is required");

        RuleFor(x => x.LanguageId)
            .NotEmpty().WithMessage("Language ID is required");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required")
            .MaximumLength(100000).WithMessage("Code cannot exceed 100KB");
    }
}
