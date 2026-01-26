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
            catch (OperationCanceledException)
            {
                completed = false;
            }

            if (!completed)
            {
                _logger.LogWarning(
                    "Container {ContainerId} timed out after {Timeout}s",
                    containerId,
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
                "Container {ContainerId} completed with status {Status}, score {Score}%, time {Time}ms",
                containerId,
                result.Status,
                result.Score,
                result.TotalExecutionTimeMs ?? 0);

            return result;
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
                await CleanupContainerAsync(containerId, cancellationToken);
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
            result.Score = deserializedResult.Score;
            result.TotalExecutionTimeMs = deserializedResult.TotalExecutionTimeMs;
            result.MaxMemoryUsedKB = deserializedResult.MaxMemoryUsedKB;
            result.CompilationError = deserializedResult.CompilationError;
            result.RuntimeError = deserializedResult.RuntimeError;
            result.TestResults = deserializedResult.TestResults ?? new List<TestCaseResult>();

            // map actual TestCaseIds from db
            if (result.TestResults.Count <= testCases.Count)
            {
                for (int i = 0; i < result.TestResults.Count; i++)
                {
                    result.TestResults[i].TestCaseId = testCases[i].Id;
                }
            }

            result.TotalTests = testCases.Count;
            result.PassedTests = result.TestResults.Count(t => t.Status == TestResultStatus.Passed);

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
                Memory = context.Language.MemoryLimitMB * 1024 * 1024,
                NanoCPUs = (long)(context.Language.CpuLimit * 1_000_000_000),
                PidsLimit = 100, // expanded for java/c#
                Tmpfs = new Dictionary<string, string> { { "/tmp", "size=50m,mode=1777" } }, // expanded tmpfs
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

    private async Task<string> GetContainerLogsAsync(string containerId, CancellationToken cancellationToken)
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
        await stream.CopyOutputToAsync(Stream.Null, ms, ms, cancellationToken);
        return Encoding.UTF8.GetString(ms.ToArray());
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