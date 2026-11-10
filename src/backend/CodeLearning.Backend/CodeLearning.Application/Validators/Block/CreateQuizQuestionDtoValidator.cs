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

        RuleFor(x => x.Explanation)
            .MaximumLength(2000).WithMessage("Explanation must not exceed 2000 characters");

        RuleFor(x => x.Answers)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Question must have at least one answer")
            .Must(answers => answers.Count >= 2).WithMessage("Question must have at least 2 answers")
            .Must(answers => answers.Count <= 10).WithMessage("Question cannot have more than 10 answers");

        RuleFor(x => x.Answers)
            .Must(answers => answers.Count == 2).WithMessage("TrueFalse question must have exactly 2 answers")
            .When(x => x.Type == "TrueFalse" && x.Answers is { Count: >= 2 });

        RuleForEach(x => x.Answers)
            .NotNull().WithMessage("Answer must not be null")
            .SetValidator(new CreateQuizAnswerDtoValidator());

        RuleFor(x => x)
            .Must(HaveAtLeastOneCorrectAnswer).WithMessage("Question must have at least one correct answer");

        RuleFor(x => x)
            .Must(HaveExactlyOneCorrectAnswer)
            .WithMessage(x => $"{x.Type} question must have exactly one correct answer")
            .When(x => x.Type is "SingleChoice" or "TrueFalse");
    }

    private static bool BeValidQuestionType(string? type) =>
        type is "SingleChoice" or "MultipleChoice" or "TrueFalse";

    // Null-safe: a null list or null entries are reported by the rules above, not thrown here
    private static int CorrectAnswerCount(CreateQuizQuestionDto question) =>
        question.Answers?.Count(a => a?.IsCorrect == true) ?? 0;

    private static bool HaveAtLeastOneCorrectAnswer(CreateQuizQuestionDto question) =>
        CorrectAnswerCount(question) >= 1;

    private static bool HaveExactlyOneCorrectAnswer(CreateQuizQuestionDto question) =>
        CorrectAnswerCount(question) == 1;
}
