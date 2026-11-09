using CodeLearning.Core.Grading;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace CodeLearning.Runner.Services;

// Removes sandbox containers that DockerRunner could not remove itself (Runner crash or restart, Docker errors).
// Runs at startup and every CleanupIntervalMinutes. Only containers older than the longest possible run are
// touched, so a container another Runner instance is still using is never removed
public sealed class SandboxContainerCleanupService : BackgroundService
{
    // On top of the maximum runtime: container creation, image checks and result collection
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly DockerClient _client;
    private readonly ILogger<SandboxContainerCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxAge;
    private readonly TimeProvider _timeProvider;

    public SandboxContainerCleanupService(
        IConfiguration configuration,
        ILogger<SandboxContainerCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        _client = new DockerClientBuilder().WithEndpoint(new Uri(configuration["ExecutionSettings:DockerHost"]!)).Build();
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("ExecutionSettings:CleanupIntervalMinutes", 60)));
        _maxAge = TimeSpan.FromSeconds(configuration.GetValue("ExecutionSettings:MaxContainerRuntimeSeconds", SandboxTimeBudget.DefaultMaxContainerRuntimeSeconds)) + Grace;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);
        do
        {
            try
            {
                await RemoveStaleContainersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Docker or the guard may not be up yet; the next round tries again
                _logger.LogWarning(ex, "Sandbox container cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> RemoveStaleContainersAsync(CancellationToken cancellationToken)
    {
        // DockerGuard enforces this label filter on the listing whatever the request says
        var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{DockerRunner.SandboxLabel}=true"] = true }
            }
        }, cancellationToken);

        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _maxAge;
        var removed = 0;

        foreach (var container in containers)
        {
            if (container.Labels is null
                || !container.Labels.TryGetValue(DockerRunner.SandboxLabel, out var sandbox)
                || sandbox != "true"
                || container.Created.ToUniversalTime() > cutoff)
            {
                continue;
            }

            try
            {
                await _client.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                    cancellationToken);
                removed++;
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning(ex, "Failed to remove stale sandbox container {ContainerId}", container.ID);
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Count} stale sandbox containers", removed);
        }

        return removed;
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}
