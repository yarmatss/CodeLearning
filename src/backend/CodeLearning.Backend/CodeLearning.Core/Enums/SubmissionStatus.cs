namespace CodeLearning.Core.Enums;

public enum SubmissionStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    CompilationError = 3,
    RuntimeError = 4,
    TimeLimitExceeded = 5,
    MemoryLimitExceeded = 6
}
