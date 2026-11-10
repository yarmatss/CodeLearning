using CodeLearning.Core.Messages;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace CodeLearning.Api.Realtime;

// Relays the Runner's status events to the browser of the student who made the submission
public sealed class SubmissionStatusChangedConsumer(
    IHubContext<SubmissionsHub, ISubmissionsClient> hubContext) : IConsumer<SubmissionStatusChanged>
{
    public Task Consume(ConsumeContext<SubmissionStatusChanged> context)
    {
        var message = context.Message;

        // The user id of a connection is its NameIdentifier claim, the same claim the controllers read
        return hubContext.Clients
            .User(message.StudentId.ToString())
            .SubmissionStatusChanged(new SubmissionStatusNotification(message.SubmissionId, message.Status));
    }
}
