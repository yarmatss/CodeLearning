using CodeLearning.Application.DTOs.Submission;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Infrastructure.Services;

public class SubmissionService(ApplicationDbContext context, IPublishEndpoint publishEndpoint) : ISubmissionService
{
    public async Task<SubmissionResponseDto> SubmitCodeAsync(SubmitCodeDto dto, Guid studentId)
    {
        var problem = await context.Problems
            .Include(p => p.TestCases)
            .Include(p => p.StarterCodes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == dto.ProblemId)
            ?? throw new BusinessRuleException($"Problem {dto.ProblemId} not found");

        if (!await CanSubmitAsync(problem, studentId))
        {
            throw new BusinessRuleException("You must be enrolled in a course that contains this problem to submit solutions");
        }

        if (problem.TestCases.Count == 0)
        {
            throw new BusinessRuleException("Problem has no test cases");
        }

        // Validate that the language is supported for this problem
        var hasStarterCodeForLanguage = problem.StarterCodes.Any(sc => sc.LanguageId == dto.LanguageId);
        if (!hasStarterCodeForLanguage)
        {
            throw new BusinessRuleException($"Language is not supported for this problem. Available languages: {string.Join(", ", problem.StarterCodes.Select(sc => sc.LanguageId))}");
        }

        var language = await context.Languages
            .FirstOrDefaultAsync(l => l.Id == dto.LanguageId)
            ?? throw new BusinessRuleException($"Language {dto.LanguageId} not found");

        if (!language.IsEnabled)
        {
            throw new BusinessRuleException($"Language {language.Name} is disabled");
        }

        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ProblemId = dto.ProblemId,
            Problem = problem,
            StudentId = studentId,
            Student = await context.Users.FindAsync(studentId)
                ?? throw new BusinessRuleException($"Student {studentId} not found"),
            LanguageId = dto.LanguageId,
            Language = language,
            Code = dto.Code,
            Status = SubmissionStatus.Pending,
            Score = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.Submissions.Add(submission);

        // The bus outbox stores the message in the same transaction as the submission and delivers it afterwards
        await publishEndpoint.Publish(new SubmissionCreated(submission.Id));
        await context.SaveChangesAsync();

        return MapToDto(submission);
    }

    public async Task<SubmissionResponseDto?> GetSubmissionAsync(Guid submissionId, Guid studentId)
    {
        var submission = await context.Submissions
            .Include(s => s.Problem)
            .Include(s => s.Language)
            .Include(s => s.TestResults)
                .ThenInclude(tr => tr.TestCase)
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.StudentId == studentId);

        return submission == null ? null : MapToDto(submission);
    }

    public async Task<List<SubmissionResponseDto>> GetMySubmissionsAsync(Guid problemId, Guid studentId)
    {
        return await context.Submissions
            .Where(s => s.ProblemId == problemId && s.StudentId == studentId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SubmissionResponseDto
            {
                Id = s.Id,
                ProblemId = s.ProblemId,
                ProblemTitle = s.Problem.Title,
                LanguageId = s.LanguageId,
                LanguageName = s.Language.Name,
                Code = s.Code,
                Status = s.Status,
                Score = s.Score,
                TotalTestCases = s.TestResults.Count,
                PassedTestCases = s.TestResults.Count(tr => tr.Status == TestResultStatus.Passed),
                ExecutionTimeMs = s.ExecutionTimeMs,
                MemoryUsedKB = s.MemoryUsedKB,
                CompilationError = s.CompilationError,
                RuntimeError = s.RuntimeError,
                CreatedAt = s.CreatedAt,
                CompletedAt = s.CompletedAt
            })
            .ToListAsync();
    }

    // Every submission occupies a sandbox container, so only the problem's author (testing it)
    // or students enrolled in a course that uses the problem may submit
    private async Task<bool> CanSubmitAsync(Problem problem, Guid userId)
    {
        if (problem.AuthorId == userId)
        {
            return true;
        }

        return await context.CourseBlocks.AnyAsync(b =>
            b.ProblemId == problem.Id &&
            context.StudentCourseProgresses.Any(p =>
                p.StudentId == userId && p.CourseId == b.Subchapter.Chapter.CourseId));
    }

    private static SubmissionResponseDto MapToDto(Submission submission)
    {
        var totalTests = submission.TestResults.Count;
        var passedTests = submission.TestResults.Count(tr => tr.Status == TestResultStatus.Passed);

        var dto = new SubmissionResponseDto
        {
            Id = submission.Id,
            ProblemId = submission.ProblemId,
            ProblemTitle = submission.Problem?.Title ?? string.Empty,
            LanguageId = submission.LanguageId,
            LanguageName = submission.Language?.Name ?? string.Empty,
            Code = submission.Code,
            Status = submission.Status,
            Score = submission.Score,
            TotalTestCases = totalTests,
            PassedTestCases = passedTests,
            ExecutionTimeMs = submission.ExecutionTimeMs,
            MemoryUsedKB = submission.MemoryUsedKB,
            CompilationError = submission.CompilationError,
            RuntimeError = submission.RuntimeError,
            CreatedAt = submission.CreatedAt,
            CompletedAt = submission.CompletedAt
        };

        if (submission.TestResults.Count > 0)
        {
            // Only return public test results to students
            dto.TestResults = submission.TestResults
                .Where(tr => tr.TestCase.IsPublic)
                .Select(tr => new TestResultDto
                {
                    TestCaseId = tr.TestCaseId,
                    Status = tr.Status,
                    Input = tr.TestCase.Input,
                    ExpectedOutput = tr.TestCase.ExpectedOutput,
                    ActualOutput = tr.ActualOutput,
                    ErrorMessage = tr.ErrorMessage,
                    ExecutionTimeMs = tr.ExecutionTimeMs,
                    MemoryUsedKB = tr.MemoryUsedKB,
                    IsPublic = true
                }).ToList();
        }

        return dto;
    }
}
