using Dapper;
using Microsoft.Extensions.Caching.Memory;
using TenantOps.Shared;
using TenantOps.Shared.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddTenantOpsDefaults();
builder.Services.AddMemoryCache();

var app = builder.Build();
app.UseTenantOpsDefaults();

// --------------------------------------------------------------------------
// PUBLIC RESOLVER — called by APIM policy AND by Next.js SSR to determine
// which tenant owns the incoming host or the /t/{slug} route. No auth needed
// because the domain/slug IS the identifier. Cached aggressively.
// --------------------------------------------------------------------------
app.MapGet("/resolve", async (string? host, string? slug,
        IMemoryCache cache, ITenantDbConnectionFactory db) =>
{
    if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(slug))
        return Results.BadRequest("Provide ?host= or ?slug=");

    var cacheKey = $"tenant:{host}:{slug}";
    if (cache.TryGetValue<TenantBranding?>(cacheKey, out var cached))
        return cached is null ? Results.NotFound() : Results.Ok(cached);

    await using var conn = await db.OpenPlatformAdminAsync();
    var row = await conn.QuerySingleOrDefaultAsync<TenantBranding>(@"
        SELECT TOP 1 TenantId, Slug, Domain, Name, ThemeJson
        FROM app.Tenants
        WHERE IsActive = 1
          AND ( (@host IS NOT NULL AND Domain = @host)
             OR (@slug IS NOT NULL AND Slug   = @slug) );",
        new { host, slug });

    cache.Set(cacheKey, row, TimeSpan.FromMinutes(5));
    return row is null ? Results.NotFound() : Results.Ok(row);
}).AllowAnonymous();

// --------------------------------------------------------------------------
// PLATFORM ADMIN — list all tenants (requires platform-admin role claim).
// Used by the admin UI panel to show per-tenant usage.
// --------------------------------------------------------------------------
app.MapGet("/admin/tenants", async (ITenantDbConnectionFactory db, HttpContext ctx) =>
{
    if (!ctx.User.HasClaim("role", "platform-admin")) return Results.Forbid();
    await using var conn = await db.OpenPlatformAdminAsync();
    var rows = await conn.QueryAsync<TenantBranding>(@"
        SELECT TenantId, Slug, Domain, Name, ThemeJson FROM app.Tenants ORDER BY Name;");
    return Results.Ok(rows);
}).RequireAuthorization();

// --------------------------------------------------------------------------
// Per-tenant branding fetch — trust the validated x-tenant-id header (from
// APIM) or JWT claim. Caller cannot switch tenant because the middleware
// rejects header/claim mismatches.
// --------------------------------------------------------------------------
app.MapGet("/me/tenant", async (HttpContext ctx, IMemoryCache cache, ITenantDbConnectionFactory db) =>
{
    var tenantId = (Guid)ctx.Items["TenantId"]!;
    var key = $"branding:{tenantId}";
    if (cache.TryGetValue<TenantBranding?>(key, out var cached) && cached is not null)
        return Results.Ok(cached);

    await using var conn = await db.OpenAsync();
    // The RLS predicate will gate Users/Documents/Tickets; Tenants is read via
    // platform-admin connection instead. Here we do a direct lookup by PK.
    var row = await cache.GetOrCreateAsync(key, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        await using var admin = await db.OpenPlatformAdminAsync();
        return await admin.QuerySingleOrDefaultAsync<TenantBranding>(
            @"SELECT TenantId, Slug, Domain, Name, ThemeJson FROM app.Tenants WHERE TenantId=@id;",
            new { id = tenantId });
    });
    return row is null ? Results.NotFound() : Results.Ok(row);
}).RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "tenant-api", version = "1.0" }));

app.Run();

public record TenantBranding(Guid TenantId, string Slug, string? Domain, string Name, string ThemeJson);
