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
// PUBLIC RESOLVER — called by APIM policy AND by Next.js SSR.
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
// PLATFORM ADMIN — list all tenants.
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
// Per-tenant branding fetch.
// --------------------------------------------------------------------------
app.MapGet("/me/tenant", async (HttpContext ctx, IMemoryCache cache, ITenantDbConnectionFactory db) =>
{
    var tenantId = (Guid)ctx.Items["TenantId"]!;
    var key = $"branding:{tenantId}";
    if (cache.TryGetValue<TenantBranding?>(key, out var cached) && cached is not null)
        return Results.Ok(cached);

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

// --------------------------------------------------------------------------
// POST /me/tenant/logo — store PNG logo as a base64 data URL in ThemeJson.
// Accepts JSON { logoBase64: "...", mime: "image/png" }.
// PNG magic bytes are validated server-side.
// --------------------------------------------------------------------------
app.MapPost("/me/tenant/logo", async (HttpContext ctx, LogoUploadDto dto,
        ITenantDbConnectionFactory db, IMemoryCache cache) =>
{
    var tenantId = (Guid)ctx.Items["TenantId"]!;

    if (dto.Mime != "image/png")
        return Results.BadRequest("Only PNG images are accepted.");

    byte[] bytes;
    try { bytes = Convert.FromBase64String(dto.LogoBase64); }
    catch { return Results.BadRequest("Invalid base64 data."); }

    if (bytes.Length > 256 * 1024)
        return Results.BadRequest("Logo must be under 256 KB.");

    // Validate PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
    if (bytes.Length < 8 || !bytes.Take(8).SequenceEqual(Constants.PngMagic))
        return Results.BadRequest("File is not a valid PNG.");

    var dataUrl = $"data:image/png;base64,{dto.LogoBase64}";

    await using var conn = await db.OpenPlatformAdminAsync();

    // JSON_MODIFY keeps the rest of the ThemeJson intact (primary, accent, etc.)
    await conn.ExecuteAsync(@"
        UPDATE app.Tenants
        SET ThemeJson = JSON_MODIFY(ISNULL(ThemeJson, '{}'), '$.logo', @logo)
        WHERE TenantId = @tenantId;",
        new { logo = dataUrl, tenantId });

    // Evict resolve + branding caches so the new logo is visible on next request.
    var slug = await conn.QuerySingleOrDefaultAsync<string>(
        @"SELECT Slug FROM app.Tenants WHERE TenantId=@tenantId;", new { tenantId });

    cache.Remove($"branding:{tenantId}");
    if (slug is not null) cache.Remove($"tenant::{slug}");

    return Results.Ok(new { uploaded = true });
}).RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "tenant-api", version = "1.0" }));

app.Run();

public record TenantBranding(Guid TenantId, string Slug, string? Domain, string Name, string ThemeJson);
public record LogoUploadDto(string LogoBase64, string Mime);

static class Constants
{
    public static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
}
