using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class BulkAddTestCasesDtoValidator : AbstractValidator<BulkAddTestCasesDto>
{
    public BulkAddTestCasesDtoValidator()
    {
        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required")
            .Must(HaveAtLeastOnePublicTestCase).WithMessage("At least one test case must be public");

        RuleForEach(x => x.TestCases).SetValidator(new CreateTestCaseDtoValidator());
    }

    private static bool HaveAtLeastOnePublicTestCase(List<CreateTestCaseDto> testCases)
    {
        return testCases.Any(tc => tc.IsPublic);
    }
}
