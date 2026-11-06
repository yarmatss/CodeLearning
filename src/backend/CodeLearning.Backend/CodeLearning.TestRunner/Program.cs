using CodeLearning.Core.Enums;
using CodeLearning.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeLearning.TestRunner;

[JsonSerializable(typeof(ExecutionResult))]
internal partial class AppJsonContext : JsonSerializerContext { }

static partial class Program
{
    const int MAX_OUTPUT_CHARS = 10000;

    // Lines that /usr/bin/time -v appends to stderr; everything else is the program's own error output
    static readonly string[] TimeOutputMarkers =
    [
        "Command being timed:",
        "User time (seconds):",
        "System time (seconds):",
        "Percent of CPU this job got:",
        "Elapsed (wall clock) time",
        "Average shared text size",
        "Average unshared data size",
        "Average stack size",
        "Average total size",
        "Average resident set size",
        "Major (requiring I/O) page faults",
        "Minor (reclaiming a frame) page faults",
        "Voluntary context switches",
        "Involuntary context switches",
        "Swaps",
        "File system inputs",
        "File system outputs",
        "Socket messages sent",
        "Socket messages received",
        "Signals delivered",
        "Page size (bytes)",
        "Exit status:",
        "Command terminated by signal"
    ];

    const string PeakMemoryMarker = "Maximum resident set size (kbytes):";

    // Printed by /usr/bin/time when the command was killed by SIGKILL
    const string KilledBySigKillMarker = "Command terminated by signal 9";

    static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        if (string.IsNullOrEmpty(options.RunCommand)) return 1;

        // The submission runs as the same user as this agent. Non-dumpable keeps it from reading this
        // process's memory or file descriptors through /proc, where the test inputs are held
        MakeNonDumpable();

        var result = await RunAllTestsAsync(options);

        var json = JsonSerializer.Serialize(result, AppJsonContext.Default.ExecutionResult);
        await File.WriteAllTextAsync(options.OutputFile, json);

        return 0;
    }

    sealed record RunnerOptions(string RunCommand, string InputDir, string OutputFile, int TimeLimitMs, long MemoryLimitKb);

    static RunnerOptions ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string>();
        var i = 0;
        while (i < args.Length)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length)
            {
                values[args[i]] = args[i + 1];
                i += 2;
            }
            else
            {
                i++;
            }
        }

        var timeLimitMs = values.TryGetValue("--timelimit", out var limit) && int.TryParse(limit, out var parsed)
            ? parsed
            : 2000;

        // 0 means unknown: memory kills are then reported as runtime errors
        var memoryLimitKb = values.TryGetValue("--memorylimitkb", out var memory) && long.TryParse(memory, out var parsedMemory)
            ? parsedMemory
            : 0;

        return new RunnerOptions(
            values.GetValueOrDefault("--run", ""),
            values.GetValueOrDefault("--input", "/app/io"),
            values.GetValueOrDefault("--out", "/app/result.json"),
            timeLimitMs,
            memoryLimitKb);
    }

    static async Task<ExecutionResult> RunAllTestsAsync(RunnerOptions options)
    {
        var runCommand = options.RunCommand;
        var inputDir = options.InputDir;
        var timeLimitMs = options.TimeLimitMs;

        var result = new ExecutionResult
        {
            Status = SubmissionStatus.Running,
            TestResults = new List<TestCaseResult>()
        };

        var totalSw = Stopwatch.StartNew();

        try
        {
            var inputs = await LoadAndRemoveInputsAsync(inputDir);

            long maxMemoryUsed = 0;

            foreach (var (testIndex, inputContent) in inputs)
            {
                var testResult = await RunSingleTestAsync(runCommand, inputContent, timeLimitMs, options.MemoryLimitKb);
                testResult.TestIndex = testIndex;
                KillLeftoverProcesses();

                if (testResult.MemoryUsedKB.HasValue && testResult.MemoryUsedKB.Value > maxMemoryUsed)
                    maxMemoryUsed = testResult.MemoryUsedKB.Value;

                result.TestResults.Add(testResult);
            }

            result.Status = SubmissionStatus.Completed;
            result.TotalExecutionTimeMs = (int?)totalSw.ElapsedMilliseconds;
            result.MaxMemoryUsedKB = (int?)maxMemoryUsed;
        }
        catch (Exception ex)
        {
            result.Status = SubmissionStatus.RuntimeError;
            result.RuntimeError = "Internal Runner Error: " + ex.Message;
        }

        return result;
    }

    // Every input is read before any submission process starts, then deleted, so the submission
    // (which can read /app) only ever sees the input of the test it is running, on stdin
    static async Task<List<(int TestIndex, string Content)>> LoadAndRemoveInputsAsync(string inputDir)
    {
        var inputs = new List<(int TestIndex, string Content)>();

        foreach (var inputFile in Directory.GetFiles(inputDir, "input_*.txt"))
        {
            var testIndex = int.Parse(Path.GetFileNameWithoutExtension(inputFile).Split('_')[1]);
            inputs.Add((testIndex, await File.ReadAllTextAsync(inputFile)));
            File.Delete(inputFile);
        }

        return inputs.OrderBy(i => i.TestIndex).ToList();
    }

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    const int PR_SET_DUMPABLE = 4;

    static void MakeNonDumpable()
    {
        if (OperatingSystem.IsLinux() && Prctl(PR_SET_DUMPABLE, 0, 0, 0, 0) != 0)
        {
            // Not fatal: grading still works, only this extra isolation is missing
            Console.Error.WriteLine($"prctl(PR_SET_DUMPABLE) failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int sig);

    const int SIGKILL = 9;

    // Submission processes that daemonized escape Process.Kill(entireProcessTree).
    // kill(-1) signals every process this user can reach except PID 1 and the runner itself.
    static void KillLeftoverProcesses()
    {
        if (OperatingSystem.IsLinux())
        {
            // Fails with ESRCH when nothing is left to kill, which is the normal case
            _ = Kill(-1, SIGKILL);
        }
    }

    static void AppendLimited(StringBuilder builder, string? line, bool markTruncation)
    {
        if (line == null) return;

        if (builder.Length < MAX_OUTPUT_CHARS) builder.AppendLine(line);
        else if (markTruncation && builder.Length == MAX_OUTPUT_CHARS) builder.AppendLine("... [Output Truncated]");
    }

    static (long PeakMemoryKb, string ProgramErrors) ParseTimeOutput(string stderr)
    {
        long peakMemoryKb = 0;
        var programErrors = new StringBuilder();

        using var reader = new StringReader(stderr);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains(PeakMemoryMarker))
            {
                var parts = line.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length > 1 && long.TryParse(parts[1], out long kb))
                {
                    peakMemoryKb = kb;
                }
            }
            else if (!TimeOutputMarkers.Any(line.Contains))
            {
                programErrors.AppendLine(line);
            }
        }

        return (peakMemoryKb, programErrors.ToString().Trim());
    }

    static async Task<TestCaseResult> RunSingleTestAsync(string command, string input, int timeoutMs, long memoryLimitKb)
    {
        var result = new TestCaseResult { ActualOutput = "" };

        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/time",
            Arguments = $"-v {command}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = "/app"
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) => AppendLimited(outputBuilder, e.Data, markTruncation: true);
        process.ErrorDataReceived += (sender, e) => AppendLimited(errorBuilder, e.Data, markTruncation: false);

        var sw = Stopwatch.StartNew();

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                result.Status = TestResultStatus.TimeLimitExceeded;
                result.ExecutionTimeMs = timeoutMs;
                return result;
            }

            sw.Stop();
            result.ExecutionTimeMs = (int?)sw.ElapsedMilliseconds;

            var stderr = errorBuilder.ToString();
            var (peakMemoryKb, programErrors) = ParseTimeOutput(stderr);
            result.MemoryUsedKB = (int)peakMemoryKb;

            // The cgroup OOM killer ends the process with SIGKILL; near the limit, that is a memory verdict
            if (process.ExitCode != 0
                && stderr.Contains(KilledBySigKillMarker)
                && memoryLimitKb > 0
                && peakMemoryKb >= memoryLimitKb * 9 / 10)
            {
                result.Status = TestResultStatus.MemoryLimitExceeded;
                return result;
            }

            if (process.ExitCode != 0)
            {
                result.Status = TestResultStatus.RuntimeError;
                result.ErrorMessage = programErrors;
                return result;
            }

            result.ActualOutput = outputBuilder.ToString().Trim();
            // Judged by SubmissionGrader outside the sandbox
            result.Status = TestResultStatus.Failed;
        }
        catch (Exception ex)
        {
            result.Status = TestResultStatus.RuntimeError;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}