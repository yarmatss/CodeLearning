using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Models;

[JsonSerializable(typeof(ExecutionResult))]
internal partial class AppJsonContext : JsonSerializerContext { }

class Program
{
    const int MAX_OUTPUT_CHARS = 10000;

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

            int passedTests = 0;
            bool hasRuntimeError = false;
            bool hasTimeLimitExceeded = false;
            long maxMemoryUsed = 0;

            foreach (var inputFile in inputFiles)
            {
                var testIndexStr = Path.GetFileNameWithoutExtension(inputFile).Split('_')[1];
                var expectedFile = Path.Combine(inputDir, $"expected_{testIndexStr}.txt");
                if (!File.Exists(expectedFile)) continue;

                var inputContent = await File.ReadAllTextAsync(inputFile);
                var expectedOutput = (await File.ReadAllTextAsync(expectedFile)).Trim();

                var testResult = await RunSingleTestAsync(runCommand, inputContent, expectedOutput, timeLimitMs);

                if (testResult.MemoryUsedKB.HasValue && testResult.MemoryUsedKB.Value > maxMemoryUsed)
                    maxMemoryUsed = testResult.MemoryUsedKB.Value;

                if (testResult.Status == TestResultStatus.Passed) passedTests++;
                else if (testResult.Status == TestResultStatus.RuntimeError) hasRuntimeError = true;
                else if (testResult.Status == TestResultStatus.TimeLimitExceeded) hasTimeLimitExceeded = true;

                result.TestResults.Add(testResult);
            }

            if (hasTimeLimitExceeded) result.Status = SubmissionStatus.TimeLimitExceeded;
            else if (hasRuntimeError) result.Status = SubmissionStatus.RuntimeError;
            else result.Status = SubmissionStatus.Completed;

            result.TotalExecutionTimeMs = (int?)totalSw.ElapsedMilliseconds;
            result.MaxMemoryUsedKB = (int?)maxMemoryUsed;
            result.Score = inputFiles.Count > 0 ? (int)((double)passedTests / inputFiles.Count * 100) : 0;
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

    static async Task<TestCaseResult> RunSingleTestAsync(string command, string input, string expectedOutput, int timeoutMs)
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

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                if (outputBuilder.Length < MAX_OUTPUT_CHARS) outputBuilder.AppendLine(e.Data);
                else if (outputBuilder.Length == MAX_OUTPUT_CHARS) outputBuilder.AppendLine("... [Output Truncated]");
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                if (errorBuilder.Length < MAX_OUTPUT_CHARS) errorBuilder.AppendLine(e.Data);
            }
        };

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
                process.Kill();
                result.Status = TestResultStatus.TimeLimitExceeded;
                result.ExecutionTimeMs = timeoutMs;
                return result;
            }

            sw.Stop();
            result.ExecutionTimeMs = (int?)sw.ElapsedMilliseconds;

            string errorOutput = errorBuilder.ToString();
            long peakMemoryKb = 0;
            var cleanErrorBuilder = new StringBuilder();

            using (var reader = new StringReader(errorOutput))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("Maximum resident set size (kbytes):"))
                    {
                        var parts = line.Split(':', StringSplitOptions.TrimEntries);
                        if (parts.Length > 1 && long.TryParse(parts[1], out long kb))
                        {
                            peakMemoryKb = kb;
                        }
                    }
                    else if (line.Contains("Command being timed:") ||
                             line.Contains("User time (seconds):") ||
                             line.Contains("System time (seconds):") ||
                             line.Contains("Percent of CPU this job got:") ||
                             line.Contains("Elapsed (wall clock) time") ||
                             line.Contains("Average shared text size") ||
                             line.Contains("Average unshared data size") ||
                             line.Contains("Average stack size") ||
                             line.Contains("Average total size") ||
                             line.Contains("Average resident set size") ||
                             line.Contains("Major (requiring I/O) page faults") ||
                             line.Contains("Minor (reclaiming a frame) page faults") ||
                             line.Contains("Voluntary context switches") ||
                             line.Contains("Involuntary context switches") ||
                             line.Contains("Swaps") ||
                             line.Contains("File system inputs") ||
                             line.Contains("File system outputs") ||
                             line.Contains("Socket messages sent") ||
                             line.Contains("Socket messages received") ||
                             line.Contains("Signals delivered") ||
                             line.Contains("Page size (bytes)") ||
                             line.Contains("Exit status:"))
                    {
                        // lines from time, ignore
                    }
                    else
                    {
                        // actual error message from the program
                        cleanErrorBuilder.AppendLine(line);
                    }
                }
            }

            result.MemoryUsedKB = (int)peakMemoryKb;

            if (process.ExitCode != 0)
            {
                result.Status = TestResultStatus.RuntimeError;
                result.ErrorMessage = cleanErrorBuilder.ToString().Trim();
                return result;
            }

            var actualOutput = outputBuilder.ToString().Trim();
            result.ActualOutput = actualOutput;

            if (actualOutput.Replace("\r\n", "\n") == expectedOutput.Replace("\r\n", "\n"))
            {
                result.Status = TestResultStatus.Passed;
            }
            else
            {
                result.Status = TestResultStatus.Failed;
            }
        }
        catch (Exception ex)
        {
            result.Status = TestResultStatus.RuntimeError;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }
}