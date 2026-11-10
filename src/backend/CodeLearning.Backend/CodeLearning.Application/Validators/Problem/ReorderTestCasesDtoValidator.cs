using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class ReorderTestCasesDtoValidator : AbstractValidator<ReorderTestCasesDto>
{
    public ReorderTestCasesDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.TestCaseIds)
            .NotEmpty().WithMessage("Test case IDs list cannot be empty")
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("Test case IDs must not be empty")
            .Must(ids => ids.Count == ids.Distinct().Count())
            .WithMessage("Duplicate test case IDs are not allowed");
    }
}
