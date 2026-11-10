using CodeLearning.Application.DTOs.Block;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class UpdateProblemBlockDtoValidator : AbstractValidator<UpdateProblemBlockDto>
{
    public UpdateProblemBlockDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");

        RuleFor(x => x.ProblemId)
            .NotEmpty().WithMessage("Problem ID is required");
    }
}
