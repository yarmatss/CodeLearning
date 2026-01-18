using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Runner.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Formats.Tar;
using System.Text;
using System.Text.Json;

namespace CodeLearning.Runner.Services;

public class DockerRunner : IDockerRunner
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerRunner> _logger;

    public DockerRunner(IConfiguration configuration, ILogger<DockerRunner> logger)
    {
        var dockerHost = configuration["ExecutionSettings:DockerHost"]!;

        _client = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        Models.ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        string? containerId = null;

        try
        {
            VerifyWorkspaceFiles(context.WorkspaceDirectory);

            containerId = await CreateContainerAsync(context, cancellationToken);

            await CopyFilesToContainerAsync(containerId, context.WorkspaceDirectory, cancellationToken);

            await _client.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                cancellationToken);

            _logger.LogInformation(
                "Started container {ContainerId} for submission {SubmissionId}",
                containerId,
                context.Submission.Id);

            // Wait for container to complete (with timeout buffer for compilation + all tests)
            // For compiled languages, add extra time for compilation
            var compilationOverhead = !string.IsNullOrWhiteSpace(context.Language.CompileCommand) ? 15 : 0;
            var totalTimeout = (context.Language.TimeoutSeconds * context.TestCases.Count) + compilationOverhead + 10;
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(totalTimeout));

            bool completed;
            try
            {
                await _client.Containers.WaitContainerAsync(containerId, cts.Token);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                completed = false;
            }

            if (!completed)
            {
                _logger.LogWarning(
                    "Container {ContainerId} timed out after {Timeout}s (expected max: {TotalTimeout}s)",
                    containerId,
                    context.Language.TimeoutSeconds,
                    totalTimeout);

                await _client.Containers.KillContainerAsync(
                    containerId,
                    new ContainerKillParameters(),
                    cancellationToken);

                return new ExecutionResult
                {
                    Status = SubmissionStatus.TimeLimitExceeded,
                    TotalExecutionTimeMs = context.Language.TimeoutSeconds * 1000
                };
            }

            var logs = await GetContainerLogsAsync(containerId, cancellationToken);

            _logger.LogDebug("Container output (first 1000 chars): {Output}", 
                logs.Length > 1000 ? logs.Substring(0, 1000) : logs);

            var result = ParseExecutionOutput(logs, context.TestCases);

            _logger.LogInformation(
                "Container {ContainerId} completed with status {Status}, total time {Time}ms, peak memory {MemoryKB}KB",
                containerId,
                result.Status,
                result.TotalExecutionTimeMs ?? 0,
                result.MaxMemoryUsedKB ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing submission {SubmissionId}",
                context.Submission.Id);

            return new ExecutionResult
            {
                Status = SubmissionStatus.RuntimeError,
                RuntimeError = ex.Message
            };
        }
        finally
        {
            if (containerId != null)
            {
                await CleanupContainerAsync(containerId, cancellationToken);
            }
        }
    }

    private void VerifyWorkspaceFiles(string workspaceDirectory)
    {
        if (!Directory.Exists(workspaceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Workspace directory does not exist: {workspaceDirectory}");
        }

        var files = Directory.GetFiles(workspaceDirectory);

        _logger.LogDebug(
            "Workspace directory {WorkspaceDir} contains {FileCount} files: {Files}",
            workspaceDirectory,
            files.Length,
            string.Join(", ", files.Select(Path.GetFileName)));

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"Workspace directory is empty: {workspaceDirectory}");
        }
    }

    private async Task<string> CreateContainerAsync(
        Models.ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var runCommandParts = context.Language.RunCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var createParams = new CreateContainerParameters
        {
            Image = context.Language.DockerImage,
            Cmd = runCommandParts,
            WorkingDir = "/app",
            
            // Run as non-root user inside container
            User = "65534:65534", // nobody:nogroup
            
            Labels = new Dictionary<string, string>
            {
                { "app", "codelearning" },
                { "submission-id", context.Submission.Id.ToString() },
                { "student-id", context.Submission.StudentId.ToString() },
                { "language", context.Language.Name }
            },

            HostConfig = new HostConfig
            {
                // Network isolation
                NetworkMode = "none",

                // Resource limits
                Memory = context.Language.MemoryLimitMB * 1024 * 1024,
                MemorySwap = context.Language.MemoryLimitMB * 1024 * 1024,
                NanoCPUs = (long)(context.Language.CpuLimit * 1_000_000_000),
                PidsLimit = 50,

                // Filesystem isolation
                Tmpfs = new Dictionary<string, string>
                {
                    { "/tmp", "size=10m,mode=1777,noexec,nosuid" }
                },

                // Privilege restrictions
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string>
                {
                    "no-new-privileges",
                },
                
                // Read-only /proc and /sys
                ReadonlyPaths = new List<string> { "/proc", "/sys" },
                
                // Mask sensitive paths
                MaskedPaths = new List<string>
                {
                    "/proc/kcore",
                    "/proc/keys",
                    "/proc/timer_list"
                },

                AutoRemove = false
            }
        };

        await EnsureImageExistsAsync(context.Language.DockerImage, cancellationToken);

        var response = await _client.Containers.CreateContainerAsync(
            createParams,
            cancellationToken);

        var containerId = response.ID;

        _logger.LogDebug(
            "Created container {ContainerId} for workspace {WorkspaceDir}",
            containerId,
            context.WorkspaceDirectory);

        return containerId;
    }

    private async Task CopyFilesToContainerAsync(
        string containerId,
        string workspaceDirectory,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(workspaceDirectory);

        _logger.LogDebug(
            "Copying {FileCount} files to container {ContainerId}: {Files}",
            files.Length,
            containerId,
            string.Join(", ", files.Select(Path.GetFileName)));

        using var tarStream = new MemoryStream();
        
        await TarFile.CreateFromDirectoryAsync(
            sourceDirectoryName: workspaceDirectory,
            destination: tarStream,
            includeBaseDirectory: false,  // Don't include workspace GUID folder name
            cancellationToken: cancellationToken);

        tarStream.Seek(0, SeekOrigin.Begin);

        // Extract TAR archive to /app in container
        await _client.Containers.ExtractArchiveToContainerAsync(
            containerId,
            new ContainerPathStatParameters { Path = "/app" },
            tarStream,
            cancellationToken);

        _logger.LogDebug(
            "Successfully copied {FileCount} files to container {ContainerId}:/app",
            files.Length,
            containerId);
    }

    private async Task<string> GetContainerLogsAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        const int MaxLogSizeBytes = 1024 * 1024; // 1 MB limit

        var logsParams = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = false,
            Tail = "10000" // Limit to last 10k lines as additional safety
        };

        using var multiplexedStream = await _client.Containers.GetContainerLogsAsync(
            containerId,
            tty: false,
            logsParams,
            cancellationToken);

        using var stdoutStream = new MemoryStream();
        using var stderrStream = new MemoryStream();

        await multiplexedStream.CopyOutputToAsync(
            stdin: Stream.Null,
            stdout: stdoutStream,
            stderr: stderrStream,
            cancellationToken);

        if (stdoutStream.Length > MaxLogSizeBytes || stderrStream.Length > MaxLogSizeBytes)
        {
            _logger.LogWarning(
                "Container {ContainerId} output exceeded size limit. Stdout: {StdoutSize}, Stderr: {StderrSize}",
                containerId,
                stdoutStream.Length,
                stderrStream.Length);
        }

        var stdoutContent = stdoutStream.Length > 0
            ? Encoding.UTF8.GetString(stdoutStream.GetBuffer(), 0, (int)Math.Min(stdoutStream.Length, MaxLogSizeBytes))
            : string.Empty;

        var stderrContent = stderrStream.Length > 0
            ? Encoding.UTF8.GetString(stderrStream.GetBuffer(), 0, (int)Math.Min(stderrStream.Length, MaxLogSizeBytes))
            : string.Empty;

        // Combine stdout and stderr
        if (string.IsNullOrEmpty(stderrContent))
        {
            return stdoutContent;
        }

        return string.Concat(stdoutContent, stderrContent);
    }

    private ExecutionResult ParseExecutionOutput(
        string output,
        List<TestCase> testCases)
    {
        var result = new ExecutionResult
        {
            TotalTests = testCases.Count
        };

        // Check for empty output
        if (string.IsNullOrWhiteSpace(output))
        {
            result.Status = SubmissionStatus.RuntimeError;
            result.RuntimeError = "No output from execution";
            return result;
        }

        try
        {
            // Expected JSON format from bash:
            // {
            //   "results": [
            //     {"testCaseId":"...", "status":1, "executionTimeMs":42, "memoryUsedKB":8192, ...}
            //   ]
            // }
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("results", out var resultsElement))
            {
                // Fallback: try parsing as array directly (old format compatibility)
                if (output.TrimStart().StartsWith("["))
                {
                    var testResults = JsonSerializer.Deserialize<List<TestCaseResult>>(output, jsonOptions);
                    if (testResults != null)
                    {
                        return ProcessTestResults(testResults, testCases, result);
                    }
                }
                
                result.Status = SubmissionStatus.RuntimeError;
                result.RuntimeError = "Invalid JSON format: missing 'results' property";
                return result;
            }
            
            var testResultsList = JsonSerializer.Deserialize<List<TestCaseResult>>(
                resultsElement.GetRawText(), 
                jsonOptions);

            if (testResultsList == null || testResultsList.Count == 0)
            {
                result.Status = SubmissionStatus.RuntimeError;
                result.RuntimeError = "No test results returned";
                return result;
            }

            return ProcessTestResults(testResultsList, testCases, result);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse execution output: {Output}", output);
            result.Status = SubmissionStatus.RuntimeError;
            result.RuntimeError = $"Failed to parse results: {ex.Message}\nOutput: {output.Substring(0, Math.Min(500, output.Length))}";
            return result;
        }
    }

    private ExecutionResult ProcessTestResults(
        List<TestCaseResult> testResults,
        List<TestCase> testCases,
        ExecutionResult result)
    {
        // Check for compilation error (special case with Guid.Empty)
        var compilationError = testResults.FirstOrDefault(t =>
            t.ErrorMessage != null && t.TestCaseId == Guid.Empty);

        if (compilationError != null)
        {
            result.Status = SubmissionStatus.CompilationError;
            result.CompilationError = compilationError.ErrorMessage;
            
            if (compilationError.ErrorLine.HasValue)
            {
                result.CompilationError = $"Line {compilationError.ErrorLine}: {result.CompilationError}";
            }
            
            return result;
        }

        result.TestResults = testResults;
        result.PassedTests = testResults.Count(t => t.Status == TestResultStatus.Passed);
        result.Score = result.TotalTests > 0 
            ? (int)((result.PassedTests / (double)result.TotalTests) * 100) 
            : 0;

        // Calculate total execution time (sum of all test times)
        result.TotalExecutionTimeMs = testResults
            .Where(t => t.ExecutionTimeMs.HasValue)
            .Sum(t => t.ExecutionTimeMs!.Value);
            
        // Find peak memory usage (max across all tests)
        result.MaxMemoryUsedKB = testResults
            .Where(t => t.MemoryUsedKB.HasValue)
            .Max(t => (int?)t.MemoryUsedKB!.Value);

        // Determine overall status
        if (result.PassedTests == result.TotalTests)
        {
            result.Status = SubmissionStatus.Completed;
        }
        else if (testResults.Any(t => t.Status == TestResultStatus.RuntimeError))
        {
            result.Status = SubmissionStatus.RuntimeError;
            var firstError = testResults.First(t => t.Status == TestResultStatus.RuntimeError);
            result.RuntimeError = firstError.ErrorMessage;
        }
        else
        {
            result.Status = SubmissionStatus.Completed;
        }

        return result;
    }

    private async Task EnsureImageExistsAsync(
        string imageName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.Images.InspectImageAsync(imageName, cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            _logger.LogInformation("Pulling Docker image: {ImageName}", imageName);

            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = imageName },
                null,
                new Progress<JSONMessage>(message =>
                {
                    if (!string.IsNullOrEmpty(message.Status))
                    {
                        _logger.LogDebug("Docker pull: {Status}", message.Status);
                    }
                }),
                cancellationToken);

            _logger.LogInformation("Successfully pulled image: {ImageName}", imageName);
        }
    }

    private async Task CleanupContainerAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true
                },
                cancellationToken);

            _logger.LogDebug("Removed container {ContainerId}", containerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove container {ContainerId}",
                containerId);
        }
    }
}