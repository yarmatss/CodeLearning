using CodeLearning.Core.Entities;

namespace CodeLearning.Core.Grading;

// How long each phase of a run may take. The generated script enforces the compile and test budgets itself
// (so partial results survive); the container timeout in DockerRunner is only the backstop above them.
// Shared by the Runner (to size a run) and the API (to reject problems whose tests cannot fit the budget),
// so both sides always agree on how many tests a language can run.
public sealed record SandboxTimeBudget(int CompileSeconds, int PerTestMs, int TestsMs, int RunnerSeconds, int ContainerSeconds)
{
    // Must match ExecutionSettings:MaxContainerRuntimeSeconds when that setting is absent
    public const int DefaultMaxContainerRuntimeSeconds = 300;

    // Container start, unpacking the job, copying the workspace and printing the result
    public const int OverheadSeconds = 20;

    // Slack for the runner to report before the script's own timeout, plus rounding
    public const int RunnerSlackSeconds = 6;

    public const int CSharpCompileSeconds = 90;
    public const int DefaultCompileSeconds = 30;

    public static SandboxTimeBudget For(Language language, int testCount, int maxContainerRuntimeSeconds)
    {
        var compileSeconds = CompileSecondsFor(language);
        var perTestMs = PerTestSeconds(language) * 1000;
        var availableTestSeconds = AvailableTestSeconds(compileSeconds, maxContainerRuntimeSeconds);
        var testsMs = (int)Math.Min((long)perTestMs * Math.Max(1, testCount), availableTestSeconds * 1000L);

        // The agent stops itself at testsMs; this lets it report before the script's own timeout kills it
        var runnerSeconds = (testsMs + 999) / 1000 + 5;
        var containerSeconds = compileSeconds + runnerSeconds + OverheadSeconds;

        return new SandboxTimeBudget(compileSeconds, perTestMs, testsMs, runnerSeconds, containerSeconds);
    }

    // The most tests that can each use their full time limit without being cut off by the test budget
    public static int MaxTestCount(Language language, int maxContainerRuntimeSeconds)
    {
        var available = AvailableTestSeconds(CompileSecondsFor(language), maxContainerRuntimeSeconds);
        return Math.Max(available / PerTestSeconds(language), 0);
    }

    public static int CompileSecondsFor(Language language)
    {
        if (string.IsNullOrWhiteSpace(language.CompileCommand))
        {
            return 0;
        }

        return language.Name.Equals("C#", StringComparison.OrdinalIgnoreCase)
            ? CSharpCompileSeconds
            : DefaultCompileSeconds;
    }

    private static int PerTestSeconds(Language language) => Math.Max(1, language.TimeoutSeconds);

    private static int AvailableTestSeconds(int compileSeconds, int maxContainerRuntimeSeconds) =>
        Math.Max(1, maxContainerRuntimeSeconds - compileSeconds - OverheadSeconds - RunnerSlackSeconds);
}
