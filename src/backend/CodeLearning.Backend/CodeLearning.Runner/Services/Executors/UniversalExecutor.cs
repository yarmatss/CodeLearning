using CodeLearning.Core.Entities;
using CodeLearning.Runner.Models;
using System.Text;
using System.Text.Json;

namespace CodeLearning.Runner.Services.Executors;

public class UniversalExecutor : ICodeExecutor
{
    private readonly IDockerRunner _dockerRunner;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UniversalExecutor> _logger;

    public UniversalExecutor(
        IDockerRunner dockerRunner,
        IConfiguration configuration,
        ILogger<UniversalExecutor> logger)
    {
        _dockerRunner = dockerRunner;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        Submission submission,
        Language language,
        List<TestCase> testCases,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid();
        var workspaceBasePath = _configuration["ExecutionSettings:WorkspaceBasePath"]!;
        var workspaceDir = Path.Combine(workspaceBasePath, workspaceId.ToString());

        try
        {
            Directory.CreateDirectory(workspaceDir);

            _logger.LogInformation(
                "Created workspace {WorkspaceId} for {Language} submission {SubmissionId}",
                workspaceId,
                language.Name,
                submission.Id);

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

            return await _dockerRunner.ExecuteAsync(context, cancellationToken);
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
        // 1. Write student's solution code
        string solutionFile;
        
        // Special handling for C# - save code to temporary file
        // (dotnet new will create Program.cs, we'll overwrite it later)
        if (language.Name.Equals("C#", StringComparison.OrdinalIgnoreCase))
        {
            solutionFile = "student_code.tmp";
            await File.WriteAllTextAsync(
                Path.Combine(workspaceDir, solutionFile),
                code,
                cancellationToken);
        }
        // Special handling for Java - use capitalized class name
        else if (language.Name.Equals("Java", StringComparison.OrdinalIgnoreCase))
        {
            solutionFile = "Solution.java";
            await File.WriteAllTextAsync(
                Path.Combine(workspaceDir, solutionFile),
                code,
                cancellationToken);
        }
        else
        {
            // For other languages, write solution file normally
            solutionFile = $"solution{language.FileExtension}";
            await File.WriteAllTextAsync(
                Path.Combine(workspaceDir, solutionFile),
                code,
                cancellationToken);
        }

        // 2. Write test case files (input and expected output)
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

        // 3. Write test metadata as JSON
        var metadata = testCases.Select((tc, index) => new
        {
            index,
            id = tc.Id.ToString()
        }).ToList();

        await File.WriteAllTextAsync(
            Path.Combine(workspaceDir, "metadata.json"),
            JsonSerializer.Serialize(metadata),
            cancellationToken);

        // 4. Generate universal shell script
        var shellScript = GenerateShellScript(testCases.Count, language);
        await File.WriteAllTextAsync(
            Path.Combine(workspaceDir, "run_tests.sh"),
            shellScript,
            cancellationToken);

        _logger.LogDebug(
            "Prepared {Language} workspace: {SolutionFile}, {TestCount} test files, run_tests.sh",
            language.Name,
            solutionFile,
            testCases.Count);
    }

    private string GenerateShellScript(int testCount, Language language)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# Universal test runner - auto-generated");
        sb.AppendLine();
        
        // Compile if needed (for Java, C#, C++, etc.)
        if (!string.IsNullOrWhiteSpace(language.CompileCommand))
        {
            sb.AppendLine("# ============ Compilation ============");
            
            // For C#, create project first, then replace Program.cs
            if (language.Name.Equals("C#", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("# Create .NET console project in current directory");
                sb.AppendLine("dotnet new console -o . --force --use-program-main > /dev/null 2>&1");
                sb.AppendLine();
                sb.AppendLine("# Replace template Program.cs with student's code");
                sb.AppendLine("mv student_code.tmp Program.cs");
                sb.AppendLine();
                sb.AppendLine("# Build in Release mode (capture all output for error reporting)");
                sb.AppendLine("if ! dotnet build -c Release --nologo --verbosity minimal -nowarn:CS8600,CS8602,CS8604 > compile_output.txt 2>&1; then");
                sb.AppendLine("  # Build failed - extract and format error message");
                sb.AppendLine("  error=$(cat compile_output.txt | head -c 2000 | jq -Rs . || echo '\"Compilation error\"')");
                sb.AppendLine("  echo '{\"results\":[{\"testCaseId\":\"00000000-0000-0000-0000-000000000000\",\"status\":0,\"errorMessage\":'$error',\"executionTimeMs\":0,\"memoryUsedKB\":0}]}'");
                sb.AppendLine("  exit 0");
                sb.AppendLine("fi");
                sb.AppendLine();
                sb.AppendLine("# Find the compiled DLL");
                sb.AppendLine("dll_path=$(find bin/Release/net10.0 -name '*.dll' -type f | head -1)");
            }
            else
            {
                // For other compiled languages (Java, C++, etc.)
                sb.AppendLine($"if ! {language.CompileCommand} 2> compile_error.txt; then");
                sb.AppendLine("  # Compilation failed");
                sb.AppendLine("  error=$(cat compile_error.txt | head -c 2000 | jq -Rs . || echo '\"Compilation error\"')");
                sb.AppendLine("  echo '{\"results\":[{\"testCaseId\":\"00000000-0000-0000-0000-000000000000\",\"status\":0,\"errorMessage\":'$error',\"executionTimeMs\":0,\"memoryUsedKB\":0}]}'");
                sb.AppendLine("  exit 0");
                sb.AppendLine("fi");
            }
            
            sb.AppendLine();
        }
        
        // Load test metadata
        sb.AppendLine("metadata=$(cat metadata.json)");
        sb.AppendLine("results='['");
        sb.AppendLine();

        // Generate test execution for each test case
        for (int i = 0; i < testCount; i++)
        {
            var comma = i < testCount - 1 ? "," : "";
            
            sb.AppendLine($"# ============ Test {i} ============");
            sb.AppendLine($"test_id=$(echo \"$metadata\" | jq -r '.[{i}].id')");
            sb.AppendLine();
            
            // Determine solution file name and executable command based on language
            string executableCommand;
            
            if (language.Name.Equals("Java", StringComparison.OrdinalIgnoreCase))
            {
                executableCommand = language.ExecutableCommand;
            }
            else if (language.Name.Equals("C#", StringComparison.OrdinalIgnoreCase))
            {
                executableCommand = "dotnet exec \"$dll_path\"";  // Use compiled DLL
            }
            else
            {
                string solutionFileName = $"solution{language.FileExtension}";
                executableCommand = $"{language.ExecutableCommand} {solutionFileName}";
            }
            
            // Execute with /usr/bin/time -v to measure both time and memory
            sb.AppendLine($"/usr/bin/time -v -o stats_{i}.txt \\");
            sb.AppendLine($"  timeout {language.TimeoutSeconds}s {executableCommand} \\");
            sb.AppendLine($"    < input_{i}.txt > output_{i}.txt 2> error_{i}.txt");
            sb.AppendLine("exit_code=$?");
            sb.AppendLine();
            
            // Parse time and memory from /usr/bin/time -v output
            sb.AppendLine("test_time=0");
            sb.AppendLine("test_memory=0");
            sb.AppendLine($"if [ -f stats_{i}.txt ]; then");
            sb.AppendLine("  # Extract elapsed time: 'Elapsed (wall clock) time (h:mm:ss or m:ss): 0:00.12'");
            sb.AppendLine($"  elapsed_line=$(grep -i 'Elapsed (wall clock) time' stats_{i}.txt 2>/dev/null || echo '')");
            sb.AppendLine("  if [ -n \"$elapsed_line\" ]; then");
            sb.AppendLine("    # Extract time value (supports formats: 0:00.12, 1:23.45, 0:01:23.45)");
            sb.AppendLine("    time_val=$(echo \"$elapsed_line\" | awk -F': ' '{print $2}')");
            sb.AppendLine("    # Convert to seconds (handle h:mm:ss or m:ss format)");
            sb.AppendLine("    if echo \"$time_val\" | grep -q ':'; then");
            sb.AppendLine("      # Has colons - parse as time");
            sb.AppendLine("      IFS=':' read -ra parts <<< \"$time_val\"");
            sb.AppendLine("      if [ ${#parts[@]} -eq 3 ]; then");
            sb.AppendLine("        # h:mm:ss format");
            sb.AppendLine("        seconds=$(echo \"${parts[0]}*3600 + ${parts[1]}*60 + ${parts[2]}\" | bc 2>/dev/null || echo 0)");
            sb.AppendLine("      else");
            sb.AppendLine("        # m:ss format");
            sb.AppendLine("        seconds=$(echo \"${parts[0]}*60 + ${parts[1]}\" | bc 2>/dev/null || echo 0)");
            sb.AppendLine("      fi");
            sb.AppendLine("      test_time=$(echo \"$seconds * 1000 / 1\" | bc 2>/dev/null || echo 0)");
            sb.AppendLine("    fi");
            sb.AppendLine("  fi");
            sb.AppendLine();
            sb.AppendLine("  # Extract memory: 'Maximum resident set size (kbytes): 8192'");
            sb.AppendLine($"  mem_line=$(grep -i 'Maximum resident set size' stats_{i}.txt 2>/dev/null || echo '')");
            sb.AppendLine("  if [ -n \"$mem_line\" ]; then");
            sb.AppendLine("    test_memory=$(echo \"$mem_line\" | awk -F': ' '{print $2}' | tr -d ' ' || echo 0)");
            sb.AppendLine("    if ! echo \"$test_memory\" | grep -Eq '^[0-9]+$'; then");
            sb.AppendLine("      test_memory=0");
            sb.AppendLine("    fi");
            sb.AppendLine("  fi");
            sb.AppendLine("fi");
            sb.AppendLine();
            
            // Compare outputs (trim whitespace)
            sb.AppendLine($"actual=$(cat output_{i}.txt | tr -d '\\n\\r' | xargs)");
            sb.AppendLine($"expected=$(cat expected_{i}.txt | tr -d '\\n\\r' | xargs)");
            sb.AppendLine();
            
            // Escape output for JSON
            sb.AppendLine("actual_json=$(echo \"$actual\" | jq -Rs . || echo '\"\"')");
            sb.AppendLine();
            
            // Determine test status and build JSON result
            sb.AppendLine("if [ $exit_code -eq 124 ]; then");
            sb.AppendLine("  # Timeout (exit code 124 from timeout command)");
            sb.AppendLine($"  results+=\"{{\\\"testCaseId\\\":\\\"$test_id\\\",\\\"status\\\":4,\\\"errorMessage\\\":\\\"Time limit exceeded\\\",\\\"executionTimeMs\\\":$test_time,\\\"memoryUsedKB\\\":$test_memory}}{comma}\"");
            sb.AppendLine("elif [ $exit_code -ne 0 ]; then");
            sb.AppendLine("  # Runtime error");
            sb.AppendLine($"  error_msg=$(cat error_{i}.txt | head -c 1000 | jq -Rs . || echo '\"Unknown error\"')");
            sb.AppendLine($"  results+=\"{{\\\"testCaseId\\\":\\\"$test_id\\\",\\\"status\\\":3,\\\"errorMessage\\\":$error_msg,\\\"executionTimeMs\\\":$test_time,\\\"memoryUsedKB\\\":$test_memory}}{comma}\"");
            sb.AppendLine("elif [ \"$actual\" = \"$expected\" ]; then");
            sb.AppendLine("  # Test passed");
            sb.AppendLine($"  results+=\"{{\\\"testCaseId\\\":\\\"$test_id\\\",\\\"status\\\":1,\\\"actualOutput\\\":$actual_json,\\\"executionTimeMs\\\":$test_time,\\\"memoryUsedKB\\\":$test_memory}}{comma}\"");
            sb.AppendLine("else");
            sb.AppendLine("  # Wrong answer");
            sb.AppendLine("  # Truncate output if too long");
            sb.AppendLine("  if [ ${#actual} -gt 1000 ]; then");
            sb.AppendLine("    actual=\"${actual:0:1000}... (truncated)\"");
            sb.AppendLine("    actual_json=$(echo \"$actual\" | jq -Rs .)");
            sb.AppendLine("  fi");
            sb.AppendLine($"  results+=\"{{\\\"testCaseId\\\":\\\"$test_id\\\",\\\"status\\\":2,\\\"actualOutput\\\":$actual_json,\\\"executionTimeMs\\\":$test_time,\\\"memoryUsedKB\\\":$test_memory}}{comma}\"");
            sb.AppendLine("fi");
            sb.AppendLine();
        }

        sb.AppendLine("results+=']'");
        sb.AppendLine();
        sb.AppendLine("# Output results as JSON (only to stdout, suppress stderr)");
        sb.AppendLine("echo '{\"results\":'$results'}' 2>/dev/null");

        return sb.ToString();
    }

    private void CleanupWorkspace(string workspaceDir)
    {
        try
        {
            if (Directory.Exists(workspaceDir))
            {
                Directory.Delete(workspaceDir, recursive: true);
                _logger.LogDebug("Cleaned up workspace {WorkspaceDir}", workspaceDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup workspace {WorkspaceDir}", workspaceDir);
        }
    }
}
