using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Core.Constants;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateTestCaseDtoValidator : AbstractValidator<CreateTestCaseDto>
{
    public CreateTestCaseDtoValidator()
    {
        RuleFor(x => x.Input)
            .NotNull().WithMessage("Input is required")
            .MaximumLength(TestCaseLimits.MaxInputLength)
            .WithMessage($"Input must not exceed {TestCaseLimits.MaxInputLength} characters");

        RuleFor(x => x.ExpectedOutput)
            .NotEmpty().WithMessage("Expected output is required")
            .MaximumLength(TestCaseLimits.MaxExpectedOutputLength)
            .WithMessage($"Expected output must not exceed {TestCaseLimits.MaxExpectedOutputLength} characters");
    }
}
