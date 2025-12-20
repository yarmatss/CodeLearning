using CodeLearning.Core.Entities;
using System.Text.Json;

namespace CodeLearning.Runner.Services.Executors;

public class PythonExecutor : BaseExecutor
{
    public PythonExecutor(
        IDockerRunner dockerRunner,
        IConfiguration configuration,
        ILogger<PythonExecutor> logger)
        : base(dockerRunner, configuration, logger)
    {
    }

    public override string LanguageName => "Python";

    protected override async Task PrepareWorkspaceAsync(
        string workspaceDir,
        string code,
        List<TestCase> testCases,
        Language language,
        CancellationToken cancellationToken)
    {
        // Write student's code
        var solutionPath = Path.Combine(workspaceDir, "solution.py");
        await File.WriteAllTextAsync(solutionPath, code, cancellationToken);

        // Generate wrapper script
        var wrapperScript = GenerateWrapperScript(testCases);
        var wrapperPath = Path.Combine(workspaceDir, "runner.py");
        await File.WriteAllTextAsync(wrapperPath, wrapperScript, cancellationToken);

        Logger.LogDebug(
            "Prepared Python workspace with {TestCount} test cases",
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
import sys
import json
import traceback
import time
import ast
from io import StringIO

def load_solution():
    """"""
    Load and compile student's code, extracting only the 'solution' function.
    This prevents automatic execution of code at module level that uses input().
    """"""
    with open('/app/solution.py', 'r') as f:
        student_code = f.read()
    
    # Compile first to catch syntax errors
    compiled_code = compile(student_code, '/app/solution.py', 'exec')
    
    # Create isolated namespace for student code
    student_namespace = {{}}
    
    # Redirect stdin to empty to prevent input() calls during module load
    original_stdin = sys.stdin
    sys.stdin = StringIO('')
    
    try:
        # Execute in isolated namespace
        exec(compiled_code, student_namespace)
    except EOFError:
        # Student code calls input() at module level - this is expected if they call solution()
        # We'll handle this by extracting just the function definition
        pass
    finally:
        sys.stdin = original_stdin
    
    # Check if solution function was defined before the EOFError
    if 'solution' in student_namespace and callable(student_namespace['solution']):
        return student_namespace['solution']
    
    # If solution wasn't captured, try to extract just the function definition
    # Parse the AST to find the solution function
    try:
        tree = ast.parse(student_code)
        
        # Find the solution function definition
        solution_def = None
        for node in ast.walk(tree):
            if isinstance(node, ast.FunctionDef) and node.name == 'solution':
                solution_def = node
                break
        
        if solution_def is None:
            raise NameError(""Function 'solution' not defined"")
        
        # Create a new module with only the function definition
        new_tree = ast.Module(body=[solution_def], type_ignores=[])
        ast.fix_missing_locations(new_tree)
        
        # Compile and execute just the function definition
        func_code = compile(new_tree, '/app/solution.py', 'exec')
        func_namespace = {{}}
        exec(func_code, func_namespace)
        
        return func_namespace['solution']
        
    except SyntaxError:
        raise  # Re-raise syntax errors to be caught by outer handler

# Main execution
try:
    solution_func = load_solution()
    
except SyntaxError as e:
    # Syntax errors = Compilation Error
    error_msg = f""{{e.__class__.__name__}}: {{str(e)}}""
    if e.lineno:
        error_msg += f""\nLine {{e.lineno}}: {{e.text}}""
    
    print(json.dumps([{{
        ""testCaseId"": ""00000000-0000-0000-0000-000000000000"",
        ""status"": ""CompilationError"",
        ""errorMessage"": error_msg,
        ""errorLine"": e.lineno,
        ""stackTrace"": traceback.format_exc()
    }}]))
    sys.exit(0)
    
except NameError as e:
    # Function not defined = Compilation Error
    print(json.dumps([{{
        ""testCaseId"": ""00000000-0000-0000-0000-000000000000"",
        ""status"": ""CompilationError"",
        ""errorMessage"": str(e),
        ""stackTrace"": traceback.format_exc()
    }}]))
    sys.exit(0)
    
except Exception as e:
    # Other errors during module loading = Runtime Error
    print(json.dumps([{{
        ""testCaseId"": ""00000000-0000-0000-0000-000000000000"",
        ""status"": 3,
        ""errorMessage"": f""{{e.__class__.__name__}}: {{str(e)}}"",
        ""stackTrace"": traceback.format_exc()
    }}]))
    sys.exit(0)

# Test cases
tests = {testCasesJson}

results = []
for test in tests:
    start_time = time.time()
    old_stdout = sys.stdout
    
    try:
        # Setup I/O redirection BEFORE calling solution
        sys.stdin = StringIO(test['input'])
        sys.stdout = buffer = StringIO()
        
        # Execute solution function
        solution_func()
        
        # Get output
        output = buffer.getvalue()
        sys.stdout = old_stdout
        
        # Limit output size to prevent memory exhaustion (max 10KB)
        if len(output) > 10240:
            output = output[:10240] + ""... (output truncated)""
        
        output = output.strip()
        
        # Calculate execution time
        execution_time_ms = int((time.time() - start_time) * 1000)
        
        # Compare output
        passed = output == test['expectedOutput']
        
        results.append({{
            ""testCaseId"": test['id'],
            ""status"": 1 if passed else 2,
            ""actualOutput"": output,
            ""executionTimeMs"": execution_time_ms
        }})
        
    except SystemExit as e:
        # Student called sys.exit() - treat as runtime error
        sys.stdout = old_stdout
        execution_time_ms = int((time.time() - start_time) * 1000)
        
        results.append({{
            ""testCaseId"": test['id'],
            ""status"": 3,
            ""errorMessage"": f""SystemExit: Code called sys.exit({{e.code}})"",
            ""executionTimeMs"": execution_time_ms
        }})
        
    except Exception as e:
        sys.stdout = old_stdout
        execution_time_ms = int((time.time() - start_time) * 1000)
        
        results.append({{
            ""testCaseId"": test['id'],
            ""status"": 3,
            ""errorMessage"": f""{{e.__class__.__name__}}: {{str(e)}}"",
            ""executionTimeMs"": execution_time_ms,
            ""stackTrace"": traceback.format_exc()
        }})

# Output results as JSON
print(json.dumps(results))
";
    }
}
