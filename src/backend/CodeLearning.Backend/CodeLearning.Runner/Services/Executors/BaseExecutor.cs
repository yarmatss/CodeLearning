using CodeLearning.Core.Entities;
using CodeLearning.Runner.Models;

namespace CodeLearning.Runner.Services.Executors;

public abstract class BaseExecutor : ICodeExecutor
{
    protected readonly IDockerRunner DockerRunner;
    protected readonly IConfiguration Configuration;
    protected readonly ILogger Logger;

    protected BaseExecutor(
        IDockerRunner dockerRunner,
        IConfiguration configuration,
        ILogger logger)
    {
        DockerRunner = dockerRunner;
        Configuration = configuration;
        Logger = logger;
    }

    public abstract string LanguageName { get; }

    public async Task<ExecutionResult> ExecuteAsync(
        Submission submission,
        Language language,
        List<TestCase> testCases,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid();
        var workspaceBasePath = Configuration["ExecutionSettings:WorkspaceBasePath"]!;
        var workspaceDir = Path.Combine(workspaceBasePath, workspaceId.ToString());

        try
        {
            // Create workspace
            Directory.CreateDirectory(workspaceDir);

            Logger.LogInformation(
                "Created workspace {WorkspaceId} for submission {SubmissionId}",
                workspaceId,
                submission.Id);

            // Prepare files (language-specific)
            await PrepareWorkspaceAsync(
                workspaceDir,
                submission.Code,
                testCases,
                language,
                cancellationToken);

            // Create execution context
            var context = new Models.ExecutionContext
            {
                Submission = submission,
                Language = language,
                TestCases = testCases,
                WorkspaceDirectory = workspaceDir
            };

            // Execute in Docker
            return await DockerRunner.ExecuteAsync(context, cancellationToken);
        }
        finally
        {
            // Cleanup workspace
            CleanupWorkspace(workspaceDir);
        }
    }

    protected abstract Task PrepareWorkspaceAsync(
        string workspaceDir,
        string code,
        List<TestCase> testCases,
        Language language,
        CancellationToken cancellationToken);

    protected void CleanupWorkspace(string workspaceDir)
    {
        try
        {
            if (Directory.Exists(workspaceDir))
            {
                Directory.Delete(workspaceDir, recursive: true);
                Logger.LogDebug("Cleaned up workspace {WorkspaceDir}", workspaceDir);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to cleanup workspace {WorkspaceDir}", workspaceDir);
        }
    }
}
