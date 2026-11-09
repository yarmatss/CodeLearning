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

    [Theory]
    [InlineData("1 2 \n3\t\n", "1 2\n3")]
    [InlineData("a\n\n\n", "a")]
    [InlineData("a   \r\nb  \r\n\r\n", "a\nb")]
    public void OutputsMatch_ShouldIgnoreTrailingWhitespaceOnEachLineAndTrailingBlankLines(string actual, string expected)
    {
        SubmissionGrader.OutputsMatch(actual, expected).Should().BeTrue();
    }

    [Theory]
    [InlineData("1 2", "12")]
    [InlineData("a\n\nb", "a\nb")]
    [InlineData("a\0", "a")]
    public void OutputsMatch_ShouldStillCompareInnerWhitespaceAndContent(string actual, string expected)
    {
        SubmissionGrader.OutputsMatch(actual, expected).Should().BeFalse();
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

        // Assert - the sandbox's own error text is never shown to the student
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.RuntimeError.Should().Be(SubmissionGrader.IncompleteExecutionMessage);
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
        testCases[0].IsPublic = true;
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
    public void Grade_MemoryLimitExceededOnOneTest_ShouldReportMemoryLimitExceeded()
    {
        // Arrange
        var testCases = CreateTestCases("1", "2");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults =
            [
                Ran(0, "1"),
                new TestCaseResult { TestIndex = 1, Status = TestResultStatus.MemoryLimitExceeded, MemoryUsedKB = 250_000 }
            ]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.MemoryLimitExceeded);
        result.TestResults[1].Status.Should().Be(TestResultStatus.MemoryLimitExceeded);
        result.Score.Should().Be(50);
    }

    [Fact]
    public void Grade_RuntimeErrorOnHiddenTest_ShouldNotExposeItsErrorOutput()
    {
        // Arrange - a submission can print a hidden test's input to stderr and fail on purpose
        var testCases = CreateTestCases("1", "2");
        testCases[0].IsPublic = true;
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults =
            [
                Ran(0, "1"),
                new TestCaseResult { TestIndex = 1, Status = TestResultStatus.RuntimeError, ErrorMessage = "hidden input: 42 17" }
            ]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.RuntimeError.Should().BeNull();
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

    #region Review Fix Tests

    [Fact]
    public void Grade_SandboxRuntimeErrorWithoutResults_ShouldNotPassItsTextThrough()
    {
        // Arrange - text the sandbox reported (possibly forged, possibly a hidden input)
        var testCases = CreateTestCases("3");
        testCases[0].IsPublic = true;
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.RuntimeError,
            RuntimeError = "hidden input: 42 17"
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.RuntimeError);
        result.RuntimeError.Should().Be(SubmissionGrader.IncompleteExecutionMessage);
        result.TestResults.Single().ErrorMessage.Should().Be(SubmissionGrader.NoResultMessage);
    }

    [Fact]
    public void Grade_CompilationError_ShouldNotPassRuntimeErrorTextThrough()
    {
        // Arrange
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.CompilationError,
            CompilationError = "",
            RuntimeError = "secret"
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.CompilationError.Should().Be(SubmissionGrader.CompilationFailedMessage);
        result.RuntimeError.Should().BeNull();
    }

    [Fact]
    public void Grade_VeryLongCompilationError_ShouldBeTruncated()
    {
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.CompilationError,
            CompilationError = new string('e', SubmissionGrader.MaxCompilationErrorLength * 2)
        };

        var result = SubmissionGrader.Grade(sandbox, CreateTestCases("3"));

        result.CompilationError!.Length.Should().BeLessThan(SubmissionGrader.MaxCompilationErrorLength + 100);
        result.CompilationError.Should().EndWith(BoundedOutputCapture.TruncationMarker);
    }

    [Theory]
    [InlineData(SubmissionStatus.TimeLimitExceeded, TestResultStatus.TimeLimitExceeded)]
    [InlineData(SubmissionStatus.MemoryLimitExceeded, TestResultStatus.MemoryLimitExceeded)]
    public void Grade_GlobalLimitWithPartialResults_ShouldTakePriorityOverNoResult(
        SubmissionStatus sandboxStatus,
        TestResultStatus expectedMissingStatus)
    {
        // Arrange - the container-level limit fired after the first test
        var testCases = CreateTestCases("3", "15", "7");
        var sandbox = new ExecutionResult
        {
            Status = sandboxStatus,
            TestResults = [Ran(0, "3")]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(sandboxStatus);
        result.TestResults[0].Status.Should().Be(TestResultStatus.Passed);
        result.TestResults.Skip(1).Should().OnlyContain(tr => tr.Status == expectedMissingStatus && tr.ErrorMessage == null);
        result.RuntimeError.Should().BeNull();
        result.Score.Should().Be(33);
    }

    [Fact]
    public void Grade_GlobalTimeLimitWithoutAnyResults_ShouldReportTimeLimitExceededForEveryTest()
    {
        var testCases = CreateTestCases("3", "15");
        var sandbox = new ExecutionResult { Status = SubmissionStatus.TimeLimitExceeded };

        var result = SubmissionGrader.Grade(sandbox, testCases);

        result.Status.Should().Be(SubmissionStatus.TimeLimitExceeded);
        result.TestResults.Should().HaveCount(2).And.OnlyContain(tr => tr.Status == TestResultStatus.TimeLimitExceeded);
    }

    [Theory]
    [InlineData("Traceback (most recent call last):\n  File \"solution.py\", line 1\nMemoryError")]
    [InlineData("Traceback (most recent call last):\nMemoryError: cannot allocate")]
    [InlineData("Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space")]
    public void Grade_RuntimeOutOfMemoryMessage_ShouldBecomeMemoryLimitExceeded(string errorOutput)
    {
        // Arrange
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults =
            [
                new TestCaseResult { TestIndex = 0, Status = TestResultStatus.RuntimeError, ErrorMessage = errorOutput }
            ]
        };

        // Act
        var result = SubmissionGrader.Grade(sandbox, testCases);

        // Assert
        result.Status.Should().Be(SubmissionStatus.MemoryLimitExceeded);
        result.TestResults.Single().Status.Should().Be(TestResultStatus.MemoryLimitExceeded);
    }

    [Fact]
    public void Grade_ErrorMentioningMemoryErrorInsideALine_ShouldStayRuntimeError()
    {
        var testCases = CreateTestCases("3");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults =
            [
                new TestCaseResult { TestIndex = 0, Status = TestResultStatus.RuntimeError, ErrorMessage = "ValueError: not a MemoryError" }
            ]
        };

        SubmissionGrader.Grade(sandbox, testCases).Status.Should().Be(SubmissionStatus.RuntimeError);
    }

    [Fact]
    public void Grade_OutputWithTrailingSpacesOnEachLine_ShouldPass()
    {
        var testCases = CreateTestCases("1 2\n3 4");
        var sandbox = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            TestResults = [Ran(0, "1 2 \n3 4 \n\n")]
        };

        SubmissionGrader.Grade(sandbox, testCases).Score.Should().Be(100);
    }

    #endregion
}
