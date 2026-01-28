using CodeLearning.Core.Entities;
using CodeLearning.Core.Models;
using System.Text;

namespace CodeLearning.Runner.Services;

public class UniversalExecutor(
    IDockerRunner dockerRunner,
    IConfiguration configuration,
    ILogger<UniversalExecutor> logger) : ICodeExecutor
{
    public async Task<ExecutionResult> ExecuteAsync(
        Submission submission,
        Language language,
        List<TestCase> testCases,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid();
        var workspaceBasePath = configuration["ExecutionSettings:WorkspaceBasePath"]
                           ?? throw new InvalidOperationException("WorkspaceBasePath not configured");
        var workspaceDir = Path.Combine(workspaceBasePath, workspaceId.ToString());

        try
        {
            Directory.CreateDirectory(workspaceDir);

            logger.LogInformation(
                "Created workspace {WorkspaceId} for {Language} submission {SubmissionId}",
                workspaceId, language.Name, submission.Id);

            await PrepareWorkspaceAsync(
                workspaceDir,
                submission.Code,
                testCases,
                language,
                cancellationToken);

            var context = new Models.ExecutionContext
            {
                Submission = submission,
                Language = language,
                TestCases = testCases,
                WorkspaceDirectory = workspaceDir
            };

            return await dockerRunner.ExecuteAsync(context, cancellationToken);
        }
        finally
        {
            CleanupWorkspace(workspaceDir);
        }
    }

    private async Task PrepareWorkspaceAsync(
        string workspaceDir,
        string code,
        List<TestCase> testCases,
        Language language,
        CancellationToken cancellationToken)
    {
        string solutionFile;

        if (language.Name.Equals("C#", StringComparison.OrdinalIgnoreCase))
        {
            // write to StudentCode.cs to avoid conflicts with Program.cs created by dotnet new
            solutionFile = "StudentCode.cs";
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, solutionFile), code, cancellationToken);
        }
        else if (language.Name.Equals("Java", StringComparison.OrdinalIgnoreCase))
        {
            // In Java file must be named Solution.java
            // Student must provide code in class named Solution
            solutionFile = "Solution.java";
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, solutionFile), code, cancellationToken);
        }
        else
        {
            solutionFile = $"solution{language.FileExtension}";
            await File.WriteAllTextAsync(Path.Combine(workspaceDir, solutionFile), code, cancellationToken);
        }

        for (int i = 0; i < testCases.Count; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspaceDir, $"input_{i}.txt"),
                testCases[i].Input,
                cancellationToken);

            await File.WriteAllTextAsync(
                Path.Combine(workspaceDir, $"expected_{i}.txt"),
                testCases[i].ExpectedOutput,
                cancellationToken);
        }

        var shellScript = GenerateShellScript(language, solutionFile);

        await File.WriteAllTextAsync(
            Path.Combine(workspaceDir, "run_tests.sh"),
            shellScript,
            cancellationToken);
    }

    private string GenerateShellScript(Language language, string solutionFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");

        string runCommandForAgent = $"{language.ExecutableCommand} {solutionFileName}";

        if (!string.IsNullOrWhiteSpace(language.CompileCommand))
        {
            sb.AppendLine("# === Compilation Phase ===");

            if (language.Name.Equals("C#", StringComparison.OrdinalIgnoreCase))
            {
                // C# specific compilation steps
                sb.AppendLine("dotnet new console -o . --force > /dev/null");
                sb.AppendLine($"mv {solutionFileName} Program.cs");

                sb.AppendLine("if ! dotnet build -c Release > compile_log.txt 2>&1; then");
                sb.AppendLine("  ERR=$(cat compile_log.txt)");
                sb.AppendLine("  # Escape double quotes and backslashes in error message");
                sb.AppendLine("  ERR=$(cat compile_log.txt | jq -Rs .)");
                sb.AppendLine("  echo \"{\\\"Status\\\":3, \\\"CompilationError\\\": $ERR, \\\"Score\\\":0}\" > /app/result.json");
                sb.AppendLine("  cat /app/result.json");
                sb.AppendLine("  exit 0");
                sb.AppendLine("fi");

                sb.AppendLine("DLL_PATH=$(find bin/Release -name '*.dll' | head -n 1)");
                runCommandForAgent = "dotnet exec $DLL_PATH";
            }
            else if (language.Name.Equals("Java", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"if ! javac {solutionFileName} > compile_log.txt 2>&1; then");
                sb.AppendLine("  ERR=$(cat compile_log.txt)");
                sb.AppendLine("  # Escape double quotes and backslashes in error message");
                sb.AppendLine("  ERR=$(cat compile_log.txt | jq -Rs .)");
                sb.AppendLine("  echo \"{\\\"Status\\\":3, \\\"CompilationError\\\": $ERR, \\\"Score\\\":0}\" > /app/result.json");
                sb.AppendLine("  cat /app/result.json");
                sb.AppendLine("  exit 0");
                sb.AppendLine("fi");

                runCommandForAgent = $"java {Path.GetFileNameWithoutExtension(solutionFileName)}";
            }
        }

        sb.AppendLine();
        sb.AppendLine("# === Execution Phase ===");

        sb.AppendLine($"/app/runner --run \"{runCommandForAgent}\" --input \"/app\" --out \"/app/result.json\" --timelimit {language.TimeoutSeconds * 1000}");

        sb.AppendLine("if [ -f /app/result.json ]; then");
        sb.AppendLine("  cat /app/result.json");
        sb.AppendLine("else");

        sb.AppendLine("  echo '{\"Status\": 4, \"RuntimeError\": \"Agent failed to produce result (Critical Error)\", \"Score\": 0}'");
        sb.AppendLine("fi");

        return sb.ToString();
    }

    private void CleanupWorkspace(string workspaceDir)
    {
        try
        {
            if (Directory.Exists(workspaceDir))
            {
                Directory.Delete(workspaceDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to cleanup workspace {WorkspaceDir}: {Message}", workspaceDir, ex.Message);
        }
    }
}