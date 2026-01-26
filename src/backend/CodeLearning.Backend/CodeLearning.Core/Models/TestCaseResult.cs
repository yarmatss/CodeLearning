using CodeLearning.Core.Enums;
using System.Text.Json.Serialization;

namespace CodeLearning.Core.Models;

public class TestCaseResult
{
    public Guid TestCaseId { get; set; }
    
    [JsonConverter(typeof(TestResultStatusConverter))]
    public TestResultStatus Status { get; set; }
    
    public string? ActualOutput { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public int? MemoryUsedKB { get; set; }
    public int? ErrorLine { get; set; }
    public string? StackTrace { get; set; }
}
