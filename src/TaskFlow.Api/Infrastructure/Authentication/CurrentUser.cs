using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace TaskFlow.Api.Infrastructure.Authentication;

public static class CurrentUser
{
    public static Guid GetId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }
}
