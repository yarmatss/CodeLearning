extern alias Runner;

using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using CodeLearning.Core.Text;
using FluentAssertions;
using Npgsql;
using Runner::CodeLearning.Runner.Consumers;
using System.Text;

namespace CodeLearning.Tests.Unit.Grading;

public class TestRunnerHelpersTests
{
    private const string TimeReport = """
        	Command being timed: "python3 solution.py"
        	User time (seconds): 0.01
        	Maximum resident set size (kbytes): 9876
        	Exit status: 0
        """;

    #region TimeStats

    [Fact]
    public void TimeStats_ShouldReadPeakMemoryAndExitStatus()
    {
        var stats = TimeStats.Parse(TimeReport);

        stats.PeakMemoryKb.Should().Be(9876);
        stats.ExitStatus.Should().Be(0);
        stats.TerminatingSignal.Should().BeNull();
    }

    [Fact]
    public void TimeStats_ShouldReadTerminatingSignal()
    {
        var stats = TimeStats.Parse("Command terminated by signal 9\n\tMaximum resident set size (kbytes): 250000\n");

        stats.TerminatingSignal.Should().Be(9);
        stats.PeakMemoryKb.Should().Be(250000);
    }

    [Fact]
    public void TimeStats_ShouldUseOnlyTheFirstOccurrence_SoAppendedLinesCannotOverrideIt()
    {
        var stats = TimeStats.Parse(TimeReport + "\tMaximum resident set size (kbytes): 1\nCommand terminated by signal 9\n");

        stats.PeakMemoryKb.Should().Be(9876);
        stats.TerminatingSignal.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void TimeStats_MissingOrUnreadableReport_ShouldBeUnknown(string? report)
    {
        TimeStats.Parse(report).Should().Be(TimeStats.Unknown);
    }

    #endregion

    #region BoundedOutputCapture

    [Fact]
    public async Task BoundedOutputCapture_ShouldStopStoringAtTheLimitButDrainTheStream()
    {
        // Arrange - 1 MB flood against a 1 KB cap
        var source = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 1024 * 1024)));
        var capture = new BoundedOutputCapture(1024);

        // Act
        await capture.CopyFromAsync(source);

        // Assert
        source.Position.Should().Be(source.Length);
        capture.Truncated.Should().BeTrue();
        var text = capture.GetText();
        text.Should().StartWith(new string('x', 1024)).And.EndWith(BoundedOutputCapture.TruncationMarker);
        text.Length.Should().Be(1024 + 1 + BoundedOutputCapture.TruncationMarker.Length);
    }

    [Fact]
    public async Task BoundedOutputCapture_OutputWithinLimit_ShouldBeKeptWhole()
    {
        var capture = new BoundedOutputCapture(1024);

        await capture.CopyFromAsync(new MemoryStream(Encoding.UTF8.GetBytes("hello\0world\n")));

        capture.Truncated.Should().BeFalse();
        capture.GetText().Should().Be("hello\0world\n");
    }

    [Fact]
    public void BoundedOutputCapture_ExactlyAtLimit_ShouldNotBeMarkedTruncated()
    {
        var capture = new BoundedOutputCapture(4);

        capture.Append("abcd"u8);

        capture.Truncated.Should().BeFalse();
        capture.GetText().Should().Be("abcd");
    }

    #endregion

    #region TestRunVerdict

    [Fact]
    public void Verdict_CleanExit_ShouldBeLeftForTheGrader()
    {
        TestRunVerdict.Classify(0, TimeStats.Parse(TimeReport), "", 262144)
            .Should().Be(TestResultStatus.Failed);
    }

    [Fact]
    public void Verdict_SigKillNearTheMemoryLimit_ShouldBeMemoryLimitExceeded()
    {
        var stats = new TimeStats(PeakMemoryKb: 250_000, TerminatingSignal: 9, ExitStatus: null);

        // stderr is irrelevant: the statistics come from time's own file, not the program's stderr
        TestRunVerdict.Classify(137, stats, new string('E', 8192), 262144)
            .Should().Be(TestResultStatus.MemoryLimitExceeded);
    }

    [Fact]
    public void Verdict_SigKillFarBelowTheMemoryLimit_ShouldBeRuntimeError()
    {
        var stats = new TimeStats(PeakMemoryKb: 10_000, TerminatingSignal: 9, ExitStatus: null);

        TestRunVerdict.Classify(137, stats, "", 262144).Should().Be(TestResultStatus.RuntimeError);
    }

    [Fact]
    public void Verdict_PythonMemoryError_ShouldBeMemoryLimitExceeded()
    {
        TestRunVerdict.Classify(1, TimeStats.Unknown, "Traceback (most recent call last):\nMemoryError\n", 0)
            .Should().Be(TestResultStatus.MemoryLimitExceeded);
    }

    [Fact]
    public void Verdict_NonZeroExit_ShouldBeRuntimeError()
    {
        TestRunVerdict.Classify(1, TimeStats.Unknown, "ZeroDivisionError: division by zero", 262144)
            .Should().Be(TestResultStatus.RuntimeError);
    }

    #endregion

    #region TextSanitizer

    [Theory]
    [InlineData("a\0b\0", "ab")]
    [InlineData("\0", "")]
    [InlineData("plain", "plain")]
    [InlineData("emoji \U0001F600", "emoji \U0001F600")]
    public void TextSanitizer_ShouldDropNulCharacters(string input, string expected)
    {
        TextSanitizer.ForDatabase(input).Should().Be(expected);
    }

    [Fact]
    public void TextSanitizer_ShouldReplaceLoneSurrogates()
    {
        TextSanitizer.ForDatabase("a\uD800b\uDC00").Should().Be("a�b�");
    }

    [Fact]
    public void TextSanitizer_Null_ShouldStayNull()
    {
        TextSanitizer.ForDatabase(null).Should().BeNull();
    }

    #endregion

    #region NonRetryableErrors

    [Theory]
    [InlineData(PostgresErrorCodes.CharacterNotInRepertoire, true)]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation, true)]
    [InlineData(PostgresErrorCodes.ConnectionFailure, false)]
    [InlineData(PostgresErrorCodes.UniqueViolation, false)]
    public void NonRetryableErrors_ShouldClassifyPostgresDataErrors(string sqlState, bool expected)
    {
        var postgres = new PostgresException("boom", "ERROR", "ERROR", sqlState);

        // EF Core wraps the provider exception
        NonRetryableErrors.IsNonRetryable(new InvalidOperationException("save failed", postgres))
            .Should().Be(expected);
    }

    [Fact]
    public void NonRetryableErrors_InfrastructureFailure_ShouldBeRetried()
    {
        NonRetryableErrors.IsNonRetryable(new InvalidOperationException("Docker is unavailable"))
            .Should().BeFalse();
    }

    #endregion
}
