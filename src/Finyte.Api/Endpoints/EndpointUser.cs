using System.Security.Claims;

namespace Finyte.Api.Endpoints;

public static class EndpointUser
{
    public static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated user does not have a subject claim.");
    }
}
