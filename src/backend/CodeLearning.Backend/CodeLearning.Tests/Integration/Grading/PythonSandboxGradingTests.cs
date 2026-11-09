extern alias Runner;

using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CodeLearning.Tests.Integration.Sandbox;
using Runner::CodeLearning.Runner.Consumers;
using Runner::CodeLearning.Runner.Services;
using ExecutionResult = CodeLearning.Core.Models.ExecutionResult;

namespace CodeLearning.Tests.Integration.Grading;

// End to end through the real pipeline: SubmissionCreatedConsumer -> UniversalExecutor (real script)
// -> DockerRunner -> a real executor container running the agent -> SubmissionGrader -> database.
// Needs Docker and the Python executor image (skipped without it):
//   docker compose --profile executors build executor-python
// CODELEARNING_PYTHON_IMAGE overrides the seeded image name, e.g. to test a locally rebuilt agent;
// CL_SANDBOX_DOCKER_HOST sends the Docker calls elsewhere, e.g. through a DockerGuard
[Trait("Category", "Sandbox")]
[Collection(SandboxCollection.Name)]
public class PythonSandboxGradingTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private const string PythonImage = "codelearning/python:3.12-slim";
    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [SandboxFact(PythonImage)]
    public async Task CorrectSolution_ShouldBeAcceptedWithOneResultPerTest()
    {
        var submission = await RunAsync(
            "a, b = map(int, input().split())\nprint(a + b)",
            ("1 2", "3", true),
            ("10 5", "15", false));

        submission.Status.Should().Be(SubmissionStatus.Completed, Describe(submission));
        submission.Score.Should().Be(100);
        submission.TestResults.Should().HaveCount(2).And.OnlyContain(r => r.Status == TestResultStatus.Passed);
    }

    [SandboxFact(PythonImage)]
    public async Task WrongAnswerAndHiddenRuntimeError_ShouldScorePartiallyWithoutLeakingTheHiddenInput()
    {
        var submission = await RunAsync(
            """
            import sys
            data = input()
            if data == "secret 42":
                sys.stderr.write("leak: " + data)
                sys.exit(3)
            print(int(data) * 2)
            """,
            ("2", "4", true),
            ("secret 42", "x", false),
            ("3", "7", true));

        submission.Score.Should().Be(33);
        submission.Status.Should().Be(SubmissionStatus.RuntimeError);
        submission.RuntimeError.Should().NotContain("secret");
    }

    [SandboxFact(PythonImage)]
    public async Task OutputWithNulCharacters_ShouldBeGradedAndStored()
    {
        var submission = await RunAsync("print('a\\0b')", ("", "ab", true));

        // Stored without the NUL (PostgreSQL text rejects it); the comparison itself saw it, so the answer is wrong
        submission.Status.Should().Be(SubmissionStatus.Completed, Describe(submission));
        submission.Score.Should().Be(0);
        submission.TestResults.Single().ActualOutput.Should().NotContain("\0");
    }

    [SandboxFact(PythonImage)]
    public async Task StderrFloodWhileExhaustingMemory_ShouldStillBeMemoryLimitExceeded()
    {
        var submission = await RunAsync(
            """
            import sys
            sys.stderr.write("E" * 200000)
            sys.stderr.flush()
            hog = []
            while True:
                hog.append(b"x" * (8 * 1024 * 1024))
            """,
            ("", "1", true));

        submission.Status.Should().Be(SubmissionStatus.MemoryLimitExceeded);
        submission.TestResults.Single().Status.Should().Be(TestResultStatus.MemoryLimitExceeded);
    }

    [SandboxFact(PythonImage)]
    public async Task ProgramIgnoringALargeStdinWhileLooping_ShouldGetAPerTestTimeLimit()
    {
        // More input than a pipe buffer holds: writing it must not stall the agent past the time limit
        var input = new string('7', 100_000);

        var submission = await RunAsync(
            "while True:\n    pass",
            (input, "1", true),
            ("1", "1", true));

        submission.Status.Should().Be(SubmissionStatus.TimeLimitExceeded);
        submission.TestResults.Should().HaveCount(2).And.OnlyContain(r => r.Status == TestResultStatus.TimeLimitExceeded);
        submission.RuntimeError.Should().BeNull();
    }

    private async Task<Submission> RunAsync(string code, params (string Input, string Expected, bool IsPublic)[] tests)
    {
        var submissionId = await SeedSubmissionAsync(code, tests);

        var workspace = Path.Combine(Path.GetTempPath(), "codelearning-sandbox-tests");
        Directory.CreateDirectory(workspace);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionSettings:DockerHost"] = SandboxDocker.Host,
                ["ExecutionSettings:WorkspaceBasePath"] = workspace,
                ["ExecutionSettings:MaxContainerRuntimeSeconds"] = "120",
                ["ExecutionSettings:MaxConcurrentExecutions"] = "1",
                ["ExecutionSettings:MaxDeliveryAttempts"] = "2",
                ["ExecutionSettings:RetryDelaySeconds"] = "0"
            })
            .Build();

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddScoped(_ => fixture.CreateDbContext())
            .AddSingleton<IDockerRunner, DockerRunner>()
            .AddSingleton<UniversalExecutor>()
            .AddSingleton<ICodeExecutor>(sp => new ImageOverrideExecutor(sp.GetRequiredService<UniversalExecutor>()))
            .AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromMinutes(3), testInactivityTimeout: TimeSpan.FromSeconds(5));
                x.AddConsumer<SubmissionCreatedConsumer, SubmissionCreatedConsumerDefinition>();
                x.AddConsumer<SubmissionFaultConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetTestHarness();
        await harness.Start();

        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            var submission = await LoadAsync(submissionId);
            if (submission.Status is not (SubmissionStatus.Pending or SubmissionStatus.Running))
            {
                var fault = harness.Published.Select<Fault<SubmissionCreated>>().FirstOrDefault();
                if (fault != null)
                {
                    LastInfrastructureError = string.Join(Environment.NewLine,
                        fault.Context.Message.Exceptions.Select(e => $"{e.ExceptionType}: {e.Message} {e.StackTrace}"));
                }

                return submission;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Submission still {submission.Status}");
            }

            await Task.Delay(250);
        }
    }

    private async Task<Guid> SeedSubmissionAsync(string code, (string Input, string Expected, bool IsPublic)[] tests)
    {
        await using var dbContext = fixture.CreateDbContext();

        var student = TestDataBuilder.CreateValidUser();
        var problem = TestDataBuilder.CreateValidProblem(TestDataBuilder.CreateValidUser(UserRole.Teacher));

        for (var i = 0; i < tests.Length; i++)
        {
            var testCase = TestDataBuilder.CreateValidTestCase(problem, orderIndex: i + 1, isPublic: tests[i].IsPublic);
            testCase.Input = tests[i].Input;
            testCase.ExpectedOutput = tests[i].Expected;
            problem.TestCases.Add(testCase);
        }

        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ProblemId = problem.Id,
            Problem = problem,
            StudentId = student.Id,
            Student = student,
            LanguageId = PythonLanguageId,
            Language = await dbContext.Languages.SingleAsync(l => l.Id == PythonLanguageId),
            Code = code
        };

        dbContext.Submissions.Add(submission);
        await dbContext.SaveChangesAsync();
        return submission.Id;
    }

    private async Task<Submission> LoadAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.Submissions
            .AsNoTracking()
            .Include(s => s.TestResults)
            .SingleAsync(s => s.Id == submissionId);
    }

    private static string? LastInfrastructureError;

    private static string Describe(Submission submission) =>
        $"[{LastInfrastructureError}] [{submission.RuntimeError}] "
        + string.Join("; ", submission.TestResults.Select(r => $"{r.Status}: {r.ErrorMessage}"));

    private sealed class ImageOverrideExecutor(UniversalExecutor inner) : ICodeExecutor
    {
        public async Task<ExecutionResult> ExecuteAsync(
            Submission submission,
            Language language,
            List<TestCase> testCases,
            CancellationToken cancellationToken = default)
        {
            // The consumer loads the language untracked, so this never reaches the database
            if (Environment.GetEnvironmentVariable("CODELEARNING_PYTHON_IMAGE") is { Length: > 0 } image)
            {
                language.DockerImage = image;
            }

            try
            {
                return await inner.ExecuteAsync(submission, language, testCases, cancellationToken);
            }
            catch (Exception ex)
            {
                // Surfaced in assertion messages: the submission itself only records a generic internal error
                LastInfrastructureError = ex.ToString();
                throw;
            }
        }
    }
}
