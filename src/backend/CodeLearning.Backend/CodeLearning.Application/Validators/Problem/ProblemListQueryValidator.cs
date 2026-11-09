using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class ProblemListQueryValidator : AbstractValidator<ProblemListQuery>
{
    public ProblemListQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ProblemListQuery.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {ProblemListQuery.MaxPageSize}");

        RuleFor(x => x.Difficulty)
            .Must(d => string.IsNullOrEmpty(d) || ProblemValidationRules.IsValidDifficulty(d))
            .WithMessage("Difficulty must be Easy, Medium, or Hard");

        RuleFor(x => x.Search)
            .MaximumLength(200).WithMessage("Search must not exceed 200 characters");
    }
}
