using CodeLearning.Api.Realtime;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace CodeLearning.Tests.Unit.Realtime;

public class SubmissionStatusChangedConsumerTests
{
    [Fact]
    public async Task Consume_ShouldNotifyOnlyTheSubmittingStudent()
    {
        // Arrange
        var message = new SubmissionStatusChanged(Guid.NewGuid(), Guid.NewGuid(), SubmissionStatus.Completed);

        var studentClient = new Mock<ISubmissionsClient>();
        var clients = new Mock<IHubClients<ISubmissionsClient>>(MockBehavior.Strict);
        clients.Setup(c => c.User(message.StudentId.ToString())).Returns(studentClient.Object);

        var hubContext = new Mock<IHubContext<SubmissionsHub, ISubmissionsClient>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        var consumer = new SubmissionStatusChangedConsumer(hubContext.Object);
        var context = Mock.Of<ConsumeContext<SubmissionStatusChanged>>(c => c.Message == message);

        // Act
        await consumer.Consume(context);

        // Assert: the strict mock already rejects any other audience (All, Group, another user)
        studentClient.Verify(
            c => c.SubmissionStatusChanged(new SubmissionStatusNotification(message.SubmissionId, message.Status)),
            Times.Once);
    }
}
