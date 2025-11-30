using CodeLearning.Application.DTOs.Block;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class CreateQuizBlockDtoValidator : AbstractValidator<CreateQuizBlockDto>
{
    public CreateQuizBlockDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters");

        RuleFor(x => x.Questions)
            .NotEmpty().WithMessage("Quiz must have at least one question")
            .Must(questions => questions.Count <= 50).WithMessage("Quiz cannot have more than 50 questions");

        RuleForEach(x => x.Questions)
            .SetValidator(new CreateQuizQuestionDtoValidator());
    }
}
