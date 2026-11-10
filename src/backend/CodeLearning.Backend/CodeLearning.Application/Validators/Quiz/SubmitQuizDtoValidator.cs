using CodeLearning.Application.DTOs.Quiz;
using FluentValidation;

namespace CodeLearning.Application.Validators.Quiz;

public class SubmitQuizDtoValidator : AbstractValidator<SubmitQuizDto>
{
    public SubmitQuizDtoValidator()
    {
        RuleFor(x => x.Answers)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Quiz must have at least one answer")
            .Must(answers => answers.Count <= 50).WithMessage("Quiz cannot have more than 50 answers")
            .Must(answers => answers.Where(a => a is not null).Select(a => a.QuestionId).Distinct().Count()
                             == answers.Count(a => a is not null))
            .WithMessage("Each question can be answered only once");

        RuleForEach(x => x.Answers)
            .NotNull().WithMessage("Answer must not be null")
            .SetValidator(new QuizAnswerSubmissionDtoValidator());
    }
}
