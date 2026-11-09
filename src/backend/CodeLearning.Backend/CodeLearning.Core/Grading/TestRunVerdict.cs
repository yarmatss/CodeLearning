using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Grading;

public static class TestRunVerdict
{
    private const int SigKill = 9;

    // Status of one test run that finished within its time limit. A run that exits normally is reported
    // as Failed: its output is judged against the expected output outside the sandbox
    public static TestResultStatus Classify(int exitCode, TimeStats stats, string? stderr, long memoryLimitKb)
    {
        // The cgroup OOM killer ends the process with SIGKILL; near the limit, that is a memory verdict
        if (stats.TerminatingSignal == SigKill
            && memoryLimitKb > 0
            && stats.PeakMemoryKb >= memoryLimitKb * 9 / 10)
        {
            return TestResultStatus.MemoryLimitExceeded;
        }

        var failed = exitCode != 0 || stats.TerminatingSignal != null;

        if (failed && IsOutOfMemoryMessage(stderr))
        {
            return TestResultStatus.MemoryLimitExceeded;
        }

        return failed ? TestResultStatus.RuntimeError : TestResultStatus.Failed;
    }

    // Runtimes that hit their own heap limit before the cgroup does report it themselves
    public static bool IsOutOfMemoryMessage(string? errorOutput)
    {
        if (string.IsNullOrEmpty(errorOutput))
        {
            return false;
        }

        if (errorOutput.Contains("java.lang.OutOfMemoryError", StringComparison.Ordinal))
        {
            return true;
        }

        using var reader = new StringReader(errorOutput);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Last line of a Python traceback: "MemoryError" or "MemoryError: <details>"
            if (line == "MemoryError" || line.StartsWith("MemoryError:", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
