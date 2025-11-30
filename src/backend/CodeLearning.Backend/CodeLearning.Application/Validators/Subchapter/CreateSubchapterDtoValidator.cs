using CodeLearning.Application.DTOs.Subchapter;
using FluentValidation;

namespace CodeLearning.Application.Validators.Subchapter;

public class CreateSubchapterDtoValidator : AbstractValidator<CreateSubchapterDto>
{
    public CreateSubchapterDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters");
    }
}
