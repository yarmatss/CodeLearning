using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Constants;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class BulkAddTestCasesDtoValidator : AbstractValidator<BulkAddTestCasesDto>
{
    public BulkAddTestCasesDtoValidator()
    {
        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required")
            .Must(HaveAtLeastOnePublicTestCase).WithMessage("At least one test case must be public")
            .Must(testCases => testCases.Count <= TestCaseLimits.MaxTestCasesPerProblem)
            .WithMessage($"A problem can have at most {TestCaseLimits.MaxTestCasesPerProblem} test cases");

        RuleForEach(x => x.TestCases).SetValidator(new CreateTestCaseDtoValidator());
    }

    private static bool HaveAtLeastOnePublicTestCase(List<CreateTestCaseDto> testCases)
    {
        return testCases.Any(tc => tc.IsPublic);
    }
}
