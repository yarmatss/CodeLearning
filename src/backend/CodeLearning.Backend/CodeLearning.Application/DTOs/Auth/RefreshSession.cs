namespace CodeLearning.Application.DTOs.Auth;

// One signed-in device or browser. The id stays the same across refresh-token rotations and is
// carried in the access token's sid claim, so logout can find the session without the refresh cookie
public sealed record RefreshSession(Guid UserId, Guid SessionId);
