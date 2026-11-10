using CodeLearning.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CodeLearning.Api.Realtime;

public sealed record SubmissionStatusNotification(Guid SubmissionId, SubmissionStatus Status);

// What the server calls on connected browsers
public interface ISubmissionsClient
{
    Task SubmissionStatusChanged(SubmissionStatusNotification notification);
}

// Server-to-client only: tells a student that one of their submissions changed status, so the page can
// fetch it instead of polling. The connection is authenticated by the same access-token cookie as the
// REST API, and each notification goes to the submitting user's connections only (Clients.User)
[Authorize]
public sealed class SubmissionsHub : Hub<ISubmissionsClient>
{
    public const string Route = "/api/hubs/submissions";
}
