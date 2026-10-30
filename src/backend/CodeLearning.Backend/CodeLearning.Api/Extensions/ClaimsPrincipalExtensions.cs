using System.Security.Claims;

namespace CodeLearning.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserIdOrNull(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
