using CodeLearning.Application.DTOs.Block;
using FluentValidation;

namespace CodeLearning.Application.Validators.Block;

public class CreateQuizQuestionDtoValidator : AbstractValidator<CreateQuizQuestionDto>
{
    public CreateQuizQuestionDtoValidator()
    {
        RuleFor(x => x.QuestionText)
            .NotEmpty().WithMessage("Question text is required")
            .MaximumLength(1000).WithMessage("Question text must not exceed 1000 characters");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Question type is required")
            .Must(BeValidQuestionType).WithMessage("Invalid question type. Allowed: SingleChoice, MultipleChoice, TrueFalse");

        RuleFor(x => x.Points)
            .GreaterThan(0).WithMessage("Points must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Points must not exceed 100");

        RuleFor(x => x.Answers)
            .NotEmpty().WithMessage("Question must have at least one answer")
            .Must(answers => answers.Count >= 2).WithMessage("Question must have at least 2 answers")
            .Must(answers => answers.Count <= 10).WithMessage("Question cannot have more than 10 answers");

        RuleForEach(x => x.Answers)
            .SetValidator(new CreateQuizAnswerDtoValidator());

        RuleFor(x => x)
            .Must(HaveAtLeastOneCorrectAnswer).WithMessage("Question must have at least one correct answer")
            .When(x => x.Type == "SingleChoice")
            .Must(HaveExactlyOneCorrectAnswer).WithMessage("SingleChoice question must have exactly one correct answer");
    }

    private bool BeValidQuestionType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type is "SingleChoice" or "MultipleChoice" or "TrueFalse";
    }

    private bool HaveAtLeastOneCorrectAnswer(CreateQuizQuestionDto question)
    {
        return question.Answers.Any(a => a.IsCorrect);
    }

    private bool HaveExactlyOneCorrectAnswer(CreateQuizQuestionDto question)
    {
        return question.Answers.Count(a => a.IsCorrect) == 1;
    }
}
