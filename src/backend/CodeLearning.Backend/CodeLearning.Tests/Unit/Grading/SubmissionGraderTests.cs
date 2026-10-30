using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using CodeLearning.Core.Models;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Grading;

public class SubmissionGraderTests
{
    private static List<TestCase> CreateTestCases(params string[] expectedOutputs)
    {
        return expectedOutputs.Select((expected, index) => new TestCase
        {
            Id = Guid.NewGuid(),
            Input = $"input {index}",
            ExpectedOutput = expected,
            OrderIndex = index + 1,
            Problem = null!
        }).ToList();
    }

    private static TestCaseResult Ran(int testIndex, string output) => new()
    {
        TestIndex = testIndex,
        Status = TestResultStatus.Failed, // sandbox does not judge
        ActualOutput = output
    };

    #region Output Comparison Tests

    [Fact]
    public void Grade_AllOutputsMatch_ShouldPassWithFullScore()
    {
        // Arrange
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults = [Ran(0, "3"), Ran(1, "15")]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.Completed);
        result.Score.Should().Be(100);
        result.PassedTests.Should().Be(2);
        result.TestResults.Should().OnlyContain(tr => tr.Status == TestResultStatus.Passed);
        result.TestResults.Select(tr => tr.TestCaseId).Should().Equal(testCases.Select(tc => tc.Id));
    }

    [Fact]
    public void Grade_OneWrongOutput_ShouldFailThatTestAndScorePartially()
    {
        // Arrange
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults = [Ran(0, "3"), Ran(1, "16")]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.Completed);
        result.Score.Should().Be(50);
        result.TestResults[1].Status.Should().Be(TestResultStatus.Failed);
    }

    [Theory]
    [InlineData("1\r\n2", "1\n2")]
    [InlineData("  42  \n", "42")]
    [InlineData("42", "42\n")]
    public void OutputsMatch_ShouldIgnoreLineEndingsAndSurroundingWhitespace(string actual, string expected)
    {
        SubmissionGrader.OutputsMatch(actual, expected).Should().BeTrue();
    }

    #endregion

    #region Untrusted Sandbox Report Tests

    [Fact]
    public void Grade_SandboxClaimsPassedAndFullScore_ShouldStillCompareOutputs()
    {
        // Arrange - a forged report: everything "Passed", score 100, wrong output
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            Score = 100,
            PassedTests = 1,
            TestResults =
            [
                new TestCaseResult { TestIndex = 0, Status = TestResultStatus.Passed, ActualOutput = "wrong" }
            ]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Score.Should().Be(0);
        result.TestResults.Single().Status.Should().Be(TestResultStatus.Failed);
    }

    [Fact]
    public void Grade_CompletedWithoutAnyTestResults_ShouldScoreZero()
    {
        // Arrange
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult { Status = SubmissionStatus.Completed, Score = 100 };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Score.Should().Be(0);
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.TestResults.Should().HaveCount(2)
            .And.OnlyContain(tr => tr.Status == TestResultStatus.RuntimeError);
    }

    [Fact]
    public void Grade_DuplicateOrUnknownTestIndexes_ShouldBeIgnored()
    {
        // Arrange - index 0 reported twice, index 7 does not exist, index 1 missing
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults = [Ran(0, "3"), Ran(0, "3"), Ran(7, "15")]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.TestResults.Should().HaveCount(2);
        result.TestResults[0].Status.Should().Be(TestResultStatus.Passed);
        result.TestResults[1].Status.Should().Be(TestResultStatus.RuntimeError);
        result.Score.Should().Be(50);
    }

    [Theory]
    [InlineData(SubmissionStatus.Pending)]
    [InlineData(SubmissionStatus.Running)]
    public void Grade_NonTerminalStatusWithoutTestResults_ShouldBecomeRuntimeError(SubmissionStatus forgedStatus)
    {
        // Arrange - a forged report that would leave the submission unfinished
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult { Status = forgedStatus };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.Score.Should().Be(0);
    }

    #endregion

    #region Error Status Tests

    [Fact]
    public void Grade_SandboxRuntimeErrorWithPartialResults_ShouldGradeReportedAndFailMissing()
    {
        // Arrange - runner crashed after the first test
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.RuntimeError,
            RuntimeError = "Internal Runner Error: boom",
            TestResults = [Ran(0, "3")]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.RuntimeError.Should().Be("Internal Runner Error: boom");
        result.TestResults[0].Status.Should().Be(TestResultStatus.Passed);
        result.TestResults[1].Status.Should().Be(TestResultStatus.RuntimeError);
        result.Score.Should().Be(50);
    }

    [Fact]
    public void Grade_TimeLimitExceededOnOneTest_ShouldReportTimeLimitExceeded()
    {
        // Arrange
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults =
            [
                Ran(0, "3"),
                new TestCaseResult { TestIndex = 1, Status = TestResultStatus.TimeLimitExceeded }
            ]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.TimeLimitExceeded);
        result.Score.Should().Be(50);
    }

    [Fact]
    public void Grade_RuntimeError_ShouldUseFirstErrorMessage()
    {
        // Arrange
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults =
            [
                new TestCaseResult { TestIndex = 0, Status = TestResultStatus.RuntimeError, ErrorMessage = "ZeroDivisionError" }
            ]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.RuntimeError.Should().Be("ZeroDivisionError");
        result.Score.Should().Be(0);
    }

    [Fact]
    public void Grade_CompilationError_ShouldPassThroughWithZeroScore()
    {
        // Arrange
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.CompilationError,
            CompilationError = "error CS1002: ; expected",
            Score = 100
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.CompilationError);
        result.CompilationError.Should().Contain("CS1002");
        result.Score.Should().Be(0);
        result.TotalTests.Should().Be(1);
    }

    #endregion
}
