using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using CodeLearning.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeLearning.TestRunner;

[JsonSerializable(typeof(ExecutionResult))]
internal partial class AppJsonContext : JsonSerializerContext { }

// Usage:
//   runner --run "<command>" --input <dir with input_N.txt> --out <result.json>
//          [--timelimit <ms per test, default 2000>] [--memorylimitkb <kb, 0 = unknown>]
//          [--totaltimelimit <ms for all tests, 0 = none>] [--workdir <dir, default: current directory>]
// The per-test statistics of /usr/bin/time go to time-<n>.txt next to --out, so that directory
// must not be writable by the submission
static partial class Program
{
    // Expected outputs are capped at 10,000 characters, so this always fits a correct answer
    const int MaxStdoutBytes = 32 * 1024;
    const int MaxStderrBytes = 8 * 1024;

    // How long the pipes may stay open after the process exited (or was killed) before the reads are abandoned
    static readonly TimeSpan PipeDrainTimeout = TimeSpan.FromSeconds(2);

    static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options == null)
        {
            await Console.Error.WriteLineAsync("Usage: runner --run <command> --input <dir> --out <file> [--timelimit <ms>] "
                                    + "[--memorylimitkb <kb>] [--totaltimelimit <ms>] [--workdir <dir>]");
            return 1;
        }

        // Non-dumpable keeps the submission from reading this process's memory or file descriptors
        // through /proc, where the test inputs are held
        MakeNonDumpable();

        var result = await RunAllTestsAsync(options);
        await WriteResultAsync(options.OutputFile, result);

        return 0;
    }

    sealed record RunnerOptions(
        string RunCommand,
        string InputDir,
        string OutputFile,
        string StatsDir,
        string? WorkDir,
        int TimeLimitMs,
        long MemoryLimitKb,
        int TotalTimeLimitMs);

    static RunnerOptions? ParseArgs(string[] args)
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

        if (!values.TryGetValue("--run", out var run) || string.IsNullOrWhiteSpace(run)
            || !values.TryGetValue("--input", out var input) || string.IsNullOrWhiteSpace(input)
            || !values.TryGetValue("--out", out var output) || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var timeLimitMs = values.TryGetValue("--timelimit", out var limit) && int.TryParse(limit, out var parsed) && parsed > 0
            ? parsed
            : 2000;

        // 0 means unknown: memory kills are then reported as runtime errors
        var memoryLimitKb = values.TryGetValue("--memorylimitkb", out var memory) && long.TryParse(memory, out var parsedMemory)
            ? parsedMemory
            : 0;

        // 0 means no budget for the whole run; the container-level timeout is then the only cap
        var totalTimeLimitMs = values.TryGetValue("--totaltimelimit", out var total) && int.TryParse(total, out var parsedTotal)
            ? Math.Max(0, parsedTotal)
            : 0;

        var outputFile = Path.GetFullPath(output);

        return new RunnerOptions(
            run,
            input,
            outputFile,
            Path.GetDirectoryName(outputFile) ?? "/",
            values.GetValueOrDefault("--workdir"),
            timeLimitMs,
            memoryLimitKb,
            totalTimeLimitMs);
    }

    static async Task<ExecutionResult> RunAllTestsAsync(RunnerOptions options)
    {
        var result = new ExecutionResult
        {
            Status = SubmissionStatus.Running,
            TestResults = new List<TestCaseResult>()
        };

        var totalSw = Stopwatch.StartNew();

        try
        {
            var inputs = await LoadAndRemoveInputsAsync(options.InputDir);

            long maxMemoryUsed = 0;

            foreach (var (testIndex, inputContent) in inputs)
            {
                var timeoutMs = options.TimeLimitMs;

                if (options.TotalTimeLimitMs > 0)
                {
                    var remainingMs = options.TotalTimeLimitMs - (int)totalSw.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                    {
                        // Out of budget for the whole run: the rest are reported instead of being cut off by the container timeout
                        result.TestResults.Add(new TestCaseResult
                        {
                            TestIndex = testIndex,
                            Status = TestResultStatus.TimeLimitExceeded,
                            ExecutionTimeMs = 0
                        });
                        continue;
                    }

                    timeoutMs = Math.Min(timeoutMs, remainingMs);
                }

                var statsFile = Path.Combine(options.StatsDir, $"time-{testIndex}.txt");
                var testResult = await RunSingleTestAsync(options, inputContent, timeoutMs, statsFile);
                testResult.TestIndex = testIndex;
                KillLeftoverProcesses();

                if (testResult.MemoryUsedKB.HasValue && testResult.MemoryUsedKB.Value > maxMemoryUsed)
                    maxMemoryUsed = testResult.MemoryUsedKB.Value;

                result.TestResults.Add(testResult);
                result.TotalExecutionTimeMs = (int?)totalSw.ElapsedMilliseconds;
                result.MaxMemoryUsedKB = (int?)maxMemoryUsed;

                // Partial results survive a container-level timeout; the final write replaces them
                await WriteResultAsync(options.OutputFile, result);
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

    // Written to a temporary file first, so a reader never sees a half-written result
    static async Task WriteResultAsync(string outputFile, ExecutionResult result)
    {
        var json = JsonSerializer.Serialize(result, AppJsonContext.Default.ExecutionResult);
        var tempFile = outputFile + ".tmp";
        await File.WriteAllTextAsync(tempFile, json);
        File.Move(tempFile, outputFile, overwrite: true);
    }

    // Every input is read before any submission process starts, then deleted, so the submission
    // only ever sees the input of the test it is running, on stdin
    static async Task<List<(int TestIndex, byte[] Content)>> LoadAndRemoveInputsAsync(string inputDir)
    {
        var inputs = new List<(int TestIndex, byte[] Content)>();

        foreach (var inputFile in Directory.GetFiles(inputDir, "input_*.txt"))
        {
            var testIndex = int.Parse(Path.GetFileNameWithoutExtension(inputFile).Split('_')[1]);
            inputs.Add((testIndex, await File.ReadAllBytesAsync(inputFile)));
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

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint Geteuid();

    const int SIGKILL = 9;

    // Submission processes that daemonized escape Process.Kill(entireProcessTree)
    static void KillLeftoverProcesses()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (Geteuid() != 0)
        {
            // The submission runs as this agent's user: kill(-1) signals every process this user can reach
            // except PID 1 and the agent itself. Fails with ESRCH when nothing is left, the normal case
            _ = Kill(-1, SIGKILL);
            return;
        }

        // As root, kill(-1) would also take down the bootstrap script, so only processes running under
        // another user (the dropped-privilege submission) are killed. Repeated to catch processes forked meanwhile
        for (var round = 0; round < 10; round++)
        {
            var killedAny = false;

            foreach (var pid in EnumerateNonRootProcesses())
            {
                killedAny |= Kill(pid, SIGKILL) == 0;
            }

            if (!killedAny)
            {
                return;
            }
        }
    }

    static IEnumerable<int> EnumerateNonRootProcesses()
    {
        var self = Environment.ProcessId;

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == self || pid == 1)
            {
                continue;
            }

            string[] status;
            try
            {
                status = File.ReadAllLines(Path.Combine(directory, "status"));
            }
            catch (IOException)
            {
                continue; // exited meanwhile
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // "Uid:\t<real>\t<effective>\t<saved>\t<filesystem>"
            var uidLine = status.FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal));
            var uids = uidLine?.Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries).Skip(1);

            if (uids != null && uids.Any(uid => uid != "0"))
            {
                yield return pid;
            }
        }
    }

    static async Task<TestCaseResult> RunSingleTestAsync(RunnerOptions options, byte[] input, int timeoutMs, string statsFile)
    {
        var result = new TestCaseResult { ActualOutput = "" };

        TryDelete(statsFile);

        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/time",
            // -o keeps the statistics out of the submission's stderr, which it controls and which is capped
            Arguments = $"-v -o \"{statsFile}\" {options.RunCommand}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = options.WorkDir ?? ""
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = new BoundedOutputCapture(MaxStdoutBytes);
        var stderr = new BoundedOutputCapture(MaxStderrBytes);

        try
        {
            process.Start();

            // The time limit starts before any input is written: a program that never reads stdin
            // must not be able to stall the agent on a full pipe
            using var timeout = new CancellationTokenSource(timeoutMs);
            var sw = Stopwatch.StartNew();

            var stdoutTask = stdout.CopyFromAsync(process.StandardOutput.BaseStream);
            var stderrTask = stderr.CopyFromAsync(process.StandardError.BaseStream);
            var stdinTask = WriteInputAsync(process.StandardInput.BaseStream, input);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                TryKillTree(process);
            }

            sw.Stop();

            // Children left behind may still hold the pipes open
            KillLeftoverProcesses();
            await WaitQuietlyAsync(Task.WhenAll(stdoutTask, stderrTask, stdinTask), PipeDrainTimeout);

            if (timedOut)
            {
                TryDelete(statsFile);
                result.Status = TestResultStatus.TimeLimitExceeded;
                result.ExecutionTimeMs = timeoutMs;
                return result;
            }

            var stats = await ReadStatsAsync(statsFile);
            var errorOutput = stderr.GetText();

            result.ExecutionTimeMs = (int?)sw.ElapsedMilliseconds;
            result.MemoryUsedKB = (int)stats.PeakMemoryKb;
            result.Status = TestRunVerdict.Classify(process.ExitCode, stats, errorOutput, options.MemoryLimitKb);

            switch (result.Status)
            {
                case TestResultStatus.RuntimeError:
                    result.ErrorMessage = string.IsNullOrWhiteSpace(errorOutput)
                        ? DescribeFailure(process.ExitCode, stats)
                        : errorOutput.Trim();
                    break;
                case TestResultStatus.Failed:
                    // Judged by SubmissionGrader outside the sandbox
                    result.ActualOutput = stdout.GetText();
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Status = TestResultStatus.RuntimeError;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    static string DescribeFailure(int exitCode, TimeStats stats) =>
        stats.TerminatingSignal is { } signal
            ? $"Process terminated by signal {signal}"
            : $"Process exited with code {exitCode}";

    static async Task WriteInputAsync(Stream stdin, byte[] input)
    {
        try
        {
            await stdin.WriteAsync(input);
            await stdin.FlushAsync();
        }
        catch (IOException)
        {
            // EPIPE: the program exited or closed stdin without reading all of it
        }
        catch (ObjectDisposedException)
        {
            // The process was killed and its stdin pipe disposed while the input was still being written
        }
        finally
        {
            try
            {
                stdin.Close();
            }
            catch (IOException)
            {
                // EPIPE again on close: the reader is already gone, so there is nothing left to flush
            }
        }
    }

    static async Task WaitQuietlyAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout);
        }
        catch (Exception)
        {
            // A read or write that never finished only loses output past what was captured
        }
    }

    static async Task<TimeStats> ReadStatsAsync(string statsFile)
    {
        try
        {
            return TimeStats.Parse(await File.ReadAllTextAsync(statsFile));
        }
        catch (IOException)
        {
            return TimeStats.Unknown;
        }
        finally
        {
            TryDelete(statsFile);
        }
    }

    static void TryKillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already exited
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a leftover stats file in the agent's private tmpfs is discarded with the container
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above; the file is never read again
        }
    }
}
