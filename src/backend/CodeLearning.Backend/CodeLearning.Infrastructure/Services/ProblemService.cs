using CodeLearning.Application.DTOs.Problem;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class ProblemService(
    ApplicationDbContext context,
    ISanitizationService sanitizationService) : IProblemService
{
    public async Task<ProblemResponseDto> CreateProblemAsync(CreateProblemDto dto, Guid authorId)
    {
        var author = await context.Users.FindAsync(authorId)
            ?? throw new KeyNotFoundException("Author not found");

        var sanitizedDescription = sanitizationService.SanitizeMarkdown(dto.Description);

        var problem = new Problem
        {
            Title = dto.Title,
            Description = sanitizedDescription,
            Difficulty = Enum.Parse<DifficultyLevel>(dto.Difficulty),
            AuthorId = authorId,
            Author = author
        };

        context.Problems.Add(problem);

        if (dto.TestCases.Count > 0)
        {
            var testCases = dto.TestCases.Select((tc, index) => new TestCase
            {
                Input = tc.Input,
                ExpectedOutput = tc.ExpectedOutput,
                IsPublic = tc.IsPublic,
                OrderIndex = index + 1,
                ProblemId = problem.Id,
                Problem = problem
            }).ToList();

            context.TestCases.AddRange(testCases);
        }

        if (dto.StarterCodes.Count > 0)
        {
            var starterCodes = new List<StarterCode>();
            
            foreach (var sc in dto.StarterCodes)
            {
                var language = await context.Languages.FindAsync(sc.LanguageId);
                if (language != null)
                {
                    starterCodes.Add(new StarterCode
                    {
                        Code = sc.Code,
                        LanguageId = sc.LanguageId,
                        Language = language,
                        ProblemId = problem.Id,
                        Problem = problem
                    });
                }
            }

            context.StarterCodes.AddRange(starterCodes);
        }

        if (dto.TagIds.Count > 0)
        {
            var tags = await context.Tags.Where(t => dto.TagIds.Contains(t.Id)).ToListAsync();
            
            foreach (var tag in tags)
            {
                var problemTag = new ProblemTag
                {
                    ProblemId = problem.Id,
                    TagId = tag.Id,
                    Problem = problem,
                    Tag = tag
                };
                context.ProblemTags.Add(problemTag);
            }
        }

        await context.SaveChangesAsync();

        return await MapToProblemResponseDto(problem);
    }

    public async Task<ProblemResponseDto> GetProblemByIdAsync(Guid problemId)
    {
        var problem = await context.Problems
            .Include(p => p.Author)
            .Include(p => p.TestCases.OrderBy(tc => tc.OrderIndex))
            .Include(p => p.StarterCodes)
                .ThenInclude(sc => sc.Language)
            .Include(p => p.ProblemTags)
                .ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        return await MapToProblemResponseDto(problem);
    }

    public async Task<IEnumerable<ProblemListDto>> GetProblemsAsync(string? difficulty = null, Guid? tagId = null, string? search = null)
    {
        var query = context.Problems
            .Include(p => p.Author)
            .Include(p => p.TestCases)
            .Include(p => p.ProblemTags)
                .ThenInclude(pt => pt.Tag)
            .AsQueryable();

        if (!string.IsNullOrEmpty(difficulty))
        {
            if (Enum.TryParse<DifficultyLevel>(difficulty, out var difficultyLevel))
            {
                query = query.Where(p => p.Difficulty == difficultyLevel);
            }
        }

        if (tagId.HasValue)
        {
            query = query.Where(p => p.ProblemTags.Any(pt => pt.TagId == tagId.Value));
        }

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(p => p.Title.ToLower().Contains(searchLower) ||
                                    p.Description.ToLower().Contains(searchLower));
        }

        var problems = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return problems.Select(MapToProblemListDto);
    }

    public async Task<IEnumerable<ProblemListDto>> GetMyProblemsAsync(Guid authorId)
    {
        var problems = await context.Problems
            .Include(p => p.Author)
            .Include(p => p.TestCases)
            .Include(p => p.ProblemTags)
                .ThenInclude(pt => pt.Tag)
            .Where(p => p.AuthorId == authorId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return problems.Select(MapToProblemListDto);
    }

    public async Task<ProblemResponseDto> UpdateProblemAsync(Guid problemId, UpdateProblemDto dto, Guid authorId)
    {
        var problem = await context.Problems
            .Include(p => p.Author)
            .Include(p => p.ProblemTags)
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only update your own problems");
        }

        problem.Title = dto.Title;
        problem.Description = sanitizationService.SanitizeMarkdown(dto.Description);
        problem.Difficulty = Enum.Parse<DifficultyLevel>(dto.Difficulty);

        // Mark problem as modified to ensure EF Core tracks changes
        context.Entry(problem).State = EntityState.Modified;

        context.ProblemTags.RemoveRange(problem.ProblemTags);

        if (dto.TagIds.Count > 0)
        {
            var tags = await context.Tags.Where(t => dto.TagIds.Contains(t.Id)).ToListAsync();
            
            foreach (var tag in tags)
            {
                var problemTag = new ProblemTag
                {
                    ProblemId = problem.Id,
                    TagId = tag.Id,
                    Problem = problem,
                    Tag = tag
                };
                context.ProblemTags.Add(problemTag);
            }
        }

        await context.SaveChangesAsync();

        return await GetProblemByIdAsync(problemId);
    }

    public async Task DeleteProblemAsync(Guid problemId, Guid authorId)
    {
        var problem = await context.Problems
            .Include(p => p.Blocks)
            .Include(p => p.Submissions)
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only delete your own problems");
        }

        if (problem.Blocks.Count > 0)
        {
            throw new InvalidOperationException("Cannot delete problem that is used in courses. Remove it from courses first.");
        }

        if (problem.Submissions.Count > 0)
        {
            throw new InvalidOperationException("Cannot delete problem that has submissions.");
        }

        context.Problems.Remove(problem);
        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<TagResponseDto>> GetAllTagsAsync()
    {
        var tags = await context.Tags
            .OrderBy(t => t.Name)
            .ToListAsync();

        return tags.Select(t => new TagResponseDto
        {
            Id = t.Id,
            Name = t.Name
        });
    }

    public async Task<TestCaseResponseDto> AddTestCaseAsync(Guid problemId, CreateTestCaseDto dto, Guid authorId)
    {
        var problem = await context.Problems
            .Include(p => p.TestCases)
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        var maxOrder = problem.TestCases.Count > 0 ? problem.TestCases.Max(tc => tc.OrderIndex) : 0;

        var testCase = new TestCase
        {
            Input = dto.Input,
            ExpectedOutput = dto.ExpectedOutput,
            IsPublic = dto.IsPublic,
            OrderIndex = maxOrder + 1,
            ProblemId = problemId,
            Problem = problem
        };

        context.TestCases.Add(testCase);
        await context.SaveChangesAsync();

        return new TestCaseResponseDto
        {
            Id = testCase.Id,
            Input = testCase.Input,
            ExpectedOutput = testCase.ExpectedOutput,
            IsPublic = testCase.IsPublic,
            OrderIndex = testCase.OrderIndex
        };
    }

    public async Task<BulkAddTestCasesResult> AddTestCasesBulkAsync(Guid problemId, BulkAddTestCasesDto dto, Guid authorId)
    {
        var problem = await context.Problems
            .Include(p => p.TestCases)
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        var maxOrder = problem.TestCases.Count > 0 ? problem.TestCases.Max(tc => tc.OrderIndex) : 0;
        var newTestCases = new List<TestCase>();

        foreach (var (testCaseDto, index) in dto.TestCases.Select((tc, i) => (tc, i)))
        {
            var testCase = new TestCase
            {
                Input = testCaseDto.Input,
                ExpectedOutput = testCaseDto.ExpectedOutput,
                IsPublic = testCaseDto.IsPublic,
                OrderIndex = maxOrder + index + 1,
                ProblemId = problemId,
                Problem = problem
            };
            newTestCases.Add(testCase);
        }

        context.TestCases.AddRange(newTestCases);
        await context.SaveChangesAsync();

        return new BulkAddTestCasesResult
        {
            Added = newTestCases.Count,
            TestCases = newTestCases.Select(tc => new TestCaseResponseDto
            {
                Id = tc.Id,
                Input = tc.Input,
                ExpectedOutput = tc.ExpectedOutput,
                IsPublic = tc.IsPublic,
                OrderIndex = tc.OrderIndex
            }).ToList()
        };
    }

    public async Task<TestCaseResponseDto> UpdateTestCaseAsync(Guid testCaseId, UpdateTestCaseDto dto, Guid authorId)
    {
        var testCase = await context.TestCases
            .Include(tc => tc.Problem)
            .FirstOrDefaultAsync(tc => tc.Id == testCaseId)
            ?? throw new KeyNotFoundException($"Test case with ID {testCaseId} not found");

        if (testCase.Problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        var wasPublic = testCase.IsPublic;
        var willBePrivate = !dto.IsPublic;

        if (wasPublic && willBePrivate)
        {
            var remainingPublicTests = await context.TestCases
                .Where(tc => tc.ProblemId == testCase.ProblemId && tc.Id != testCaseId && tc.IsPublic)
                .CountAsync();

            if (remainingPublicTests == 0)
            {
                throw new InvalidOperationException("Cannot make the last public test case private");
            }
        }

        testCase.Input = dto.Input;
        testCase.ExpectedOutput = dto.ExpectedOutput;
        testCase.IsPublic = dto.IsPublic;

        await context.SaveChangesAsync();

        return new TestCaseResponseDto
        {
            Id = testCase.Id,
            Input = testCase.Input,
            ExpectedOutput = testCase.ExpectedOutput,
            IsPublic = testCase.IsPublic,
            OrderIndex = testCase.OrderIndex
        };
    }

    public async Task ReorderTestCasesAsync(Guid problemId, ReorderTestCasesDto dto, Guid authorId)
    {
        var problem = await context.Problems
            .Include(p => p.TestCases)
            .FirstOrDefaultAsync(p => p.Id == problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        var testCases = problem.TestCases.ToDictionary(tc => tc.Id);

        if (dto.TestCaseIds.Count != testCases.Count)
        {
            throw new InvalidOperationException("All test cases must be included in reorder operation");
        }

        if (dto.TestCaseIds.Any(id => !testCases.ContainsKey(id)))
        {
            throw new InvalidOperationException("One or more test case IDs do not belong to this problem");
        }

        for (int i = 0; i < dto.TestCaseIds.Count; i++)
        {
            var testCaseId = dto.TestCaseIds[i];
            testCases[testCaseId].OrderIndex = i + 1;
        }

        await context.SaveChangesAsync();
    }

    public async Task<StarterCodeResponseDto> AddStarterCodeAsync(Guid problemId, CreateStarterCodeDto dto, Guid authorId)
    {
        var problem = await context.Problems.FindAsync(problemId)
            ?? throw new KeyNotFoundException($"Problem with ID {problemId} not found");

        if (problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        var language = await context.Languages.FindAsync(dto.LanguageId)
            ?? throw new KeyNotFoundException($"Language with ID {dto.LanguageId} not found");

        var existingStarterCode = await context.StarterCodes
            .FirstOrDefaultAsync(sc => sc.ProblemId == problemId && sc.LanguageId == dto.LanguageId);

        if (existingStarterCode != null)
        {
            throw new InvalidOperationException($"Starter code for {language.Name} already exists. Update it instead.");
        }

        var starterCode = new StarterCode
        {
            Code = dto.Code,
            LanguageId = dto.LanguageId,
            Language = language,
            ProblemId = problemId,
            Problem = problem
        };

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

    public async Task DeleteTestCaseAsync(Guid testCaseId, Guid authorId)
    {
        var testCase = await context.TestCases
            .Include(tc => tc.Problem)
            .FirstOrDefaultAsync(tc => tc.Id == testCaseId)
            ?? throw new KeyNotFoundException($"Test case with ID {testCaseId} not found");

        if (testCase.Problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        var remainingPublicTests = await context.TestCases
            .Where(tc => tc.ProblemId == testCase.ProblemId && tc.Id != testCaseId && tc.IsPublic)
            .CountAsync();

        if (testCase.IsPublic && remainingPublicTests == 0)
        {
            throw new InvalidOperationException("Cannot delete the last public test case");
        }

        context.TestCases.Remove(testCase);
        await context.SaveChangesAsync();
    }

    public async Task DeleteStarterCodeAsync(Guid starterCodeId, Guid authorId)
    {
        var starterCode = await context.StarterCodes
            .Include(sc => sc.Problem)
            .FirstOrDefaultAsync(sc => sc.Id == starterCodeId)
            ?? throw new KeyNotFoundException($"Starter code with ID {starterCodeId} not found");

        if (starterCode.Problem.AuthorId != authorId)
        {
            throw new UnauthorizedAccessException("You can only modify your own problems");
        }

        context.StarterCodes.Remove(starterCode);
        await context.SaveChangesAsync();
    }

    private async Task<ProblemResponseDto> MapToProblemResponseDto(Problem problem)
    {
        await EnsureNavigationPropertiesLoaded(problem);

        return new ProblemResponseDto
        {
            Id = problem.Id,
            Title = problem.Title,
            Description = problem.Description,
            Difficulty = problem.Difficulty.ToString(),
            AuthorId = problem.AuthorId,
            AuthorName = $"{problem.Author.FirstName} {problem.Author.LastName}",
            CreatedAt = problem.CreatedAt,
            TestCases = problem.TestCases.Select(tc => new TestCaseResponseDto
            {
                Id = tc.Id,
                Input = tc.Input,
                ExpectedOutput = tc.ExpectedOutput,
                IsPublic = tc.IsPublic,
                OrderIndex = tc.OrderIndex
            }).ToList(),
            StarterCodes = problem.StarterCodes.Select(sc => new StarterCodeResponseDto
            {
                Id = sc.Id,
                Code = sc.Code,
                LanguageId = sc.LanguageId,
                LanguageName = sc.Language.Name
            }).ToList(),
            Tags = problem.ProblemTags.Select(pt => new TagResponseDto
            {
                Id = pt.Tag.Id,
                Name = pt.Tag.Name
            }).ToList()
        };
    }

    private static ProblemListDto MapToProblemListDto(Problem problem) => new()
    {
        Id = problem.Id,
        Title = problem.Title,
        Difficulty = problem.Difficulty.ToString(),
        AuthorName = $"{problem.Author.FirstName} {problem.Author.LastName}",
        TestCasesCount = problem.TestCases.Count,
        Tags = problem.ProblemTags.Select(pt => new TagResponseDto
        {
            Id = pt.Tag.Id,
            Name = pt.Tag.Name
        }).ToList(),
        CreatedAt = problem.CreatedAt
    };

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
