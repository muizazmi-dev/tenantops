namespace TenantOps.Shared.Tenancy;

/// <summary>
/// Per-request tenant context. Populated by TenantResolutionMiddleware after
/// it validates the incoming request's JWT claims and/or the APIM-injected
/// x-tenant-id header. Consumers (repositories, SK orchestrator, etc.) read
/// this rather than re-parsing headers, and it is impossible to mutate after
/// the middleware runs.
/// </summary>
public sealed class TenantContext
{
    public Guid TenantId { get; private set; }
    public string? Slug { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public bool IsSet { get; private set; }

    internal void Set(Guid tenantId, string? slug, Guid? userId, bool isPlatformAdmin)
    {
        if (IsSet)
            throw new InvalidOperationException("TenantContext already set for this request.");
        TenantId = tenantId;
        Slug = slug;
        UserId = userId;
        IsPlatformAdmin = isPlatformAdmin;
        IsSet = true;
    }
}
