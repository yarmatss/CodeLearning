using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CodeLearning.Api.Middleware;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception occurred: {Message} | TraceId: {TraceId}",
            exception.Message,
            traceId
        );

        var (statusCode, type, title, detail) = MapException(exception);

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Type = type,
            Title = title,
            Detail = detail,
            Status = statusCode,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions.Add("traceId", traceId);

        if (httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            problemDetails.Extensions.Add("exceptionType", exception.GetType().Name);
            problemDetails.Extensions.Add("stackTrace", exception.StackTrace);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int StatusCode, string Type, string Title, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
                "Not Found",
                ex.Message
            ),
            UnauthorizedAccessException ex when IsAuthenticationFailure(ex) => (
                StatusCodes.Status401Unauthorized,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
                "Unauthorized",
                ex.Message
            ),
            UnauthorizedAccessException ex => (
                StatusCodes.Status403Forbidden,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
                "Forbidden",
                ex.Message
            ),
            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
                "Bad Request",
                ex.Message
            ),
            InvalidOperationException ex => (
                StatusCodes.Status400BadRequest,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
                "Bad Request",
                ex.Message
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                "Internal Server Error",
                "An unexpected error occurred. Please try again later."
            )
        };
    }

    private static bool IsAuthenticationFailure(UnauthorizedAccessException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("invalid email") ||
               message.Contains("invalid password") ||
               message.Contains("credentials") ||
               message.Contains("not found");
    }
}
