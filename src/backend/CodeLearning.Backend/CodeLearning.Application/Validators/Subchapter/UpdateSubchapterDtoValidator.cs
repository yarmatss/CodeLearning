using CodeLearning.Application.DTOs.Subchapter;
using FluentValidation;

namespace CodeLearning.Application.Validators.Subchapter;

public class UpdateSubchapterDtoValidator : AbstractValidator<UpdateSubchapterDto>
{
    public UpdateSubchapterDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters");
    }
}
