using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class CreateTestCaseDtoValidator : AbstractValidator<CreateTestCaseDto>
{
    public CreateTestCaseDtoValidator()
    {
        RuleFor(x => x.Input)
            .NotNull().WithMessage("Input is required");

        RuleFor(x => x.ExpectedOutput)
            .NotEmpty().WithMessage("Expected output is required");
    }
}
