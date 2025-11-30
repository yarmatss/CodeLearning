using CodeLearning.Application.DTOs.Chapter;
using FluentValidation;

namespace CodeLearning.Application.Validators.Chapter;

public class CreateChapterDtoValidator : AbstractValidator<CreateChapterDto>
{
    public CreateChapterDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters");
    }
}
