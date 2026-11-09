using CodeLearning.Core.Constants;
using FluentValidation;

namespace CodeLearning.Application.Validators.Problem;

public static class ProblemValidationRules
{
    public const int MaxTitleLength = 200;
    public const int MinDescriptionLength = 20;
    public const int MaxDescriptionLength = 20_000;
    public const int MaxTagIds = 10;
    public const int MaxStarterCodes = 10;
    public const int MaxStarterCodeLength = 100_000;

    public static bool IsValidDifficulty(string? difficulty) =>
        difficulty is "Easy" or "Medium" or "Hard";

    // PostgreSQL text columns reject NUL characters, so such input would fail with a 500 on save
    public static IRuleBuilderOptions<T, string> WithoutNulCharacters<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(value => value is null || !value.Contains('\0'))
            .WithMessage("{PropertyName} must not contain NUL characters");

    public static void TitleRules<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Title is required")
            .MaximumLength(MaxTitleLength).WithMessage($"Title must not exceed {MaxTitleLength} characters")
            .WithoutNulCharacters();

    public static void DescriptionRules<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Description is required")
            .MinimumLength(MinDescriptionLength).WithMessage($"Description must be at least {MinDescriptionLength} characters")
            .MaximumLength(MaxDescriptionLength).WithMessage($"Description must not exceed {MaxDescriptionLength} characters")
            .WithoutNulCharacters();

    public static void DifficultyRules<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Difficulty is required")
            .Must(IsValidDifficulty).WithMessage("Difficulty must be Easy, Medium, or Hard");

    public static void TagIdsRules<T>(IRuleBuilder<T, List<Guid>> rule) =>
        rule.NotNull().WithMessage("Tag IDs must not be null")
            .Must(ids => ids is null || ids.Count <= MaxTagIds).WithMessage($"A problem can have at most {MaxTagIds} tags")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty)).WithMessage("Tag IDs must not be empty")
            .Must(ids => ids is null || ids.Count == ids.Distinct().Count()).WithMessage("Duplicate tag IDs are not allowed");

    public static bool HaveAtLeastOnePublicTestCase<TTestCase>(List<TTestCase>? testCases, Func<TTestCase, bool> isPublic)
        where TTestCase : class =>
        testCases is not null && testCases.Any(tc => tc is not null && isPublic(tc));

    public static bool WithinTestCaseLimit<TTestCase>(List<TTestCase>? testCases) =>
        testCases is null || testCases.Count <= TestCaseLimits.MaxTestCasesPerProblem;
}
