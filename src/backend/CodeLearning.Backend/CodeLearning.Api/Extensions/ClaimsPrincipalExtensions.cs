using CodeLearning.Application.Exceptions;
using System.Security.Claims;

namespace CodeLearning.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    extension(ClaimsPrincipal user)
    {
        public Guid? GetUserIdOrNull() =>
            Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

        // For [Authorize] endpoints; a missing or malformed id is answered with 401, never 500
        public Guid GetRequiredUserId() =>
            user.GetUserIdOrNull() ?? throw new AuthenticationFailedException("User not authenticated");
    }

}
