using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class ReorderTestCasesDtoValidator : AbstractValidator<ReorderTestCasesDto>
{
    public ReorderTestCasesDtoValidator()
    {
        RuleFor(x => x.TestCaseIds)
            .NotEmpty().WithMessage("Test case IDs list cannot be empty")
            .Must(ids => ids.Count == ids.Distinct().Count())
            .WithMessage("Duplicate test case IDs are not allowed");
    }
}
