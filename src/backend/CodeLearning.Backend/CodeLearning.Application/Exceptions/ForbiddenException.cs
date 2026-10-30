namespace CodeLearning.Application.Exceptions;

// The caller is authenticated but may not act on the resource (403)
public class ForbiddenException(string message) : Exception(message);
