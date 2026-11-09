namespace CodeLearning.Application.Exceptions;

// The request conflicts with the problem's current state (409): the problem is locked because a
// published course uses it, or a starter code for the language already exists
public class ProblemConflictException(string message) : Exception(message);
