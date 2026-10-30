using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Models;

namespace CodeLearning.Core.Grading;

public static class SubmissionGrader
{
    public static ExecutionResult Grade(ExecutionResult sandboxResult, IReadOnlyList<TestCase> testCases)
    {
        if (sandboxResult.TestResults.Count == 0 && sandboxResult.Status != SubmissionStatus.Completed
            || sandboxResult.Status == SubmissionStatus.CompilationError)
        {
            return new ExecutionResult
            {
                Status = sandboxResult.Status,
                Score = 0,
                TotalTests = testCases.Count,
                PassedTests = 0,
                TotalExecutionTimeMs = sandboxResult.TotalExecutionTimeMs,
                MaxMemoryUsedKB = sandboxResult.MaxMemoryUsedKB,
                CompilationError = sandboxResult.CompilationError,
                RuntimeError = sandboxResult.RuntimeError
            };
        }

        var reportedByIndex = sandboxResult.TestResults
            .GroupBy(tr => tr.TestIndex)
            .ToDictionary(g => g.Key, g => g.First());

        var gradedResults = testCases
            .Select((testCase, index) => GradeTestCase(
                testCase,
                reportedByIndex.GetValueOrDefault(index),
                index))
            .ToList();

        var passedTests = gradedResults.Count(tr => tr.Status == TestResultStatus.Passed);

        var status = gradedResults.Any(tr => tr.Status == TestResultStatus.TimeLimitExceeded)
            ? SubmissionStatus.TimeLimitExceeded
            : gradedResults.Any(tr => tr.Status == TestResultStatus.RuntimeError)
                ? SubmissionStatus.RuntimeError
                : SubmissionStatus.Completed;

        var runtimeError = status == SubmissionStatus.RuntimeError
            ? sandboxResult.RuntimeError
              ?? gradedResults.FirstOrDefault(tr => !string.IsNullOrWhiteSpace(tr.ErrorMessage))?.ErrorMessage
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

    public static bool OutputsMatch(string? actualOutput, string expectedOutput)
    {
        return Normalize(actualOutput ?? string.Empty) == Normalize(expectedOutput);
    }

    private static TestCaseResult GradeTestCase(TestCase testCase, TestCaseResult? reported, int index)
    {
        if (reported == null)
        {
            return new TestCaseResult
            {
                TestCaseId = testCase.Id,
                TestIndex = index,
                Status = TestResultStatus.RuntimeError,
                ErrorMessage = "No result produced for this test case"
            };
        }

        var status = reported.Status switch
        {
            TestResultStatus.RuntimeError => TestResultStatus.RuntimeError,
            TestResultStatus.TimeLimitExceeded => TestResultStatus.TimeLimitExceeded,
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

    private static string Normalize(string output) => output.Replace("\r\n", "\n").Trim();
}
