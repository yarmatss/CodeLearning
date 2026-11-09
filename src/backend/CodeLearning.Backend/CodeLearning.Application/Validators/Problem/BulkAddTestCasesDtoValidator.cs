using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Constants;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class BulkAddTestCasesDtoValidator : AbstractValidator<BulkAddTestCasesDto>
{
    public BulkAddTestCasesDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required")
            .Must(tcs => ProblemValidationRules.HaveAtLeastOnePublicTestCase(tcs, tc => tc.IsPublic))
            .WithMessage("At least one test case must be public")
            .Must(ProblemValidationRules.WithinTestCaseLimit)
            .WithMessage($"A problem can have at most {TestCaseLimits.MaxTestCasesPerProblem} test cases");

        RuleForEach(x => x.TestCases)
            .NotNull().WithMessage("Test case must not be null")
            .SetValidator(new CreateTestCaseDtoValidator());
    }
}
