using System.Security.Claims;

namespace Finyte.Api.Auth;

public static class ClerkAuthorizedParties
{
    public const string SectionName = "Clerk:AuthorizedParties";

    public static IReadOnlyList<string> Read(IConfiguration configuration) =>
        (configuration.GetSection(SectionName).Get<string[]>() ?? []).Select(Origin).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsAllowed(ClaimsPrincipal? principal, IReadOnlyList<string> parties)
    {
        if (parties.Count == 0)
        {
            return true;
        }
        var party = principal?.FindFirst("azp")?.Value;
        return !string.IsNullOrWhiteSpace(party) && parties.Contains(Origin(party), StringComparer.OrdinalIgnoreCase);
    }

    private static string Origin(string value) => value.Trim().TrimEnd('/');
}
