using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Constants;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateProblemDtoValidator : AbstractValidator<CreateProblemDto>
{
    public CreateProblemDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        ProblemValidationRules.TitleRules(RuleFor(x => x.Title));
        ProblemValidationRules.DescriptionRules(RuleFor(x => x.Description));
        ProblemValidationRules.DifficultyRules(RuleFor(x => x.Difficulty));

        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required")
            .Must(tcs => ProblemValidationRules.HaveAtLeastOnePublicTestCase(tcs, tc => tc.IsPublic))
            .WithMessage("At least one test case must be public")
            .Must(ProblemValidationRules.WithinTestCaseLimit)
            .WithMessage($"A problem can have at most {TestCaseLimits.MaxTestCasesPerProblem} test cases");

        RuleForEach(x => x.TestCases)
            .NotNull().WithMessage("Test case must not be null")
            .SetValidator(new CreateTestCaseDtoValidator());

        RuleFor(x => x.StarterCodes)
            .NotEmpty().WithMessage("At least one starter code is required")
            .Must(scs => scs.Count <= ProblemValidationRules.MaxStarterCodes)
            .WithMessage($"A problem can have at most {ProblemValidationRules.MaxStarterCodes} starter codes")
            .Must(scs => scs.Where(sc => sc is not null).GroupBy(sc => sc.LanguageId).All(g => g.Count() == 1))
            .WithMessage("Each language may have only one starter code");

        RuleForEach(x => x.StarterCodes)
            .NotNull().WithMessage("Starter code must not be null")
            .SetValidator(new CreateStarterCodeDtoValidator());

        ProblemValidationRules.TagIdsRules(RuleFor(x => x.TagIds));
    }
}
