using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateProblemDtoValidator : AbstractValidator<CreateProblemDto>
{
    public CreateProblemDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MinimumLength(20).WithMessage("Description must be at least 20 characters");

        RuleFor(x => x.Difficulty)
            .NotEmpty().WithMessage("Difficulty is required")
            .Must(BeValidDifficulty).WithMessage("Difficulty must be Easy, Medium, or Hard");

        RuleFor(x => x.TestCases)
            .NotEmpty().WithMessage("At least one test case is required")
            .Must(HaveAtLeastOnePublicTestCase).WithMessage("At least one test case must be public");

        RuleForEach(x => x.TestCases).SetValidator(new CreateTestCaseDtoValidator());
        RuleForEach(x => x.StarterCodes).SetValidator(new CreateStarterCodeDtoValidator());
    }

    private static bool BeValidDifficulty(string difficulty)
    {
        return difficulty is "Easy" or "Medium" or "Hard";
    }

    private static bool HaveAtLeastOnePublicTestCase(List<CreateTestCaseDto> testCases)
    {
        return testCases.Any(tc => tc.IsPublic);
    }
}