using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Formats.Tar;
using System.Text;
using System.Text.Json;

namespace CodeLearning.Runner.Services;

public class DockerRunner : IDockerRunner
{
    private const long MaxFileSizeBytes = 64L * 1024 * 1024;
    // ~10KB stdout + ~10KB stderr per test, up to 6x after JSON escaping
    private const int MaxLogBytes = 16 * 1024 * 1024;

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

            // Timeout calculations
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                completed = false;
            }

            if (!completed)
            {
                _logger.LogWarning(
                    "Container {ContainerId} timed out after {Timeout}s",
                    containerId,
                    totalTimeout);

                try
                {
                    await _client.Containers.KillContainerAsync(
                        containerId,
                        new ContainerKillParameters(),
                        CancellationToken.None);
                }
                catch (DockerApiException ex)
                {
                    // Container may have exited between the timeout and the kill; cleanup removes it anyway
                    _logger.LogWarning(ex, "Failed to kill timed out container {ContainerId}", containerId);
                }

                return new ExecutionResult
                {
                    Status = SubmissionStatus.TimeLimitExceeded,
                    TotalExecutionTimeMs = totalTimeout * 1000
                };
            }

            var (logs, truncated) = await GetContainerLogsAsync(containerId, cancellationToken);

            _logger.LogDebug("Container output (first 1000 chars): {Output}",
                logs.Length > 1000 ? logs.Substring(0, 1000) : logs);

            if (truncated)
            {
                _logger.LogWarning("Container {ContainerId} output exceeded {MaxLogBytes} bytes", containerId, MaxLogBytes);
                return new ExecutionResult
                {
                    Status = SubmissionStatus.RuntimeError,
                    RuntimeError = $"Execution output exceeded the {MaxLogBytes / (1024 * 1024)} MB limit"
                };
            }

            var result = ParseExecutionOutput(logs, context.TestCases);

            _logger.LogInformation(
                "Container {ContainerId} completed with status {Status}, time {Time}ms",
                containerId,
                result.Status,
                result.TotalExecutionTimeMs ?? 0);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: not a verdict, let the unacknowledged message be retried
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing submission {SubmissionId}", context.Submission.Id);
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
                await CleanupContainerAsync(containerId, CancellationToken.None);
            }
        }
    }

    private ExecutionResult ParseExecutionOutput(string output, List<TestCase> testCases)
    {
        var result = new ExecutionResult
        {
            TotalTests = testCases.Count
        };

        if (string.IsNullOrWhiteSpace(output))
        {
            result.Status = SubmissionStatus.RuntimeError;
            result.RuntimeError = "No output from execution (Agent did not produce stdout)";
            return result;
        }

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var jsonStartIndex = output.IndexOf('{');
            if (jsonStartIndex == -1)
            {
                result.Status = SubmissionStatus.RuntimeError;
                result.RuntimeError = "No JSON found in output";
                return result;
            }

            var jsonContent = output.Substring(jsonStartIndex);
            var deserializedResult = JsonSerializer.Deserialize<ExecutionResult>(jsonContent, jsonOptions);

            if (deserializedResult == null)
            {
                result.Status = SubmissionStatus.RuntimeError;
                result.RuntimeError = "Deserialized result is null";
                return result;
            }

            result.Status = deserializedResult.Status;
            result.TotalExecutionTimeMs = deserializedResult.TotalExecutionTimeMs;
            result.MaxMemoryUsedKB = deserializedResult.MaxMemoryUsedKB;
            result.CompilationError = deserializedResult.CompilationError;
            result.RuntimeError = deserializedResult.RuntimeError;
            result.TestResults = deserializedResult.TestResults ?? new List<TestCaseResult>();

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON output");
            result.Status = SubmissionStatus.RuntimeError;
            result.RuntimeError = $"Failed to parse results: {ex.Message}. Output snippet: {output.Substring(0, Math.Min(200, output.Length))}";
            return result;
        }
    }

    private void VerifyWorkspaceFiles(string workspaceDirectory)
    {
        if (!Directory.Exists(workspaceDirectory)) 
            throw new DirectoryNotFoundException($"Workspace directory does not exist: {workspaceDirectory}");
        if (Directory.GetFiles(workspaceDirectory).Length == 0) 
            throw new InvalidOperationException($"Workspace directory is empty: {workspaceDirectory}");
    }

    private async Task<string> CreateContainerAsync(Models.ExecutionContext context, CancellationToken cancellationToken)
    {
        var runCommandParts = context.Language.RunCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var createParams = new CreateContainerParameters
        {
            Image = context.Language.DockerImage,
            Cmd = runCommandParts,
            WorkingDir = "/app",
            User = "65534:65534", // nobody
            Labels = new Dictionary<string, string> { { "submission-id", context.Submission.Id.ToString() } },
            HostConfig = new HostConfig
            {
                NetworkMode = "none",
                Memory = context.Language.MemoryLimitMB * 1024L * 1024,
                NanoCPUs = (long)(context.Language.CpuLimit * 1_000_000_000),
                MemorySwap = context.Language.MemoryLimitMB * 1024L * 1024,
                PidsLimit = 100, // expanded for java/c#
                Tmpfs = new Dictionary<string, string> { { "/tmp", "size=50m,mode=1777" } }, // expanded tmpfs
                CapDrop = new List<string> { "ALL" },
                SecurityOpt = new List<string> { "no-new-privileges" },
                Ulimits = new List<Ulimit>
                {
                    new() { Name = "fsize", Soft = MaxFileSizeBytes, Hard = MaxFileSizeBytes },
                    new() { Name = "nofile", Soft = 1024, Hard = 1024 }
                },
                AutoRemove = false,
            }
        };

        await EnsureImageExistsAsync(context.Language.DockerImage, cancellationToken);
        var response = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        return response.ID;
    }

    private async Task CopyFilesToContainerAsync(string containerId,
                                                 string workspaceDirectory,
                                                 CancellationToken cancellationToken)
    {
        using var tarStream = new MemoryStream();
        await TarFile.CreateFromDirectoryAsync(workspaceDirectory,
                                               tarStream,
                                               false,
                                               cancellationToken: cancellationToken);
        tarStream.Seek(0, SeekOrigin.Begin);
        await _client.Containers.ExtractArchiveToContainerAsync(containerId,
                                                                new ContainerPathStatParameters { Path = "/app" },
                                                                tarStream,
                                                                cancellationToken);
    }

    private async Task<(string Logs, bool Truncated)> GetContainerLogsAsync(string containerId, CancellationToken cancellationToken)
    {
        var logsParams = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = false
        };
        using var stream = await _client.Containers.GetContainerLogsAsync(containerId,
                                                                          false,
                                                                          logsParams,
                                                                          cancellationToken);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read.EOF)
            {
                return (Encoding.UTF8.GetString(ms.ToArray()), false);
            }

            if (ms.Length + read.Count > MaxLogBytes)
            {
                return (Encoding.UTF8.GetString(ms.ToArray()), true);
            }

            await ms.WriteAsync(buffer.AsMemory(0, read.Count), cancellationToken);
        }
    }

    private async Task EnsureImageExistsAsync(string imageName, CancellationToken cancellationToken)
    {
        try 
        {
            await _client.Images.InspectImageAsync(imageName, cancellationToken); 
        }
        catch (DockerImageNotFoundException)
        {
            _logger.LogInformation("Pulling image {Image}", imageName);
            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = imageName },
                                                  null,
                                                  new Progress<JSONMessage>(),
                                                  cancellationToken);
        }
    }

    private async Task CleanupContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                cancellationToken);
        }
        catch (Exception ex)
        { 
            _logger.LogWarning(ex, "Failed to remove container {Id}", containerId); 
        }
    }
}