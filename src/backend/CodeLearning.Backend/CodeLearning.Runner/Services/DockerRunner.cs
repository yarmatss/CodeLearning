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
        var startTime = DateTime.UtcNow;
        string? containerId = null;
        ulong peakMemoryBytes = 0;

        try
        {
            // Verify workspace files exist before creating container
            VerifyWorkspaceFiles(context.WorkspaceDirectory);

            // Create container with security constraints (without bind mount)
            containerId = await CreateContainerAsync(context, cancellationToken);

            // Copy workspace files to container
            await CopyFilesToContainerAsync(containerId, context.WorkspaceDirectory, cancellationToken);

            // Start container
            await _client.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                cancellationToken);

            _logger.LogInformation(
                "Started container {ContainerId} for submission {SubmissionId}",
                containerId,
                context.Submission.Id);

            // Poll stats while waiting for container to complete
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(context.Language.TimeoutSeconds));

            var waitTask = _client.Containers.WaitContainerAsync(containerId, cts.Token);

            // Poll memory usage while container is running
            while (!waitTask.IsCompleted)
            {
                try
                {
                    var memoryUsage = await GetCurrentMemoryUsageAsync(containerId, cancellationToken);
                    if (memoryUsage > peakMemoryBytes)
                    {
                        peakMemoryBytes = memoryUsage;
                    }
                }
                catch
                {
                    // Container might have stopped
                }

                await Task.Delay(100, cancellationToken); // Poll every 100ms
            }

            bool completed;
            try
            {
                await waitTask;
                completed = true;
            }
            catch (OperationCanceledException)
            {
                completed = false;
            }

            if (!completed)
            {
                _logger.LogWarning(
                    "Container {ContainerId} timed out after {Timeout}s",
                    containerId,
                    context.Language.TimeoutSeconds);

                await _client.Containers.KillContainerAsync(
                    containerId,
                    new ContainerKillParameters(),
                    cancellationToken);

                return new ExecutionResult
                {
                    Status = SubmissionStatus.TimeLimitExceeded,
                    TotalExecutionTimeMs = context.Language.TimeoutSeconds * 1000,
                    MaxMemoryUsedKB = peakMemoryBytes > 0 ? (int)(peakMemoryBytes / 1024) : null
                };
            }

            // Get container exit code
            var inspectResponse = await _client.Containers.InspectContainerAsync(
                containerId,
                cancellationToken);

            var exitCode = inspectResponse.State.ExitCode;

            // Get logs (stdout + stderr)
            var logs = await GetContainerLogsAsync(containerId, cancellationToken);

            // Parse results
            var result = ParseExecutionOutput(logs, exitCode, context.TestCases, peakMemoryBytes);

            var executionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            result.TotalExecutionTimeMs = executionTime;

            _logger.LogInformation(
                "Container {ContainerId} completed with status {Status} in {Time}ms, memory {MemoryKB}KB",
                containerId,
                result.Status,
                executionTime,
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

    private async Task<ulong> GetCurrentMemoryUsageAsync(
    string containerId,
    CancellationToken cancellationToken)
    {
        try
        {
            var tcs = new TaskCompletionSource<ulong>();

            var statsParams = new ContainerStatsParameters
            {
                Stream = false,
                OneShot = true
            };

            var progress = new Progress<ContainerStatsResponse>(stats =>
            {
                tcs.TrySetResult(stats.MemoryStats?.Usage ?? 0);
            });

            // Start stats request
            var statsTask = _client.Containers.GetContainerStatsAsync(
                containerId,
                statsParams,
                progress,
                cancellationToken);

            // Wait for either stats to arrive or timeout
            var completedTask = await Task.WhenAny(
                tcs.Task,
                statsTask,
                Task.Delay(500, cancellationToken));

            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            return 0;
        }
        catch
        {
            return 0;
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
                // SECURITY: Network isolation
                NetworkMode = "none",

                // SECURITY: Resource limits
                Memory = context.Language.MemoryLimitMB * 1024 * 1024,
                MemorySwap = context.Language.MemoryLimitMB * 1024 * 1024,
                NanoCPUs = (long)(context.Language.CpuLimit * 1_000_000_000),
                PidsLimit = 50,

                // SECURITY: Filesystem isolation
                Tmpfs = new Dictionary<string, string>
                {
                    { "/tmp", "size=10m,mode=1777,noexec,nosuid" }
                },

                // SECURITY: Privilege restrictions
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string>
                {
                    "no-new-privileges",
                },
                
                // SECURITY: Read-only /proc and /sys
                ReadonlyPaths = new List<string> { "/proc", "/sys" },
                
                // SECURITY: Mask sensitive paths
                MaskedPaths = new List<string>
                {
                    "/proc/kcore",
                    "/proc/keys",
                    "/proc/timer_list"
                },

                AutoRemove = false
            }
        };

        // Pull image if not exists
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

        // Check size limits
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
        long exitCode,
        List<TestCase> testCases,
        ulong peakMemoryBytes)
    {
        var result = new ExecutionResult
        {
            TotalTests = testCases.Count,
            MaxMemoryUsedKB = peakMemoryBytes > 0 ? (int)(peakMemoryBytes / 1024) : null
        };

        // Check for compilation error (exit code != 0 and no JSON output)
        if (exitCode != 0 && !output.TrimStart().StartsWith("["))
        {
            result.Status = SubmissionStatus.CompilationError;
            result.CompilationError = output;
            return result;
        }

        try
        {
            // Parse JSON output from wrapper script
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var testResults = JsonSerializer.Deserialize<List<TestCaseResult>>(output, jsonOptions);

            if (testResults == null || testResults.Count == 0)
            {
                result.Status = SubmissionStatus.RuntimeError;
                result.RuntimeError = "No test results returned";
                return result;
            }

            // Check for compilation error (special case with "CompilationError" status string)
            var compilationError = testResults.FirstOrDefault(t =>
                t.ErrorMessage != null && t.StackTrace != null &&
                t.TestCaseId == Guid.Empty);

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
            result.Score = (int)((result.PassedTests / (double)result.TotalTests) * 100);

            // Determine overall status
            if (result.PassedTests == result.TotalTests)
            {
                result.Status = SubmissionStatus.Completed;
            }
            else if (testResults.Any(t => t.Status == TestResultStatus.RuntimeError))
            {
                result.Status = SubmissionStatus.RuntimeError;
            }
            else
            {
                result.Status = SubmissionStatus.Completed;  // Some tests failed but no errors
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse execution output: {Output}", output);
            result.Status = SubmissionStatus.RuntimeError;
            result.RuntimeError = $"Failed to parse results: {ex.Message}\nOutput: {output}";
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