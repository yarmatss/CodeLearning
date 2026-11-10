using CodeLearning.Api.Realtime;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace CodeLearning.Tests.Integration.Realtime;

public class SubmissionsHubTests(IntegrationTestWebAppFactory factory)
    : IClassFixture<IntegrationTestWebAppFactory>, IAsyncLifetime
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(15);

    // Long enough for a wrongly routed notification to arrive, short enough not to slow the suite
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _client = factory.CreateAnonymousClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Connect_WithoutAccessToken_ShouldBeUnauthorized()
    {
        // Arrange
        await using var connection = BuildConnection(accessToken: null);

        // Act
        var act = () => connection.StartAsync();

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connect_WithInvalidAccessToken_ShouldBeUnauthorized()
    {
        // Arrange
        await using var connection = BuildConnection(accessToken: "not-a-jwt");

        // Act
        var act = () => connection.StartAsync();

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StatusChanged_ShouldReachTheSubmittingStudent()
    {
        // Arrange
        var student = await RegisterStudentAsync();
        await using var connection = BuildConnection(student.AccessToken);
        var received = Listen(connection);
        await connection.StartAsync();

        var submissionId = Guid.NewGuid();

        // Act
        await PublishAsync(new SubmissionStatusChanged(submissionId, student.UserId, SubmissionStatus.Completed));

        // Assert
        var notification = await received.Task.WaitAsync(DeliveryTimeout);
        notification.SubmissionId.Should().Be(submissionId);
        notification.Status.Should().Be(SubmissionStatus.Completed);
    }

    [Fact]
    public async Task StatusChanged_ShouldReachEveryConnectionOfTheStudent()
    {
        // Arrange: the same student in two tabs
        var student = await RegisterStudentAsync();
        await using var firstTab = BuildConnection(student.AccessToken);
        await using var secondTab = BuildConnection(student.AccessToken);
        var firstReceived = Listen(firstTab);
        var secondReceived = Listen(secondTab);
        await firstTab.StartAsync();
        await secondTab.StartAsync();

        // Act
        await PublishAsync(new SubmissionStatusChanged(Guid.NewGuid(), student.UserId, SubmissionStatus.Running));

        // Assert
        (await firstReceived.Task.WaitAsync(DeliveryTimeout)).Status.Should().Be(SubmissionStatus.Running);
        (await secondReceived.Task.WaitAsync(DeliveryTimeout)).Status.Should().Be(SubmissionStatus.Running);
    }

    [Fact]
    public async Task StatusChanged_ShouldNotReachOtherStudents()
    {
        // Arrange
        var owner = await RegisterStudentAsync();
        var bystander = await RegisterStudentAsync();

        await using var ownerConnection = BuildConnection(owner.AccessToken);
        await using var bystanderConnection = BuildConnection(bystander.AccessToken);
        var ownerReceived = Listen(ownerConnection);
        var bystanderReceived = Listen(bystanderConnection);
        await ownerConnection.StartAsync();
        await bystanderConnection.StartAsync();

        // Act
        await PublishAsync(new SubmissionStatusChanged(Guid.NewGuid(), owner.UserId, SubmissionStatus.Completed));

        // Assert: once the owner has it, the bystander would have had it too
        await ownerReceived.Task.WaitAsync(DeliveryTimeout);
        await Task.Delay(SilenceWindow);
        bystanderReceived.Task.IsCompleted.Should().BeFalse();
    }

    private HubConnection BuildConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_client.BaseAddress!, SubmissionsHub.Route), options =>
            {
                // The in-memory test server has no sockets, so use the transport that is plain HTTP
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;

                // As the browser does: the token travels in the cookie, not in a header or the query string
                if (accessToken is not null)
                {
                    options.Headers["Cookie"] = $"access_token={Uri.EscapeDataString(accessToken)}";
                }
            })
            .Build();

    private static TaskCompletionSource<SubmissionStatusNotification> Listen(HubConnection connection)
    {
        var received = new TaskCompletionSource<SubmissionStatusNotification>(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<SubmissionStatusNotification>(
            nameof(ISubmissionsClient.SubmissionStatusChanged),
            notification => received.TrySetResult(notification));

        return received;
    }

    private Task PublishAsync(SubmissionStatusChanged message) =>
        factory.Services.GetRequiredService<ITestHarness>().Bus.Publish(message);

    private async Task<(Guid UserId, string AccessToken)> RegisterStudentAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", TestDataBuilder.CreateValidRegisterDto(role: "Student"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();

        return (Guid.Parse(body!.UserId), response.GetAccessToken());
    }
}
