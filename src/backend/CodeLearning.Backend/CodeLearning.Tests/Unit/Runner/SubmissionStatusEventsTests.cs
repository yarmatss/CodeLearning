extern alias Runner;

using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Runner::CodeLearning.Runner.Consumers;

namespace CodeLearning.Tests.Unit.Runner;

public class SubmissionStatusEventsTests
{
    [Fact]
    public async Task PublishAsync_ShouldPublishTheStatus()
    {
        // Arrange
        var expected = new SubmissionStatusChanged(Guid.NewGuid(), Guid.NewGuid(), SubmissionStatus.Completed);
        var publishEndpoint = new Mock<IPublishEndpoint>();

        // Act
        await SubmissionStatusEvents.PublishAsync(
            publishEndpoint.Object, NullLogger.Instance, expected.SubmissionId, expected.StudentId, expected.Status, CancellationToken.None);

        // Assert
        publishEndpoint.Verify(p => p.Publish(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The result is already stored when the status is announced; an exception here would fail the
    // message and run the student's code a second time
    [Fact]
    public async Task PublishAsync_BrokerFailure_ShouldNotThrow()
    {
        // Arrange
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<SubmissionStatusChanged>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ is unreachable"));

        // Act
        var act = () => SubmissionStatusEvents.PublishAsync(
            publishEndpoint.Object, NullLogger.Instance, Guid.NewGuid(), Guid.NewGuid(), SubmissionStatus.Completed, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
