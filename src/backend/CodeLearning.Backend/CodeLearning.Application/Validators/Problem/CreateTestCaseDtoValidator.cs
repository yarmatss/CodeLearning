using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Constants;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateTestCaseDtoValidator : AbstractValidator<CreateTestCaseDto>
{
    public CreateTestCaseDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Input)
            .NotNull().WithMessage("Input is required")
            .MaximumLength(TestCaseLimits.MaxInputLength)
            .WithMessage($"Input must not exceed {TestCaseLimits.MaxInputLength} characters")
            .WithoutNulCharacters();

        // Empty expected output is allowed: a program may legitimately print nothing
        RuleFor(x => x.ExpectedOutput)
            .NotNull().WithMessage("Expected output is required")
            .MaximumLength(TestCaseLimits.MaxExpectedOutputLength)
            .WithMessage($"Expected output must not exceed {TestCaseLimits.MaxExpectedOutputLength} characters")
            .WithoutNulCharacters();
    }
}
