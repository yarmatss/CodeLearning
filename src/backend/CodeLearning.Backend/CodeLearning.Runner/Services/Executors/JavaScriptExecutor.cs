using System.Text.Json;
using CodeLearning.Core.Entities;

namespace CodeLearning.Runner.Services.Executors;

public class JavaScriptExecutor : BaseExecutor
{
    public JavaScriptExecutor(
        IDockerRunner dockerRunner,
        IConfiguration configuration,
        ILogger<JavaScriptExecutor> logger)
        : base(dockerRunner, configuration, logger)
    {
    }

    public override string LanguageName => "JavaScript";

    protected override async Task PrepareWorkspaceAsync(
        string workspaceDir,
        string code,
        List<TestCase> testCases,
        Language language,
        CancellationToken cancellationToken)
    {
        // Write student's code
        var solutionPath = Path.Combine(workspaceDir, "solution.js");
        await File.WriteAllTextAsync(solutionPath, code, cancellationToken);

        // Generate wrapper script
        var wrapperScript = GenerateWrapperScript(testCases);
        var wrapperPath = Path.Combine(workspaceDir, "runner.js");
        await File.WriteAllTextAsync(wrapperPath, wrapperScript, cancellationToken);

        Logger.LogDebug(
            "Prepared JavaScript workspace with {TestCount} test cases",
            testCases.Count);
    }

    private string GenerateWrapperScript(List<TestCase> testCases)
    {
        var testCasesJson = JsonSerializer.Serialize(testCases.Select(tc => new
        {
            id = tc.Id,
            input = tc.Input,
            expectedOutput = tc.ExpectedOutput
        }).ToList());

        return $@"
const fs = require('fs');
const {{ StringDecoder }} = require('string_decoder');

// Load student's code
let solution;
try {{
    const studentCode = fs.readFileSync('/app/solution.js', 'utf8');
    eval(studentCode);
    
    if (typeof global.solution !== 'function') {{
        throw new Error(""Function 'solution' not defined"");
    }}
    solution = global.solution;
}} catch (error) {{
    console.log(JSON.stringify([{{
        testCaseId: '00000000-0000-0000-0000-000000000000',
        status: 0,  // CompilationError
        errorMessage: error.message
    }}]));
    process.exit(1);
}}

// Test cases
const tests = {testCasesJson};

const results = [];

// Mock stdin
function createInputStream(input) {{
    const lines = input.split('\n');
    let currentLine = 0;
    
    return {{
        read: () => lines[currentLine++],
        hasNext: () => currentLine < lines.length
    }};
}}

for (const test of tests) {{
    const startTime = Date.now();
    
    try {{
        // Capture stdout
        let output = '';
        const originalLog = console.log;
        console.log = (...args) => {{
            output += args.join(' ') + '\n';
        }};
        
        // Setup input
        global.input = createInputStream(test.input);
        
        // Execute solution
        solution();
        
        // Restore console.log
        console.log = originalLog;
        
        // Trim output
        output = output.trim();
        
        const executionTimeMs = Date.now() - startTime;
        const passed = output === test.expectedOutput;
        
        results.push({{
            testCaseId: test.id,
            status: passed ? 1 : 2,  // Passed = 1, Failed = 2
            actualOutput: output,
            executionTimeMs
        }});
        
    }} catch (error) {{
        const executionTimeMs = Date.now() - startTime;
        
        results.push({{
            testCaseId: test.id,
            status: 3,  // RuntimeError = 3
            errorMessage: error.message,
            executionTimeMs
        }});
    }}
}}

// Output results as JSON
console.log(JSON.stringify(results));
";
    }
}
