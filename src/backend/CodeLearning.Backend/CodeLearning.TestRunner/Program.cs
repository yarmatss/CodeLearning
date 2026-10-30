using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Models;

[JsonSerializable(typeof(ExecutionResult))]
internal partial class AppJsonContext : JsonSerializerContext { }

partial class Program
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
        "Exit status:"
    ];

    const string PeakMemoryMarker = "Maximum resident set size (kbytes):";

    static async Task<int> Main(string[] args)
    {
        string runCommand = "";
        string inputDir = "/app/io";
        string outputFile = "/app/result.json";
        int timeLimitMs = 2000;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--run" && i + 1 < args.Length) runCommand = args[++i];
            if (args[i] == "--input" && i + 1 < args.Length) inputDir = args[++i];
            if (args[i] == "--out" && i + 1 < args.Length) outputFile = args[++i];
            if (args[i] == "--timelimit" && i + 1 < args.Length) int.TryParse(args[++i], out timeLimitMs);
        }

        if (string.IsNullOrEmpty(runCommand)) return 1;

        var result = new ExecutionResult
        {
            Status = SubmissionStatus.Running,
            TestResults = new List<TestCaseResult>()
        };

        var totalSw = Stopwatch.StartNew();

        try
        {
            var inputFiles = Directory.GetFiles(inputDir, "input_*.txt")
                .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_')[1]))
                .ToList();

            long maxMemoryUsed = 0;

            foreach (var inputFile in inputFiles)
            {
                var testIndex = int.Parse(Path.GetFileNameWithoutExtension(inputFile).Split('_')[1]);
                var inputContent = await File.ReadAllTextAsync(inputFile);

                var testResult = await RunSingleTestAsync(runCommand, inputContent, timeLimitMs);
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

        var json = JsonSerializer.Serialize(result, AppJsonContext.Default.ExecutionResult);
        await File.WriteAllTextAsync(outputFile, json);

        return 0;
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

    static async Task<TestCaseResult> RunSingleTestAsync(string command, string input, int timeoutMs)
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

            var (peakMemoryKb, programErrors) = ParseTimeOutput(errorBuilder.ToString());
            result.MemoryUsedKB = (int)peakMemoryKb;

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