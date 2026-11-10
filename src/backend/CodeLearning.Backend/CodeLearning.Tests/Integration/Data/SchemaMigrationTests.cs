using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CodeLearning.Tests.Integration.Data;

// Uses its own database container because it migrates the schema down and back up
public class SchemaMigrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private const string MigrationBeforeReviewFixesRound2 = "20261106183650_BackendReviewFixes";
    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ReviewFixesRound2_DownThenUp_ShouldRestoreTablesAndRemoveDuplicates()
    {
        // Arrange - roll back to the previous migration, where duplicates are still allowed
        await using (var db = fixture.CreateDbContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeReviewFixesRound2);

            (await TableExistsAsync(db, "Comments")).Should().BeTrue();
            (await TableExistsAsync(db, "Certificates")).Should().BeTrue();
            (await IndexExistsAsync(db, "IX_StarterCodes_ProblemId_LanguageId")).Should().BeFalse();
            (await IndexExistsAsync(db, "IX_SubmissionTestResults_SubmissionId_TestCaseId")).Should().BeFalse();
        }

        var (problemId, submissionId, testCaseId, newestStarterCodeId, newestResultId) = await SeedDuplicatesAsync();

        // Act
        await using (var db = fixture.CreateDbContext())
        {
            await db.GetService<IMigrator>().MigrateAsync();
        }

        // Assert
        await using var verify = fixture.CreateDbContext();

        (await TableExistsAsync(verify, "Comments")).Should().BeFalse();
        (await TableExistsAsync(verify, "Certificates")).Should().BeFalse();
        (await IndexExistsAsync(verify, "IX_StarterCodes_ProblemId_LanguageId")).Should().BeTrue();
        (await IndexExistsAsync(verify, "IX_SubmissionTestResults_SubmissionId_TestCaseId")).Should().BeTrue();

        var starterCodes = await verify.StarterCodes
            .Where(s => s.ProblemId == problemId && s.LanguageId == PythonLanguageId)
            .Select(s => s.Id)
            .ToListAsync();
        starterCodes.Should().ContainSingle().Which.Should().Be(newestStarterCodeId);

        var results = await verify.SubmissionTestResults
            .Where(r => r.SubmissionId == submissionId && r.TestCaseId == testCaseId)
            .Select(r => r.Id)
            .ToListAsync();
        results.Should().ContainSingle().Which.Should().Be(newestResultId);
    }

    [Fact]
    public async Task StarterCode_DuplicateLanguageForProblem_ShouldViolateUniqueIndex()
    {
        // Arrange
        await using var db = fixture.CreateDbContext();
        var language = await db.Languages.SingleAsync(l => l.Id == PythonLanguageId);
        var problem = TestDataBuilder.CreateValidProblem(TestDataBuilder.CreateValidUser(UserRole.Teacher));
        problem.StarterCodes.Add(NewStarterCode(problem, language));
        db.Problems.Add(problem);
        await db.SaveChangesAsync();

        db.StarterCodes.Add(NewStarterCode(problem, language));

        // Act
        var act = () => db.SaveChangesAsync();

        // Assert
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task SubmissionTestResult_DuplicateTestCaseForSubmission_ShouldViolateUniqueIndex()
    {
        // Arrange
        await using var db = fixture.CreateDbContext();
        var (submission, testCase) = await CreateSubmissionAsync(db);
        db.SubmissionTestResults.Add(NewTestResult(submission, testCase));
        await db.SaveChangesAsync();

        db.SubmissionTestResults.Add(NewTestResult(submission, testCase));

        // Act
        var act = () => db.SaveChangesAsync();

        // Assert
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task<(Guid ProblemId, Guid SubmissionId, Guid TestCaseId, Guid NewestStarterCodeId, Guid NewestResultId)> SeedDuplicatesAsync()
    {
        await using var db = fixture.CreateDbContext();

        var (submission, testCase) = await CreateSubmissionAsync(db);
        var language = submission.Language;

        // Saved one at a time so each row gets a later timestamp than the one before
        db.StarterCodes.Add(NewStarterCode(submission.Problem, language));
        db.SubmissionTestResults.Add(NewTestResult(submission, testCase));
        await db.SaveChangesAsync();

        var newestStarterCode = NewStarterCode(submission.Problem, language);
        var newestResult = NewTestResult(submission, testCase);
        db.StarterCodes.Add(newestStarterCode);
        db.SubmissionTestResults.Add(newestResult);
        await db.SaveChangesAsync();

        return (submission.ProblemId, submission.Id, testCase.Id, newestStarterCode.Id, newestResult.Id);
    }

    private static async Task<(Submission Submission, TestCase TestCase)> CreateSubmissionAsync(ApplicationDbContext db)
    {
        var language = await db.Languages.SingleAsync(l => l.Id == PythonLanguageId);
        var student = TestDataBuilder.CreateValidUser();
        var problem = TestDataBuilder.CreateValidProblem(TestDataBuilder.CreateValidUser(UserRole.Teacher));
        var testCase = TestDataBuilder.CreateValidTestCase(problem);
        problem.TestCases.Add(testCase);

        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ProblemId = problem.Id,
            Problem = problem,
            StudentId = student.Id,
            Student = student,
            LanguageId = language.Id,
            Language = language,
            Code = "print(1)"
        };

        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        return (submission, testCase);
    }

    private static StarterCode NewStarterCode(Problem problem, Language language) => new()
    {
        Id = Guid.NewGuid(),
        Code = "pass",
        ProblemId = problem.Id,
        Problem = problem,
        LanguageId = language.Id,
        Language = language
    };

    private static SubmissionTestResult NewTestResult(Submission submission, TestCase testCase) => new()
    {
        Id = Guid.NewGuid(),
        SubmissionId = submission.Id,
        Submission = submission,
        TestCaseId = testCase.Id,
        TestCase = testCase,
        Status = TestResultStatus.Passed
    };

    private static Task<bool> TableExistsAsync(ApplicationDbContext db, string table) =>
        db.Database
            .SqlQuery<bool>($"SELECT to_regclass({"public.\"" + table + "\""}) IS NOT NULL AS \"Value\"")
            .SingleAsync();

    private static Task<bool> IndexExistsAsync(ApplicationDbContext db, string index) =>
        db.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = {index}) AS \"Value\"")
            .SingleAsync();
}
