using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TenantOps.Shared.Tenancy;

/// <summary>
/// Resolves the tenant for the current request and enforces that any caller-
/// supplied tenant identifier matches the authenticated principal's claims.
///
/// Trust model
/// -----------
/// * In Azure: Azure Front Door terminates TLS and forwards to APIM. APIM
///   validates the bearer JWT (validate-jwt policy) and injects the header
///   `x-tenant-id` sourced EITHER from a validated tenant claim OR from a
///   domain→tenant lookup against tenant-api. Backend services therefore
///   receive TWO independent signals: the bearer token (with its own tenant
///   claim) and the header. This middleware requires them to agree.
///
/// * In local dev: the web app calls services directly; the JWT still carries
///   a tenant claim (see identity-api) and the same equality check is applied.
///
/// * Cross-tenant attack: if a Contoso user forges x-tenant-id=fabrikam,
///   the JWT claim still says contoso — mismatch → 403. If they obtain a
///   Fabrikam JWT, APIM's validate-jwt rejects it because audience/issuer
///   won't match. There is no path to act on a tenant you don't own a token for.
///
/// References:
///   JWT validate-jwt policy: https://learn.microsoft.com/azure/api-management/validate-jwt-policy
///   Claim types & ClaimsPrincipal: https://learn.microsoft.com/dotnet/api/system.security.claims.claimsprincipal
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantHeader = "x-tenant-id";
    public const string PlatformAdminHeader = "x-platform-admin";
    public const string TenantClaimType = "tid_app"; // our own app-level tenant claim

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, TenantContext tenantContext)
    {
        // Skip anonymous surfaces: health probes and the identity login endpoint
        // that has to run before a tenant is known.
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/auth/register", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/resolve", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // Principal must exist (authentication middleware has already run).
        var user = ctx.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthenticated.");
            return;
        }

        // 1. Authoritative source: tenant claim on the validated JWT.
        var claimTenant = user.FindFirstValue(TenantClaimType)
                          ?? user.FindFirstValue("extension_TenantId")   // Entra custom ext
                          ?? user.FindFirstValue("tenantId");
        if (!Guid.TryParse(claimTenant, out var tenantFromClaim))
        {
            _logger.LogWarning("JWT missing or malformed tenant claim.");
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Tenant claim required.");
            return;
        }

        // 2. Corroborating signal: x-tenant-id header injected by APIM.
        //    If present, it MUST match the JWT claim — APIM and token must agree.
        if (ctx.Request.Headers.TryGetValue(TenantHeader, out var headerValues))
        {
            if (!Guid.TryParse(headerValues.ToString(), out var tenantFromHeader) ||
                tenantFromHeader != tenantFromClaim)
            {
                _logger.LogWarning(
                    "Tenant header {Header} does not match JWT claim {Claim}. Rejecting.",
                    headerValues.ToString(), tenantFromClaim);
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Tenant context mismatch.");
                return;
            }
        }

        // 3. Optional userId + platform-admin claim.
        Guid? userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                                     ?? user.FindFirstValue("oid")
                                     ?? user.FindFirstValue("sub"), out var uid)
            ? uid : null;
        var isPlatformAdmin = user.HasClaim("role", "platform-admin");

        var slug = user.FindFirstValue("tenant_slug");

        tenantContext.Set(tenantFromClaim, slug, userId, isPlatformAdmin);
        ctx.Items["TenantId"] = tenantFromClaim;

        // Surface on the log scope so every log line carries TenantId.
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantFromClaim,
            ["UserId"] = userId ?? Guid.Empty
        }))
        {
            await _next(ctx);
        }
    }
}

public static class TenantResolutionExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
