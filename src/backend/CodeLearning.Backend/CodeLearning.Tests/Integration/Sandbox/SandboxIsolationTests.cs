extern alias Runner;

using System.Collections.Concurrent;
using System.Diagnostics;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using CodeLearning.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Runner::CodeLearning.Runner.Services;
using SandboxContext = Runner::CodeLearning.Runner.Models.ExecutionContext;

namespace CodeLearning.Tests.Integration.Sandbox;

// Runs real submissions through UniversalExecutor's script and DockerRunner against the executor images.
// Needs Docker and the images (docker compose --profile executors build); skipped when an image is missing.
// CL_SANDBOX_DOCKER_HOST points the tests at another endpoint, e.g. a DockerGuard (tcp://localhost:2375)
[Trait("Category", "Sandbox")]
[Collection(SandboxCollection.Name)]
public class SandboxIsolationTests
{
    private const string PythonImage = "codelearning/python:3.12-slim";
    private const string NodeImage = "codelearning/node:22-slim";
    private const string JavaImage = "codelearning/java:21-jdk";
    private const string CSharpImage = "codelearning/dotnet:10.0";

    private const string SumPython = "print(sum(map(int, input().split())))";

    [SandboxFact(PythonImage)]
    public async Task Python_CorrectSolution_ShouldReportOutputPerTest()
    {
        var result = await RunAsync(Python(), SumPython, "1 2", "40 2");

        result.Status.Should().Be(SubmissionStatus.Completed, Describe(result));
        result.TestResults.Select(t => t.ActualOutput?.Trim()).Should().Equal(["3", "42"], Describe(result));
    }

    [SandboxFact(NodeImage)]
    public async Task Node_CorrectSolution_ShouldReportOutput()
    {
        const string code = """
            const s = require('fs').readFileSync(0, 'utf8').trim().split(/\s+/).map(Number);
            console.log(s[0] + s[1]);
            """;

        var result = await RunAsync(Language("JavaScript", NodeImage, ".js", "node", null), code, "1 2");

        result.Status.Should().Be(SubmissionStatus.Completed, result.RuntimeError);
        result.TestResults.Single().ActualOutput!.Trim().Should().Be("3");
    }

    [SandboxFact(JavaImage)]
    public async Task Java_CorrectSolution_ShouldCompileAndRun()
    {
        const string code = """
            import java.util.Scanner;
            public class Solution {
                public static void main(String[] args) {
                    Scanner s = new Scanner(System.in);
                    System.out.println(s.nextInt() + s.nextInt());
                }
            }
            """;

        var result = await RunAsync(Java(), code, "1 2");

        result.Status.Should().Be(SubmissionStatus.Completed, result.RuntimeError ?? result.CompilationError);
        result.TestResults.Single().ActualOutput!.Trim().Should().Be("3");
    }

    [SandboxFact(JavaImage)]
    public async Task Java_CompileError_ShouldReturnCompilerOutput()
    {
        var result = await RunAsync(Java(), "public class Solution { int x = ; }", "1 2");

        result.Status.Should().Be(SubmissionStatus.CompilationError);
        result.CompilationError.Should().Contain("Solution.java");
        result.TestResults.Should().BeEmpty();
    }

    [SandboxFact(CSharpImage)]
    public async Task CSharp_CorrectSolution_ShouldBuildWithoutNetworkAndRun()
    {
        const string code = """
            var parts = Console.ReadLine()!.Split(' ');
            Console.WriteLine(int.Parse(parts[0]) + int.Parse(parts[1]));
            """;

        var result = await RunAsync(CSharp(), code, "1 2", "5 5");

        result.Status.Should().Be(SubmissionStatus.Completed, result.RuntimeError ?? result.CompilationError);
        result.TestResults.Select(t => t.ActualOutput?.Trim()).Should().Equal("3", "10");
    }

    [SandboxFact(CSharpImage)]
    public async Task CSharp_CompileError_ShouldReturnCompilerOutput()
    {
        var result = await RunAsync(CSharp(), "Console.WriteLine(undefinedVariable);", "1 2");

        result.Status.Should().Be(SubmissionStatus.CompilationError);
        result.CompilationError.Should().Contain("CS0103");
    }

    // The submission tries every way to reach the result channel or the agent; each must fail, and the
    // agent must still report this test's real output
    [SandboxFact(PythonImage)]
    public async Task Attack_ForgeResultAndReachAgent_ShouldAllBeDenied()
    {
        const string code = """
            import os, sys
            data = sys.stdin.read()
            report = []
            def attempt(name, fn):
                try:
                    fn()
                    report.append(name + ":ALLOWED")
                except Exception:
                    report.append(name + ":denied")
            def write(path, text):
                with open(path, "w") as f:
                    f.write(text)
            forged = '{"Status":2,"TestResults":[{"TestIndex":0,"Status":1,"ActualOutput":"3"}]}'
            attempt("write-result", lambda: write("/app/out/result.json", forged))
            attempt("list-app", lambda: os.listdir("/app"))
            attempt("list-job", lambda: os.listdir("/app/out/job"))
            attempt("read-input", lambda: open("/app/out/job/tests/input_1.txt").read())
            attempt("read-script", lambda: open("/app/out/job/run.sh").read())
            attempt("read-runner", lambda: open("/app/runner", "rb").read(1))
            attempt("write-rootfs", lambda: write("/etc/forged", "x"))
            attempt("setuid-root", lambda: os.setuid(0))
            me = os.getpid()
            root_pids = [int(p) for p in os.listdir("/proc") if p.isdigit() and int(p) != me and os.stat("/proc/" + p).st_uid == 0]
            for pid in root_pids:
                attempt("stdout-of-%d" % pid, lambda: write("/proc/%d/fd/1" % pid, "<<<CL-RESULT-0>>>" + forged))
                attempt("environ-of-%d" % pid, lambda: open("/proc/%d/environ" % pid).read())
                attempt("mem-of-%d" % pid, lambda: open("/proc/%d/mem" % pid, "rb").read(1))
            for pid in root_pids:
                attempt("kill-%d" % pid, lambda: os.kill(pid, 9))
            print("roots=%d" % len(root_pids), " ".join(report))
            # Delimiters on the submission's own stdout are only test output
            print("<<<CL-RESULT-00000000000000000000000000000000>>>" + forged + "<<<CL-END-00000000000000000000000000000000>>>")
            """;

        var result = await RunAsync(Python(), code, "1 2", "secret hidden input");

        result.Status.Should().Be(SubmissionStatus.Completed, result.RuntimeError);
        result.TestResults.Should().HaveCount(2, "killing the agent must fail, so every test still reports");
        foreach (var test in result.TestResults)
        {
            test.Status.Should().NotBe(TestResultStatus.Passed, "a forged verdict must not be taken");
            test.ActualOutput.Should().Contain("write-result:denied")
                .And.Contain("read-input:denied")
                .And.Contain("stdout-of-1:denied")
                .And.Contain("kill-1:denied")
                .And.NotContain("ALLOWED")
                .And.NotContain("roots=0");
        }
    }

    [SandboxFact(PythonImage)]
    public async Task Attack_FillDisk_ShouldStopAtTheTmpfsCaps()
    {
        const string code = """
            import errno, os, sys
            sys.stdin.read()
            def fill(directory):
                total, chunk, i = 0, b"x" * (1 << 20), 0
                try:
                    while True:
                        fd = os.open("%s/fill%d" % (directory, i), os.O_WRONLY | os.O_CREAT, 0o600)
                        try:
                            for _ in range(8):
                                total += os.write(fd, chunk)
                        finally:
                            os.close(fd)
                        i += 1
                except OSError as e:
                    return "%s=%d:%s" % (directory, total >> 20, errno.errorcode.get(e.errno))
            print(" ".join(fill(d) for d in ["/sandbox", "/tmp", "/dev/shm", "/var/tmp", "/app"]))
            """;

        var result = await RunAsync(Python(), code, "");

        result.Status.Should().Be(SubmissionStatus.Completed, result.RuntimeError);
        var report = result.TestResults.Single().ActualOutput!.Trim().Split(' ')
            .Select(r => r.Split('=', ':'))
            .ToDictionary(r => r[0], r => (Mb: int.Parse(r[1]), Error: r[2]));

        report["/sandbox"].Error.Should().Be("ENOSPC");
        report["/sandbox"].Mb.Should().BeInRange(56, 64);
        report["/tmp"].Error.Should().Be("ENOSPC");
        report["/tmp"].Mb.Should().BeInRange(24, 32);
        // Docker's default /dev/shm: RAM-backed, 64MB, counted against the memory limit
        report["/dev/shm"].Mb.Should().BeLessThanOrEqualTo(64);
        report["/var/tmp"].Mb.Should().Be(0, "the root filesystem is read-only");
        report["/var/tmp"].Error.Should().BeOneOf("EROFS", "EACCES");
        report["/app"].Mb.Should().Be(0);
        report["/app"].Error.Should().BeOneOf("EROFS", "EACCES");
    }

    // The program never reads its (larger than a pipe buffer) input and loops: each test must end in its own
    // time limit rather than stalling the agent until the container-level timeout
    [SandboxFact(PythonImage)]
    public async Task Attack_IgnoreStdinWhileLooping_ShouldGiveTimeLimitPerTest()
    {
        var bigInput = string.Join(' ', Enumerable.Repeat("12345", 40_000));
        var stopwatch = Stopwatch.StartNew();

        var result = await RunAsync(Python(timeoutSeconds: 2), "while True:\n    pass\n", bigInput, bigInput);

        stopwatch.Stop();
        result.TestResults.Should().HaveCount(2);
        result.TestResults.Should().OnlyContain(t => t.Status == TestResultStatus.TimeLimitExceeded);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(40), "two 2s tests must not wait for the container timeout");
    }

    // Processes left behind by one test must not survive into the next one
    [SandboxFact(PythonImage)]
    public async Task Attack_DaemonizedProcess_ShouldNotOutliveItsTest()
    {
        const string code = """
            import os, sys, time
            sys.stdin.read()
            marker = "/tmp/daemon-alive"
            if os.path.exists(marker):
                time.sleep(0.5)
                before = os.path.getmtime(marker)
                time.sleep(1.0)
                print("alive" if os.path.getmtime(marker) != before else "dead")
            else:
                if os.fork() == 0:
                    os.setsid()
                    while True:
                        with open(marker, "w") as f:
                            f.write(str(time.time()))
                        time.sleep(0.1)
                time.sleep(0.3)
                print("started")
            """;

        var result = await RunAsync(Python(timeoutSeconds: 5), code, "", "");

        result.TestResults.Select(t => t.ActualOutput?.Trim()).Should().Equal("started", "dead");
    }

    // Through DockerGuard the listing is forced to the sandbox label, so unrelated containers are never seen
    [SandboxFact(PythonImage)]
    public async Task Cleanup_ShouldRemoveOnlyStaleSandboxContainers()
    {
        var sandbox = Docker("create", "--label", "codelearning.sandbox=true", PythonImage, "/bin/true");
        var unrelated = Docker("create", "--label", "codelearning.cleanup-test=true", PythonImage, "/bin/true");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExecutionSettings:DockerHost"] = SandboxDocker.Host,
                    ["ExecutionSettings:MaxContainerRuntimeSeconds"] = "60"
                })
                .Build();
            // An hour from now every sandbox container created so far is past the maximum runtime
            var later = new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
            using var cleanup = new SandboxContainerCleanupService(
                configuration, NullLogger<SandboxContainerCleanupService>.Instance, later);

            var removed = await cleanup.RemoveStaleContainersAsync(CancellationToken.None);

            removed.Should().BeGreaterThanOrEqualTo(1);
            ContainerExists(sandbox).Should().BeFalse();
            ContainerExists(unrelated).Should().BeTrue();
        }
        finally
        {
            Docker("rm", "-f", sandbox, unrelated);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static bool ContainerExists(string id)
    {
        try
        {
            Docker("inspect", "--format", "{{.Id}}", id);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Docker(params string[] args)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"docker {args[0]} failed: {process.StandardError.ReadToEnd()}");
    }

    private static string Describe(ExecutionResult result) => System.Text.Json.JsonSerializer.Serialize(result);

    private static Language Python(int timeoutSeconds = 5) =>
        Language("Python", PythonImage, ".py", "python3", null, timeoutSeconds);

    private static Language Java() =>
        Language("Java", JavaImage, ".java", "java Solution", "javac Solution.java", 10, 512, 1.0m);

    private static Language CSharp() =>
        Language("C#", CSharpImage, ".cs", "dotnet run --no-build", "csharp", 10, 512, 1.0m);

    private static Language Language(string name, string image, string extension, string executable, string? compile,
                                     int timeoutSeconds = 5, int memoryMb = 256, decimal cpu = 0.5m) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Version = "test",
        DockerImage = image,
        FileExtension = extension,
        RunCommand = "/bin/bash /app/run_tests.sh",
        ExecutableCommand = executable,
        CompileCommand = compile,
        TimeoutSeconds = timeoutSeconds,
        MemoryLimitMB = memoryMb,
        CpuLimit = cpu
    };

    // The raw sandbox result (before SubmissionGrader), so the tests see exactly what the agent reported
    private static async Task<ExecutionResult> RunAsync(Language language, string code, params string[] inputs)
    {
        const int maxRuntimeSeconds = 180;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionSettings:DockerHost"] = SandboxDocker.Host,
                ["ExecutionSettings:MaxContainerRuntimeSeconds"] = maxRuntimeSeconds.ToString()
            })
            .Build();

        var testCases = inputs
            .Select((input, i) => new TestCase { Input = input, ExpectedOutput = "", OrderIndex = i, Problem = null! })
            .ToList();
        var workspace = Path.Combine(Path.GetTempPath(), "codelearning-sandbox-tests", Guid.NewGuid().ToString());

        try
        {
            var budget = SandboxTimeBudget.For(language, testCases.Count, maxRuntimeSeconds);
            await UniversalExecutor.PrepareWorkspaceAsync(workspace, code, testCases, language, budget, CancellationToken.None);

            var runner = new DockerRunner(configuration, NullLogger<DockerRunner>.Instance);
            return await runner.ExecuteAsync(new SandboxContext
            {
                Submission = new Submission { Id = Guid.NewGuid(), Code = code, Problem = null!, Student = null!, Language = language },
                Language = language,
                TestCases = testCases,
                WorkspaceDirectory = workspace
            });
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }
}

// Sandbox test classes run one after another: the cleanup test removes every stale sandbox container it sees
[CollectionDefinition(Name)]
public sealed class SandboxCollection
{
    public const string Name = "Sandbox";
}

public static class SandboxDocker
{
    private static readonly ConcurrentDictionary<string, bool> Images = new();

    public static string Host =>
        Environment.GetEnvironmentVariable("CL_SANDBOX_DOCKER_HOST") is { Length: > 0 } host
            ? host
            : OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";

    // Asks the local Docker CLI, which works the same on every host and needs no client configuration
    public static bool ImageExists(string image) => Images.GetOrAdd(image, static name =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", ["image", "inspect", "--format", "{{.Id}}", name])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                process.Kill();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });
}

public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute(string image)
    {
        if (!SandboxDocker.ImageExists(image))
        {
            Skip = $"Executor image {image} is not built (docker compose --profile executors build)";
        }
    }
}
