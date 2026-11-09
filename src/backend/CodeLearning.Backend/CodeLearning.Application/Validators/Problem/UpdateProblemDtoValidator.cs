using CodeLearning.Application.DTOs.Problem;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public class UpdateProblemDtoValidator : AbstractValidator<UpdateProblemDto>
{
    public UpdateProblemDtoValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        ProblemValidationRules.TitleRules(RuleFor(x => x.Title));
        ProblemValidationRules.DescriptionRules(RuleFor(x => x.Description));
        ProblemValidationRules.DifficultyRules(RuleFor(x => x.Difficulty));
        ProblemValidationRules.TagIdsRules(RuleFor(x => x.TagIds));
    }
}
