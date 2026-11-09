using CodeLearning.Api.Middleware;
using CodeLearning.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace CodeLearning.Tests.Unit.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<IProblemDetailsService> _problemDetails = new();
    private readonly RecordingLogger _logger = new();
    private ProblemDetailsContext? _written;

    public GlobalExceptionHandlerTests()
    {
        _problemDetails
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => _written = c)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task AbortedRequest_ShouldReturn499WithoutErrorLogOrBody()
    {
        // Arrange
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = CreateContext();
        context.RequestAborted = aborted.Token;

        // Act
        var handled = await CreateHandler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Null(_written);
        VerifyNoLogAtOrAbove(LogLevel.Warning);
    }

    [Fact]
    public async Task OperationCanceled_WhenRequestNotAborted_ShouldStillBe500()
    {
        // Arrange - e.g. an internal timeout, which is a server problem
        var context = CreateContext();

        // Act
        await CreateHandler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    public async Task BadHttpRequest_ShouldUseItsOwnStatusCode(int statusCode)
    {
        // Arrange
        var context = CreateContext();

        // Act
        await CreateHandler().TryHandleAsync(context, new BadHttpRequestException("bad", statusCode), CancellationToken.None);

        // Assert
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(statusCode, _written!.ProblemDetails.Status);
        VerifyNoLogAtOrAbove(LogLevel.Error);
    }

    [Fact]
    public async Task DbUpdateConcurrency_ShouldReturn409()
    {
        // Arrange
        var context = CreateContext();

        // Act
        await CreateHandler().TryHandleAsync(context, new DbUpdateConcurrencyException("conflict"), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("Conflict", _written!.ProblemDetails.Title);
    }

    [Fact]
    public async Task ProblemConflict_ShouldReturn409WithItsMessage()
    {
        // Arrange
        var context = CreateContext();

        // Act
        await CreateHandler().TryHandleAsync(context, new ProblemConflictException("locked"), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("locked", _written!.ProblemDetails.Detail);
        VerifyNoLogAtOrAbove(LogLevel.Error);
    }

    private GlobalExceptionHandler CreateHandler() => new(_problemDetails.Object, _logger);

    private static DefaultHttpContext CreateContext()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Testing");

        var services = new ServiceCollection();
        services.AddSingleton(environment.Object);

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private void VerifyNoLogAtOrAbove(LogLevel level) =>
        Assert.DoesNotContain(_logger.Levels, l => l >= level);

    // Moq cannot proxy ILogger<T> for an internal T, so a minimal recorder is used instead
    private sealed class RecordingLogger : ILogger<GlobalExceptionHandler>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
