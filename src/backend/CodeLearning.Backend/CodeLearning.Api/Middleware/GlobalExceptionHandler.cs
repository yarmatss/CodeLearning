using CodeLearning.Application.Exceptions;
using CodeLearning.Infrastructure.Extensions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
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

        // The client went away: nobody will read a response, and it is not a server fault
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Request was aborted by the client | TraceId: {TraceId}", traceId);
            }

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        var (statusCode, type, title, detail) = MapException(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception occurred: {Message} | TraceId: {TraceId}",
                exception.Message,
                traceId
            );
        }
        else
        {
            logger.LogWarning(
                "Request failed with {StatusCode}: {Message} | TraceId: {TraceId}",
                statusCode,
                exception.Message,
                traceId
            );
        }

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
            // Malformed request, body too large, etc.: Kestrel already chose the right 4xx
            BadHttpRequestException ex => (
                ex.StatusCode,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5",
                ReasonPhrases.GetReasonPhrase(ex.StatusCode) is { Length: > 0 } phrase ? phrase : "Bad Request",
                ex.Message
            ),
            // Before the DbUpdateException case: it is a subtype
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10",
                "Conflict",
                "The resource was modified by another request. Please refresh and try again."
            ),
            ProblemConflictException ex => (
                StatusCodes.Status409Conflict,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10",
                "Conflict",
                ex.Message
            ),
            NotFoundException ex => (
                StatusCodes.Status404NotFound,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
                "Not Found",
                ex.Message
            ),
            AuthenticationFailedException ex => (
                StatusCodes.Status401Unauthorized,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
                "Unauthorized",
                ex.Message
            ),
            ForbiddenException ex => (
                StatusCodes.Status403Forbidden,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
                "Forbidden",
                ex.Message
            ),
            BusinessRuleException ex => (
                StatusCodes.Status400BadRequest,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
                "Bad Request",
                ex.Message
            ),
            DbUpdateException ex when ex.IsUniqueViolation() => (
                StatusCodes.Status409Conflict,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10",
                "Conflict",
                "The resource was modified concurrently or already exists. Please refresh and try again."
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                "Internal Server Error",
                "An unexpected error occurred. Please try again later."
            )
        };
    }
}
