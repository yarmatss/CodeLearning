using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Constants;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CodeLearning.Infrastructure.Services;

public class ProblemService(ApplicationDbContext context, IConfiguration configuration) : IProblemService
{
    private const string ModifyOwnProblemsOnlyMessage = "You can only modify your own problems";

    private const string LockedMessage =
        "This problem is used by a published course and can't be changed. Unpublish the course first.";

    // The Runner gives a run's tests only what is left of ExecutionSettings:MaxContainerRuntimeSeconds after
    // the compile budget and container overhead (SandboxTimeBudget). Tests beyond that budget are reported as
    // time limit exceeded even for correct code, so such problems are rejected here using the same calculation.
    private int MaxContainerRuntimeSeconds =>
        int.TryParse(configuration["ExecutionSettings:MaxContainerRuntimeSeconds"], out var seconds) && seconds > 0
            ? seconds
            : SandboxTimeBudget.DefaultMaxContainerRuntimeSeconds;

    public async Task<ProblemResponseDto> CreateProblemAsync(CreateProblemDto dto, Guid authorId)
    {
        var author = await context.Users.FindAsync(authorId)
            ?? throw new NotFoundException("Author not found");

        var languages = await GetValidStarterLanguagesAsync(dto.StarterCodes);
        EnsureFitsSandboxTimeBudget(dto.TestCases.Count, languages.Values);

        var problem = new Problem
        {
            Title = dto.Title,
            Description = dto.Description,
            Difficulty = Enum.Parse<DifficultyLevel>(dto.Difficulty),
            AuthorId = authorId,
            Author = author
        };

        context.Problems.Add(problem);

        context.TestCases.AddRange(dto.TestCases.Select((tc, index) => new TestCase
        {
            Input = tc.Input,
            ExpectedOutput = tc.ExpectedOutput,
            IsPublic = tc.IsPublic,
            OrderIndex = index + 1,
            ProblemId = problem.Id,
            Problem = problem
        }));

        context.StarterCodes.AddRange(dto.StarterCodes.Select(sc => new StarterCode
        {
            Code = sc.Code,
            LanguageId = sc.LanguageId,
            Language = languages[sc.LanguageId],
            ProblemId = problem.Id,
            Problem = problem
        }));

        await AddTagsAsync(problem, dto.TagIds);

        await context.SaveChangesAsync();

        return await MapToProblemResponseDto(problem, includeHiddenTestCases: true);
    }

    public async Task<ProblemResponseDto> GetProblemByIdAsync(Guid problemId, Guid userId)
    {
        var problem = await context.Problems
            .Include(p => p.Author)
            .Include(p => p.TestCases)
            .Include(p => p.StarterCodes)
                .ThenInclude(sc => sc.Language)
            .Include(p => p.ProblemTags)
                .ThenInclude(pt => pt.Tag)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new NotFoundException($"Problem with ID {problemId} not found");

        var isAuthor = problem.AuthorId == userId;

        if (!isAuthor && !await VisibleProblems(userId).AnyAsync(p => p.Id == problemId))
        {
            throw new ForbiddenException(
                "You can only view your own problems or problems in published courses you are enrolled in");
        }

        return await MapToProblemResponseDto(problem, includeHiddenTestCases: isAuthor);
    }

    public Task<PagedResult<ProblemListDto>> GetProblemsAsync(ProblemListQuery query, Guid userId) =>
        GetProblemPageAsync(VisibleProblems(userId), query);

    public Task<PagedResult<ProblemListDto>> GetMyProblemsAsync(ProblemListQuery query, Guid authorId) =>
        GetProblemPageAsync(context.Problems.Where(p => p.AuthorId == authorId), query);

    public async Task<ProblemResponseDto> UpdateProblemAsync(Guid problemId, UpdateProblemDto dto, Guid authorId)
    {
        var problem = await context.Problems
            .Include(p => p.Author)
            .Include(p => p.TestCases)
            .Include(p => p.StarterCodes)
                .ThenInclude(sc => sc.Language)
            .Include(p => p.ProblemTags)
                .ThenInclude(pt => pt.Tag)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new NotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new ForbiddenException("You can only update your own problems");
        }

        await EnsureNotLockedAsync(problemId);

        problem.Title = dto.Title;
        problem.Description = dto.Description;
        problem.Difficulty = Enum.Parse<DifficultyLevel>(dto.Difficulty);

        context.ProblemTags.RemoveRange(problem.ProblemTags);

        await AddTagsAsync(problem, dto.TagIds);

        await context.SaveChangesAsync();

        return await MapToProblemResponseDto(problem, includeHiddenTestCases: true);
    }

    public async Task DeleteProblemAsync(Guid problemId, Guid authorId)
    {
        var problem = await context.Problems.FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new NotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new ForbiddenException("You can only delete your own problems");
        }

        await EnsureNotLockedAsync(problemId);

        if (await context.CourseBlocks.AnyAsync(b => b.ProblemId == problemId))
        {
            throw new BusinessRuleException("Cannot delete problem that is used in courses. Remove it from courses first.");
        }

        if (await context.Submissions.AnyAsync(s => s.ProblemId == problemId))
        {
            throw new BusinessRuleException("Cannot delete problem that has submissions.");
        }

        context.Problems.Remove(problem);
        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<TagResponseDto>> GetAllTagsAsync()
    {
        return await context.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagResponseDto
            {
                Id = t.Id,
                Name = t.Name
            })
            .ToListAsync();
    }

    public Task<bool> IsUsedByPublishedCourseAsync(Guid problemId) =>
        context.CourseBlocks.AnyAsync(b =>
            b.ProblemId == problemId &&
            b.Subchapter.Chapter.Course.Status == CourseStatus.Published);

    public async Task<TestCaseResponseDto> AddTestCaseAsync(Guid problemId, CreateTestCaseDto dto, Guid authorId)
    {
        var problem = await GetOwnedUnlockedProblemAsync(problemId, authorId);

        var existing = await context.TestCases
            .Where(tc => tc.ProblemId == problemId)
            .Select(tc => tc.OrderIndex)
            .ToListAsync();

        EnsureTestCaseLimit(existing.Count + 1);
        await EnsureFitsSandboxTimeBudgetAsync(problemId, existing.Count + 1);

        var testCase = new TestCase
        {
            Input = dto.Input,
            ExpectedOutput = dto.ExpectedOutput,
            IsPublic = dto.IsPublic,
            OrderIndex = (existing.Count > 0 ? existing.Max() : 0) + 1,
            ProblemId = problemId,
            Problem = problem
        };

        context.TestCases.Add(testCase);
        await context.SaveChangesAsync();

        return MapToTestCaseDto(testCase);
    }

    public async Task<BulkAddTestCasesResult> AddTestCasesBulkAsync(Guid problemId, BulkAddTestCasesDto dto, Guid authorId)
    {
        var problem = await GetOwnedUnlockedProblemAsync(problemId, authorId);

        var existing = await context.TestCases
            .Where(tc => tc.ProblemId == problemId)
            .Select(tc => tc.OrderIndex)
            .ToListAsync();

        var resultingCount = existing.Count + dto.TestCases.Count;
        EnsureTestCaseLimit(resultingCount);
        await EnsureFitsSandboxTimeBudgetAsync(problemId, resultingCount);

        var maxOrder = existing.Count > 0 ? existing.Max() : 0;

        var newTestCases = dto.TestCases.Select((tc, index) => new TestCase
        {
            Input = tc.Input,
            ExpectedOutput = tc.ExpectedOutput,
            IsPublic = tc.IsPublic,
            OrderIndex = maxOrder + index + 1,
            ProblemId = problemId,
            Problem = problem
        }).ToList();

        context.TestCases.AddRange(newTestCases);
        await context.SaveChangesAsync();

        return new BulkAddTestCasesResult
        {
            Added = newTestCases.Count,
            TestCases = newTestCases.Select(MapToTestCaseDto).ToList()
        };
    }

    public async Task<TestCaseResponseDto> UpdateTestCaseAsync(Guid testCaseId, UpdateTestCaseDto dto, Guid authorId)
    {
        var testCase = await GetOwnedUnlockedTestCaseAsync(testCaseId, authorId);

        if (testCase.IsPublic && !dto.IsPublic)
        {
            var hasOtherPublicTests = await context.TestCases
                .AnyAsync(tc => tc.ProblemId == testCase.ProblemId && tc.Id != testCaseId && tc.IsPublic);

            if (!hasOtherPublicTests)
            {
                throw new BusinessRuleException("Cannot make the last public test case private");
            }
        }

        testCase.Input = dto.Input;
        testCase.ExpectedOutput = dto.ExpectedOutput;
        testCase.IsPublic = dto.IsPublic;

        await context.SaveChangesAsync();

        return MapToTestCaseDto(testCase);
    }

    public async Task ReorderTestCasesAsync(Guid problemId, ReorderTestCasesDto dto, Guid authorId)
    {
        await GetOwnedUnlockedProblemAsync(problemId, authorId);

        var testCases = await context.TestCases
            .Where(tc => tc.ProblemId == problemId)
            .ToDictionaryAsync(tc => tc.Id);

        if (dto.TestCaseIds.Count != testCases.Count)
        {
            throw new BusinessRuleException("All test cases must be included in reorder operation");
        }

        if (dto.TestCaseIds.Any(id => !testCases.ContainsKey(id)))
        {
            throw new BusinessRuleException("One or more test case IDs do not belong to this problem");
        }

        for (var i = 0; i < dto.TestCaseIds.Count; i++)
        {
            testCases[dto.TestCaseIds[i]].OrderIndex = i + 1;
        }

        await context.SaveChangesAsync();
    }

    public async Task DeleteTestCaseAsync(Guid testCaseId, Guid authorId)
    {
        var testCase = await GetOwnedUnlockedTestCaseAsync(testCaseId, authorId);

        var remaining = await context.TestCases
            .Where(tc => tc.ProblemId == testCase.ProblemId && tc.Id != testCaseId)
            .OrderBy(tc => tc.OrderIndex)
            .ToListAsync();

        if (testCase.IsPublic && !remaining.Any(tc => tc.IsPublic))
        {
            throw new BusinessRuleException("Cannot delete the last public test case");
        }

        // Close the gap so the remaining test cases stay numbered 1..n; saved in the same transaction
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].OrderIndex = i + 1;
        }

        context.TestCases.Remove(testCase);
        await context.SaveChangesAsync();
    }

    public async Task<StarterCodeResponseDto> AddStarterCodeAsync(Guid problemId, CreateStarterCodeDto dto, Guid authorId)
    {
        var problem = await GetOwnedUnlockedProblemAsync(problemId, authorId);

        var language = await context.Languages.FirstOrDefaultAsync(l => l.Id == dto.LanguageId)
            ?? throw new BusinessRuleException($"Unknown language ID: {dto.LanguageId}");

        if (!language.IsEnabled)
        {
            throw new BusinessRuleException($"Language {language.Name} is disabled");
        }

        if (await context.StarterCodes.AnyAsync(sc => sc.ProblemId == problemId && sc.LanguageId == dto.LanguageId))
        {
            throw new ProblemConflictException(
                $"Starter code for {language.Name} already exists. Delete it first to replace it.");
        }

        var testCaseCount = await context.TestCases.CountAsync(tc => tc.ProblemId == problemId);
        EnsureFitsSandboxTimeBudget(testCaseCount, [language]);

        var starterCode = new StarterCode
        {
            Code = dto.Code,
            LanguageId = dto.LanguageId,
            Language = language,
            ProblemId = problemId,
            Problem = problem
        };

        // A concurrent duplicate insert is rejected by the unique (ProblemId, LanguageId) index,
        // which the global exception handler turns into 409
        context.StarterCodes.Add(starterCode);
        await context.SaveChangesAsync();

        return new StarterCodeResponseDto
        {
            Id = starterCode.Id,
            Code = starterCode.Code,
            LanguageId = starterCode.LanguageId,
            LanguageName = language.Name
        };
    }

    public async Task DeleteStarterCodeAsync(Guid starterCodeId, Guid authorId)
    {
        var starterCode = await context.StarterCodes
            .Include(sc => sc.Problem)
            .FirstOrDefaultAsync(sc => sc.Id == starterCodeId)
            ?? throw new NotFoundException($"Starter code with ID {starterCodeId} not found");

        if (starterCode.Problem.AuthorId != authorId)
        {
            throw new ForbiddenException(ModifyOwnProblemsOnlyMessage);
        }

        await EnsureNotLockedAsync(starterCode.ProblemId);

        var hasOtherStarterCodes = await context.StarterCodes
            .AnyAsync(sc => sc.ProblemId == starterCode.ProblemId && sc.Id != starterCodeId);

        if (!hasOtherStarterCodes)
        {
            throw new BusinessRuleException("Cannot delete the last starter code; a problem needs at least one language");
        }

        context.StarterCodes.Remove(starterCode);
        await context.SaveChangesAsync();
    }

    // The author's problems, plus problems used by a published course the user is enrolled in
    private IQueryable<Problem> VisibleProblems(Guid userId) =>
        context.Problems.Where(p =>
            p.AuthorId == userId ||
            p.Blocks.Any(b =>
                b.Subchapter.Chapter.Course.Status == CourseStatus.Published &&
                b.Subchapter.Chapter.Course.StudentProgress.Any(sp => sp.StudentId == userId)));

    private static async Task<PagedResult<ProblemListDto>> GetProblemPageAsync(IQueryable<Problem> query, ProblemListQuery listQuery)
    {
        if (!string.IsNullOrEmpty(listQuery.Difficulty) &&
            Enum.TryParse<DifficultyLevel>(listQuery.Difficulty, out var difficultyLevel))
        {
            query = query.Where(p => p.Difficulty == difficultyLevel);
        }

        if (listQuery.TagId.HasValue)
        {
            var tagId = listQuery.TagId.Value;
            query = query.Where(p => p.ProblemTags.Any(pt => pt.TagId == tagId));
        }

        if (!string.IsNullOrWhiteSpace(listQuery.Search))
        {
            var pattern = $"%{EscapeLikePattern(listQuery.Search.Trim())}%";
            query = query.Where(p => EF.Functions.ILike(p.Title, pattern) ||
                                     EF.Functions.ILike(p.Description, pattern));
        }

        var totalCount = await query.CountAsync();

        const int previewLength = ProblemListDto.DescriptionPreviewLength;

        // Projected in SQL: counts and the description preview are computed by the database,
        // and no test case rows are loaded
        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((listQuery.Page - 1) * listQuery.PageSize)
            .Take(listQuery.PageSize)
            .Select(p => new
            {
                p.Id,
                p.Title,
                // Translated to SQL substring(); the span-based alternative CA1845 suggests cannot be translated
#pragma warning disable CA1845
                Description = p.Description.Length > previewLength
                    ? p.Description.Substring(0, previewLength) + "..."
                    : p.Description,
#pragma warning restore CA1845
                p.Difficulty,
                p.AuthorId,
                AuthorName = p.Author.FirstName + " " + p.Author.LastName,
                TestCasesCount = p.TestCases.Count,
                LanguagesCount = p.StarterCodes.Count,
                IsUsedByPublishedCourse = p.Blocks.Any(b =>
                    b.Subchapter.Chapter.Course.Status == CourseStatus.Published),
                Tags = p.ProblemTags
                    .OrderBy(pt => pt.Tag.Name)
                    .Select(pt => new TagResponseDto { Id = pt.Tag.Id, Name = pt.Tag.Name })
                    .ToList(),
                p.CreatedAt
            })
            .AsSplitQuery()
            .ToListAsync();

        return new PagedResult<ProblemListDto>
        {
            Items = rows.Select(r => new ProblemListDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Difficulty = r.Difficulty.ToString(),
                AuthorId = r.AuthorId,
                AuthorName = r.AuthorName,
                TestCasesCount = r.TestCasesCount,
                LanguagesCount = r.LanguagesCount,
                IsUsedByPublishedCourse = r.IsUsedByPublishedCourse,
                Tags = r.Tags,
                CreatedAt = r.CreatedAt
            }).ToList(),
            Page = listQuery.Page,
            PageSize = listQuery.PageSize,
            TotalCount = totalCount
        };
    }

    private async Task<Problem> GetOwnedUnlockedProblemAsync(Guid problemId, Guid authorId)
    {
        var problem = await context.Problems.FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new NotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new ForbiddenException(ModifyOwnProblemsOnlyMessage);
        }

        await EnsureNotLockedAsync(problemId);

        return problem;
    }

    private async Task<TestCase> GetOwnedUnlockedTestCaseAsync(Guid testCaseId, Guid authorId)
    {
        var testCase = await context.TestCases
            .Include(tc => tc.Problem)
            .FirstOrDefaultAsync(tc => tc.Id == testCaseId)
            ?? throw new NotFoundException($"Test case with ID {testCaseId} not found");

        if (testCase.Problem.AuthorId != authorId)
        {
            throw new ForbiddenException(ModifyOwnProblemsOnlyMessage);
        }

        await EnsureNotLockedAsync(testCase.ProblemId);

        return testCase;
    }

    private async Task EnsureNotLockedAsync(Guid problemId)
    {
        if (await IsUsedByPublishedCourseAsync(problemId))
        {
            throw new ProblemConflictException(LockedMessage);
        }
    }

    // Rejects unknown, disabled and duplicate languages instead of silently dropping them
    private async Task<Dictionary<Guid, Language>> GetValidStarterLanguagesAsync(List<CreateStarterCodeDto> starterCodes)
    {
        if (starterCodes.Count == 0)
        {
            throw new BusinessRuleException("At least one starter code is required");
        }

        var languageIds = starterCodes.Select(sc => sc.LanguageId).ToList();

        if (languageIds.Distinct().Count() != languageIds.Count)
        {
            throw new BusinessRuleException("Each language may have only one starter code");
        }

        var languages = await context.Languages
            .Where(l => languageIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id);

        var unknown = languageIds.Where(id => !languages.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"Unknown language IDs: {string.Join(", ", unknown)}");
        }

        var disabled = languages.Values.Where(l => !l.IsEnabled).Select(l => l.Name).ToList();
        if (disabled.Count > 0)
        {
            throw new BusinessRuleException($"Languages are disabled: {string.Join(", ", disabled)}");
        }

        return languages;
    }

    private async Task AddTagsAsync(Problem problem, List<Guid> tagIds)
    {
        if (tagIds.Count == 0)
        {
            return;
        }

        var tags = await context.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();

        foreach (var tag in tags)
        {
            context.ProblemTags.Add(new ProblemTag
            {
                ProblemId = problem.Id,
                TagId = tag.Id,
                Problem = problem,
                Tag = tag
            });
        }
    }

    private async Task EnsureFitsSandboxTimeBudgetAsync(Guid problemId, int testCaseCount)
    {
        var languages = await context.StarterCodes
            .Where(sc => sc.ProblemId == problemId)
            .Select(sc => sc.Language)
            .ToListAsync();

        EnsureFitsSandboxTimeBudget(testCaseCount, languages);
    }

    private void EnsureFitsSandboxTimeBudget(int testCaseCount, IEnumerable<Language> languages)
    {
        var cap = MaxContainerRuntimeSeconds;

        foreach (var language in languages)
        {
            var maxTests = SandboxTimeBudget.MaxTestCount(language, cap);
            var perTest = Math.Max(language.TimeoutSeconds, 1);

            if (testCaseCount > maxTests)
            {
                throw new BusinessRuleException(
                    $"Too many test cases for {language.Name}: with a {perTest}s limit per test, at most " +
                    $"{maxTests} test cases fit in the {cap}s sandbox time limit");
            }
        }
    }

    private static void EnsureTestCaseLimit(int resultingCount)
    {
        if (resultingCount > TestCaseLimits.MaxTestCasesPerProblem)
        {
            throw new BusinessRuleException(
                $"A problem can have at most {TestCaseLimits.MaxTestCasesPerProblem} test cases");
        }
    }

    // ILIKE treats % and _ as wildcards; backslash is the default escape character in PostgreSQL
    private static string EscapeLikePattern(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static TestCaseResponseDto MapToTestCaseDto(TestCase testCase) => new()
    {
        Id = testCase.Id,
        Input = testCase.Input,
        ExpectedOutput = testCase.ExpectedOutput,
        IsPublic = testCase.IsPublic,
        OrderIndex = testCase.OrderIndex
    };

    private async Task<ProblemResponseDto> MapToProblemResponseDto(Problem problem, bool includeHiddenTestCases)
    {
        await EnsureNavigationPropertiesLoaded(problem);

        // Only the problem author may see hidden test cases
        var testCases = problem.TestCases
            .Where(tc => includeHiddenTestCases || tc.IsPublic)
            .OrderBy(tc => tc.OrderIndex);

        return new ProblemResponseDto
        {
            Id = problem.Id,
            Title = problem.Title,
            Description = problem.Description,
            Difficulty = problem.Difficulty.ToString(),
            AuthorId = problem.AuthorId,
            AuthorName = $"{problem.Author.FirstName} {problem.Author.LastName}",
            CreatedAt = problem.CreatedAt,
            IsUsedByPublishedCourse = await IsUsedByPublishedCourseAsync(problem.Id),
            TestCases = testCases.Select(MapToTestCaseDto).ToList(),
            StarterCodes = problem.StarterCodes
                .OrderBy(sc => sc.Language.Name)
                .Select(sc => new StarterCodeResponseDto
                {
                    Id = sc.Id,
                    Code = sc.Code,
                    LanguageId = sc.LanguageId,
                    LanguageName = sc.Language.Name
                }).ToList(),
            Tags = problem.ProblemTags
                .OrderBy(pt => pt.Tag.Name)
                .Select(pt => new TagResponseDto
                {
                    Id = pt.Tag.Id,
                    Name = pt.Tag.Name
                }).ToList()
        };
    }

    private async Task EnsureNavigationPropertiesLoaded(Problem problem)
    {
        if (!context.Entry(problem).Collection(p => p.TestCases).IsLoaded)
        {
            await context.Entry(problem).Collection(p => p.TestCases).LoadAsync();
        }

        if (!context.Entry(problem).Collection(p => p.StarterCodes).IsLoaded)
        {
            await context.Entry(problem).Collection(p => p.StarterCodes).Query()
                .Include(sc => sc.Language)
                .LoadAsync();
        }

        if (!context.Entry(problem).Collection(p => p.ProblemTags).IsLoaded)
        {
            await context.Entry(problem).Collection(p => p.ProblemTags).Query()
                .Include(pt => pt.Tag)
                .LoadAsync();
        }
    }
}
