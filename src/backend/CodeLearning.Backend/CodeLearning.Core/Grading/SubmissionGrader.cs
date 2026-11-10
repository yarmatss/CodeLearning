using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Models;

namespace CodeLearning.Core.Grading;

public static class SubmissionGrader
{
    public const string NoResultMessage = "No result produced for this test case";
    public const string IncompleteExecutionMessage = "Execution stopped before every test case was run.";
    public const string CompilationFailedMessage = "Compilation failed.";
    public const int MaxCompilationErrorLength = 10_000;

    public static ExecutionResult Grade(ExecutionResult sandboxResult, IReadOnlyList<TestCase> testCases)
    {
        // The compiler output is captured by the agent before any submitted code runs, so it is shown as is
        if (sandboxResult.Status == SubmissionStatus.CompilationError)
        {
            return new ExecutionResult
            {
                Status = SubmissionStatus.CompilationError,
                Score = 0,
                TotalTests = testCases.Count,
                PassedTests = 0,
                TotalExecutionTimeMs = sandboxResult.TotalExecutionTimeMs,
                MaxMemoryUsedKB = sandboxResult.MaxMemoryUsedKB,
                CompilationError = Truncate(sandboxResult.CompilationError) is { Length: > 0 } error
                    ? error
                    : CompilationFailedMessage
            };
        }

        var reportedByIndex = sandboxResult.TestResults
            .GroupBy(tr => tr.TestIndex)
            .ToDictionary(g => g.Key, g => g.First());

        // A whole-run time or memory limit explains why the remaining tests have no result
        var missingStatus = sandboxResult.Status switch
        {
            SubmissionStatus.TimeLimitExceeded => TestResultStatus.TimeLimitExceeded,
            SubmissionStatus.MemoryLimitExceeded => TestResultStatus.MemoryLimitExceeded,
            _ => TestResultStatus.RuntimeError
        };

        var gradedResults = testCases
            .Select((testCase, index) => GradeTestCase(
                testCase,
                reportedByIndex.GetValueOrDefault(index),
                index,
                missingStatus))
            .ToList();

        var passedTests = gradedResults.Count(tr => tr.Status == TestResultStatus.Passed);

        var status = GetOverallStatus(gradedResults);

        // The submission-level error is shown to the student. The sandbox's own RuntimeError is never passed
        // through, and a test's error output is used only for public tests: a hidden test's could reveal its input
        var runtimeError = status == SubmissionStatus.RuntimeError
            ? GetSubmissionRuntimeError(gradedResults, testCases)
            : null;

        return new ExecutionResult
        {
            Status = status,
            Score = testCases.Count > 0 ? passedTests * 100 / testCases.Count : 0,
            TotalTests = testCases.Count,
            PassedTests = passedTests,
            TotalExecutionTimeMs = sandboxResult.TotalExecutionTimeMs,
            MaxMemoryUsedKB = sandboxResult.MaxMemoryUsedKB,
            TestResults = gradedResults,
            RuntimeError = runtimeError
        };
    }

    // Line endings, trailing whitespace on each line, and blank lines or whitespace around the whole output are ignored
    public static bool OutputsMatch(string? actualOutput, string expectedOutput)
    {
        return Normalize(actualOutput ?? string.Empty) == Normalize(expectedOutput);
    }

    private static string? GetSubmissionRuntimeError(List<TestCaseResult> gradedResults, IReadOnlyList<TestCase> testCases)
    {
        var publicError = gradedResults
            .Where((_, index) => testCases[index].IsPublic)
            .FirstOrDefault(tr => tr.Status == TestResultStatus.RuntimeError
                                  && !string.IsNullOrWhiteSpace(tr.ErrorMessage)
                                  && tr.ErrorMessage != NoResultMessage)?.ErrorMessage;

        if (publicError is not null)
        {
            return publicError;
        }

        return gradedResults.Any(tr => tr.ErrorMessage == NoResultMessage) ? IncompleteExecutionMessage : null;
    }

    private static SubmissionStatus GetOverallStatus(List<TestCaseResult> gradedResults)
    {
        if (gradedResults.Any(tr => tr.Status == TestResultStatus.TimeLimitExceeded))
            return SubmissionStatus.TimeLimitExceeded;

        if (gradedResults.Any(tr => tr.Status == TestResultStatus.MemoryLimitExceeded))
            return SubmissionStatus.MemoryLimitExceeded;

        if (gradedResults.Any(tr => tr.Status == TestResultStatus.RuntimeError))
            return SubmissionStatus.RuntimeError;

        return SubmissionStatus.Completed;
    }

    private static TestCaseResult GradeTestCase(
        TestCase testCase,
        TestCaseResult? reported,
        int index,
        TestResultStatus missingStatus)
    {
        if (reported == null)
        {
            return new TestCaseResult
            {
                TestCaseId = testCase.Id,
                TestIndex = index,
                Status = missingStatus,
                ErrorMessage = missingStatus == TestResultStatus.RuntimeError ? NoResultMessage : null
            };
        }

        var status = reported.Status switch
        {
            TestResultStatus.RuntimeError => TestRunVerdict.IsOutOfMemoryMessage(reported.ErrorMessage)
                ? TestResultStatus.MemoryLimitExceeded
                : TestResultStatus.RuntimeError,
            TestResultStatus.TimeLimitExceeded => TestResultStatus.TimeLimitExceeded,
            TestResultStatus.MemoryLimitExceeded => TestResultStatus.MemoryLimitExceeded,
            _ => OutputsMatch(reported.ActualOutput, testCase.ExpectedOutput)
                ? TestResultStatus.Passed
                : TestResultStatus.Failed
        };

        return new TestCaseResult
        {
            TestCaseId = testCase.Id,
            TestIndex = index,
            Status = status,
            ActualOutput = reported.ActualOutput,
            ErrorMessage = reported.ErrorMessage,
            ExecutionTimeMs = reported.ExecutionTimeMs,
            MemoryUsedKB = reported.MemoryUsedKB
        };
    }

    private static string Normalize(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd());
        return string.Join('\n', lines).Trim();
    }

    private static string? Truncate(string? value) =>
        value is { Length: > MaxCompilationErrorLength }
            ? value[..MaxCompilationErrorLength] + "\n" + BoundedOutputCapture.TruncationMarker
            : value;
}
