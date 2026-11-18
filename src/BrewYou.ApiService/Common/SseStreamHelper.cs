using System.Security.Claims;

namespace BrewYou.ApiService.Common;

public static class SseStreamHelper
{
    public static readonly TimeSpan DefaultMaxStreamLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Computes the remaining SSE connection lifetime, capped at the shorter of
    /// <see cref="DefaultMaxStreamLifetime"/> and the bearer token's remaining <c>exp</c> lifetime.
    /// Returns <see cref="TimeSpan.Zero"/> if the token is already expired.
    /// </summary>
    public static TimeSpan ComputeMaxStreamLifetime(ClaimsPrincipal principal, TimeSpan? maxLifetime = null)
    {
        var cap = maxLifetime ?? DefaultMaxStreamLifetime;
        var expClaim = principal.FindFirst("exp")?.Value;
        if (!string.IsNullOrWhiteSpace(expClaim) && long.TryParse(expClaim, out var expSeconds))
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (remaining < cap)
            {
                return remaining;
            }
        }

        return cap;
    }
}