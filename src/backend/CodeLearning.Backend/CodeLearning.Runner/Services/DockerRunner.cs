using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using CodeLearning.Core.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeLearning.Runner.Services;

// Runs one submission in a fresh sandbox container.
//
// Isolation design:
// - The container starts as uid 0 with every capability dropped except SETUID/SETGID (to drop to the
//   student user) and KILL (to stop the student's processes, which run under another uid), plus
//   no-new-privileges. Root here is only the owner of the agent's files, with no other privilege.
// - The root filesystem is read-only. The only writable places are size-capped tmpfs mounts:
//   /sandbox (the student's workspace, owned by 65534), /tmp, and /app/out (root-only, mode 0700:
//   the job bundle, the test inputs and the result).
// - The workspace is streamed as a tar archive on the container's stdin and unpacked by a root bootstrap
//   into /app/out/job, so nothing is copied into the image filesystem and the inputs are never readable
//   by the student.
// - Every compile and run command goes through setpriv to uid/gid 65534 (see UniversalExecutor), so the
//   submission can't write the result, read the inputs, signal the agent or reach the container's stdout
//   (PID 1 and the agent are root-owned, so /proc/<pid>/fd is closed to it).
// - The result is printed on stdout between delimiters carrying a per-run nonce that only root can read;
//   only stdout is parsed, and only between those delimiters.
public class DockerRunner : IDockerRunner
{
    public const string SandboxLabel = "codelearning.sandbox";

    public const string SandboxUser = "65534:65534";
    public const string WorkspacePath = "/sandbox";
    public const string AgentOutputPath = "/app/out";
    public const string JobPath = AgentOutputPath + "/job";
    public const string NonceFileName = "nonce";
    public const string ScriptFileName = "run.sh";

    public const string GenericFailureMessage = "Internal execution error";

    private const long MaxFileSizeBytes = 64L * 1024 * 1024;
    // Results of up to 50 tests with 32KB stdout + 8KB stderr each, after JSON escaping
    private const int MaxLogBytes = 16 * 1024 * 1024;
    private const int MaxDiagnosticBytes = 4 * 1024;

    // Unpacks the job from stdin into the root-only tmpfs and hands over to the generated script (as PID 1).
    // It reads exactly the archive's length instead of waiting for end of file: closing only the write side of an
    // attach stream does not reach the container's stdin on every transport (the Windows named pipe doesn't).
    // It holds no secret: the nonce travels inside the archive, never on a command line
    public static string Bootstrap(long archiveBytes) =>
        "set -e -o pipefail; umask 077; mkdir " + JobPath + "; " +
        $"timeout 60 head -c {archiveBytes} | tar -x -f - -C " + JobPath + " --no-same-owner --no-same-permissions; " +
        "exec /bin/bash " + JobPath + "/" + ScriptFileName + " </dev/null";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Uri _dockerHost;
    private readonly DockerClient _client;
    private readonly ILogger<DockerRunner> _logger;
    private readonly int _maxContainerRuntimeSeconds;
    private readonly string? _containerRuntime;
    private readonly bool _pullMissingImages;

    public DockerRunner(IConfiguration configuration, ILogger<DockerRunner> logger)
    {
        _dockerHost = new Uri(configuration["ExecutionSettings:DockerHost"]!);
        _client = new DockerClientBuilder().WithEndpoint(_dockerHost).Build();
        _logger = logger;
        // Hard cap so problems created before the test case limit cannot hold an execution slot indefinitely
        _maxContainerRuntimeSeconds = configuration.GetValue("ExecutionSettings:MaxContainerRuntimeSeconds", SandboxTimeBudget.DefaultMaxContainerRuntimeSeconds);
        // e.g. "runsc" for gVisor; null keeps the daemon's default runtime
        _containerRuntime = configuration["ExecutionSettings:ContainerRuntime"] is { Length: > 0 } runtime ? runtime : null;
        // Executor images are built locally (compose profile "executors"); pulling would fetch whatever
        // a registry serves under that name, so it is opt-in
        _pullMissingImages = configuration.GetValue("ExecutionSettings:PullMissingImages", false);
    }

    public static Dictionary<string, string> SandboxTmpfs(int sandboxSizeMb = 64) => new()
    {
        // tmpfs pages are charged to the container's memory cgroup, so these also count against its memory limit.
        // Docker mounts tmpfs noexec,nosuid,nodev by default
        { WorkspacePath, $"size={sandboxSizeMb}m,uid=65534,gid=65534,mode=0755" },
        { "/tmp", "size=32m,mode=1777" },
        { AgentOutputPath, "size=32m,mode=0700" }
    };

    public async Task<ExecutionResult> ExecuteAsync(
        Models.ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        string? containerId = null;

        try
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var job = await CreateJobArchiveAsync(context.WorkspaceDirectory, nonce, cancellationToken);
            var budget = SandboxTimeBudget.For(context.Language, context.TestCases.Count, _maxContainerRuntimeSeconds);

            await EnsureImageExistsAsync(context.Language.DockerImage, cancellationToken);
            containerId = await CreateContainerAsync(context, job.Length, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(budget.ContainerSeconds));

            long? exitCode;
            Stream? stdin = null;
            try
            {
                stdin = await StartWithJobAsync(containerId, job, cts.Token);

                _logger.LogInformation(
                    "Started container {ContainerId} for submission {SubmissionId}",
                    containerId,
                    context.Submission.Id);

                var wait = await _client.Containers.WaitContainerAsync(containerId, cts.Token);
                exitCode = wait.StatusCode;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                exitCode = null;
            }
            finally
            {
                if (stdin != null)
                {
                    await stdin.DisposeAsync();
                }
            }

            if (exitCode is null)
            {
                // The script enforces the compile and test budgets itself and still reports partial results,
                // so reaching this backstop means the container was stuck outside the tests
                _logger.LogWarning(
                    "Container {ContainerId} timed out after {Timeout}s",
                    containerId,
                    budget.ContainerSeconds);

                await KillAsync(containerId);

                return new ExecutionResult
                {
                    Status = SubmissionStatus.TimeLimitExceeded,
                    TotalTests = context.TestCases.Count,
                    TotalExecutionTimeMs = budget.ContainerSeconds * 1000
                };
            }

            var logs = await GetContainerLogsAsync(containerId, cancellationToken);
            var result = ParseExecutionOutput(logs.Stdout, nonce, context.TestCases.Count);

            if (result is null)
            {
                _logger.LogError(
                    "Container {ContainerId} for submission {SubmissionId} exited with {ExitCode} without a valid result " +
                    "(stdout {StdoutBytes} bytes, truncated: {Truncated}). Agent stderr: {Stderr}",
                    containerId,
                    context.Submission.Id,
                    exitCode,
                    logs.Stdout.Length,
                    logs.Truncated,
                    logs.Stderr);

                return InternalFailure(context.TestCases.Count);
            }

            _logger.LogInformation(
                "Container {ContainerId} completed with status {Status}, time {Time}ms",
                containerId,
                result.Status,
                result.TotalExecutionTimeMs ?? 0);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Verdicts on the student's code are returned above, so this is an infrastructure failure (Docker, workspace).
            // It propagates so the message is retried, and the submission is failed once the attempts run out
            _logger.LogError(ex, "Error executing submission {SubmissionId}", context.Submission.Id);
            throw;
        }
        finally
        {
            if (containerId != null)
            {
                await CleanupContainerAsync(containerId, CancellationToken.None);
            }
        }
    }

    // Only the text between this run's delimiters is trusted: the delimiters carry a nonce the submission can't read,
    // and only root processes write to the container's stdout. Null when there is no complete, valid result
    public static ExecutionResult? ParseExecutionOutput(string stdout, string nonce, int totalTests)
    {
        var begin = $"<<<CL-RESULT-{nonce}>>>";
        var end = $"<<<CL-END-{nonce}>>>";

        var start = stdout.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += begin.Length;
        var stop = stdout.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0)
        {
            return null;
        }

        ExecutionResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ExecutionResult>(stdout.AsSpan(start, stop - start).Trim(), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null || !Enum.IsDefined(parsed.Status))
        {
            return null;
        }

        parsed.TotalTests = totalTests;
        parsed.TestResults ??= [];

        // The agent writes partial results after every test; a result still marked as running means the
        // script's run budget stopped it, so the tests without a result ran out of time
        if (parsed.Status is SubmissionStatus.Running or SubmissionStatus.Pending)
        {
            parsed.Status = SubmissionStatus.TimeLimitExceeded;
        }

        return parsed;
    }

    private static ExecutionResult InternalFailure(int totalTests) => new()
    {
        Status = SubmissionStatus.RuntimeError,
        TotalTests = totalTests,
        RuntimeError = GenericFailureMessage
    };

    // The job bundle: everything UniversalExecutor wrote to the workspace plus the nonce, which never touches the disk
    private static async Task<byte[]> CreateJobArchiveAsync(string workspaceDirectory, string nonce, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspaceDirectory))
            throw new DirectoryNotFoundException($"Workspace directory does not exist: {workspaceDirectory}");
        if (!File.Exists(Path.Combine(workspaceDirectory, ScriptFileName)))
            throw new InvalidOperationException($"Workspace has no {ScriptFileName}: {workspaceDirectory}");

        using var archive = new MemoryStream();
        await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var directory in Directory.EnumerateDirectories(workspaceDirectory, "*", SearchOption.AllDirectories))
            {
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, EntryName(workspaceDirectory, directory)), cancellationToken);
            }

            foreach (var file in Directory.EnumerateFiles(workspaceDirectory, "*", SearchOption.AllDirectories))
            {
                await writer.WriteEntryAsync(file, EntryName(workspaceDirectory, file), cancellationToken);
            }

            var nonceEntry = new PaxTarEntry(TarEntryType.RegularFile, NonceFileName)
            {
                DataStream = new MemoryStream(Encoding.ASCII.GetBytes(nonce))
            };
            await writer.WriteEntryAsync(nonceEntry, cancellationToken);
        }

        return archive.ToArray();
    }

    private static string EntryName(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private async Task<string> CreateContainerAsync(Models.ExecutionContext context, long archiveBytes, CancellationToken cancellationToken)
    {
        var createParams = new CreateContainerParameters
        {
            Image = context.Language.DockerImage,
            Cmd = ["/bin/bash", "-c", Bootstrap(archiveBytes)],
            WorkingDir = "/",
            // Root without capabilities; the submission itself runs as 65534 through setpriv
            User = "0:0",
            // The job archive arrives on stdin, which closes once it is sent
            AttachStdin = true,
            OpenStdin = true,
            StdinOnce = true,
            Labels = new Dictionary<string, string>
            {
                { "submission-id", context.Submission.Id.ToString() },
                // DockerGuard only lets the Runner act on containers carrying this label
                { SandboxLabel, "true" }
            },
            HostConfig = new HostConfig
            {
                Runtime = _containerRuntime,
                NetworkMode = "none",
                Memory = context.Language.MemoryLimitMB * 1024L * 1024,
                NanoCPUs = (long)(context.Language.CpuLimit * 1_000_000_000),
                MemorySwap = context.Language.MemoryLimitMB * 1024L * 1024,
                PidsLimit = 100,
                // No docker-init: the bootstrap execs the root script as PID 1, so a kill(-1) from the agent
                // (which skips PID 1) can never take it down, and bash reaps orphaned processes itself.
                // PID 1 is root-owned, so the submission can't open /proc/1/fd
                ReadonlyRootfs = true,
                Tmpfs = SandboxTmpfs(),
                CapDrop = ["ALL"],
                // SETUID/SETGID: setpriv drops to 65534. KILL: the agent stops the submission's processes,
                // which run under another uid. All three are limited to the container's own namespaces
                CapAdd = ["SETUID", "SETGID", "KILL"],
                SecurityOpt = ["no-new-privileges"],
                Ulimits =
                [
                    new() { Name = "fsize", Soft = MaxFileSizeBytes, Hard = MaxFileSizeBytes },
                    new() { Name = "nofile", Soft = 1024, Hard = 1024 }
                ],
                AutoRemove = false,
            }
        };

        var response = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        return response.ID;
    }

    // Attaches to stdin before the start, so the bootstrap reads the whole archive. The caller keeps the returned
    // stream open until the container exits: closing it early can drop data the daemon has not copied to stdin yet
    private async Task<Stream> StartWithJobAsync(string containerId, byte[] job, CancellationToken cancellationToken)
    {
        var stdin = await DockerStdinAttach.OpenAsync(_dockerHost, containerId, cancellationToken);

        try
        {
            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);

            await stdin.WriteAsync(job, cancellationToken);
            await stdin.FlushAsync(cancellationToken);
            return stdin;
        }
        catch
        {
            await stdin.DisposeAsync();
            throw;
        }
    }

    private async Task<(string Stdout, string Stderr, bool Truncated)> GetContainerLogsAsync(string containerId, CancellationToken cancellationToken)
    {
        var logsParams = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = false
        };
        using var stream = await _client.Containers.GetContainerLogsAsync(containerId, logsParams, cancellationToken);
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var buffer = new byte[81920];
        var truncated = false;

        while (true)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read.EOF)
            {
                break;
            }

            switch (read.Target)
            {
                // The result channel
                case MultiplexedStream.TargetStream.StandardOut when stdout.Length + read.Count <= MaxLogBytes:
                    await stdout.WriteAsync(buffer.AsMemory(0, read.Count), cancellationToken);
                    break;
                case MultiplexedStream.TargetStream.StandardOut:
                    truncated = true;
                    break;
                // Diagnostics from the root script and the agent, only logged
                case MultiplexedStream.TargetStream.StandardError when stderr.Length < MaxDiagnosticBytes:
                    await stderr.WriteAsync(buffer.AsMemory(0, (int)Math.Min(read.Count, MaxDiagnosticBytes - stderr.Length)), cancellationToken);
                    break;
            }
        }

        return (Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()), truncated);
    }

    private async Task EnsureImageExistsAsync(string imageName, CancellationToken cancellationToken)
    {
        if (await ImageExistsAsync(imageName, cancellationToken))
        {
            return;
        }

        if (!_pullMissingImages)
        {
            throw new InvalidOperationException(
                $"Executor image {imageName} is not available. Build it with: docker compose --profile executors build");
        }

        _logger.LogInformation("Pulling image {Image}", imageName);
        await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = imageName },
                                              null,
                                              new Progress<JSONMessage>(),
                                              cancellationToken);
    }

    private async Task<bool> ImageExistsAsync(string imageName, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Images.InspectImageAsync(imageName, cancellationToken);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
    }

    private async Task KillAsync(string containerId)
    {
        try
        {
            await _client.Containers.KillContainerAsync(containerId, new ContainerKillParameters(), CancellationToken.None);
        }
        catch (DockerApiException ex)
        {
            // Container may have exited between the timeout and the kill; cleanup removes it anyway
            _logger.LogWarning(ex, "Failed to kill timed out container {ContainerId}", containerId);
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
            // SandboxContainerCleanupService removes it later
            _logger.LogWarning(ex, "Failed to remove container {Id}", containerId);
        }
    }
}

// A stdin-only attach done as a proper HTTP/1.1 upgrade (Connection: Upgrade, Upgrade: tcp → 101).
// Docker.DotNet 3.x attaches without the upgrade headers and relies on the daemon hijacking a plain 200 response;
// a reverse proxy such as DockerGuard (YARP) only tunnels raw bytes after a real upgrade, and over the Windows named
// pipe the written data never reached the container either. Verified against Docker Engine 29 directly (npipe)
// and through DockerGuard (tcp)
public static class DockerStdinAttach
{
    private const int MaxResponseHeaderBytes = 16 * 1024;

    public static async Task<Stream> OpenAsync(Uri dockerHost, string containerId, CancellationToken cancellationToken)
    {
        var stream = await ConnectAsync(dockerHost, cancellationToken);
        try
        {
            var request =
                $"POST /containers/{Uri.EscapeDataString(containerId)}/attach?stream=1&stdin=1 HTTP/1.1\r\n" +
                "Host: docker\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: tcp\r\n" +
                "\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var statusLine = await ReadResponseHeadAsync(stream, cancellationToken);
            if (!statusLine.StartsWith("HTTP/1.1 101", StringComparison.Ordinal)
                && !statusLine.StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Attaching to container {containerId} failed: {statusLine}");
            }

            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static async Task<Stream> ConnectAsync(Uri dockerHost, CancellationToken cancellationToken)
    {
        switch (dockerHost.Scheme)
        {
            case "npipe":
                var pipeName = dockerHost.AbsolutePath.StartsWith("/pipe/", StringComparison.Ordinal)
                    ? dockerHost.AbsolutePath["/pipe/".Length..]
                    : dockerHost.AbsolutePath.TrimStart('/');
                var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    dockerHost.Host is { Length: > 0 } server ? server : ".",
                    pipeName,
                    System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                await pipe.ConnectAsync(cancellationToken);
                return pipe;

            case "unix":
                return await ConnectSocketAsync(
                    new System.Net.Sockets.UnixDomainSocketEndPoint(dockerHost.LocalPath),
                    System.Net.Sockets.ProtocolType.Unspecified,
                    cancellationToken);

            case "tcp" or "http":
                return await ConnectSocketAsync(
                    new System.Net.DnsEndPoint(dockerHost.Host, dockerHost.Port),
                    System.Net.Sockets.ProtocolType.Tcp,
                    cancellationToken);

            default:
                throw new NotSupportedException($"Docker host scheme '{dockerHost.Scheme}' is not supported for attach");
        }
    }

    private static async Task<Stream> ConnectSocketAsync(
        System.Net.EndPoint endPoint, System.Net.Sockets.ProtocolType protocol, CancellationToken cancellationToken)
    {
        var family = endPoint is System.Net.Sockets.UnixDomainSocketEndPoint
            ? System.Net.Sockets.AddressFamily.Unix
            : System.Net.Sockets.AddressFamily.Unspecified;
        var socket = family == System.Net.Sockets.AddressFamily.Unix
            ? new System.Net.Sockets.Socket(family, System.Net.Sockets.SocketType.Stream, protocol)
            : new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, protocol);
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // Reads up to the blank line ending the response head, one byte at a time so nothing after it is consumed
    private static async Task<string> ReadResponseHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>();
        var buffer = new byte[1];

        while (head.Count < MaxResponseHeaderBytes)
        {
            if (await stream.ReadAsync(buffer, cancellationToken) == 0)
            {
                break;
            }

            head.Add(buffer[0]);
            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
            {
                var text = Encoding.ASCII.GetString(head.ToArray());
                return text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
            }
        }

        throw new InvalidOperationException("Attach response ended before its headers were complete");
    }
}
