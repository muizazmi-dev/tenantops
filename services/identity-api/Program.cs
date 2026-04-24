using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using TenantOps.Shared;

const string PlatformAdminEmail    = "admin@tenantops.dev";
const string PlatformAdminPassword = "12345"; // demo only — bypasses hash check

var builder = WebApplication.CreateBuilder(args);
builder.AddTenantOpsDefaults();

var app = builder.Build();
app.UseTenantOpsDefaults();

// --------------------------------------------------------------------------
// POST /auth/register — add a local user to an existing tenant
// --------------------------------------------------------------------------
app.MapPost("/auth/register", async (RegisterDto dto, IConfiguration cfg) =>
{
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest("Email and password required.");
    if (dto.TenantId == Guid.Empty) return Results.BadRequest("TenantId required.");

    var cs = cfg.GetConnectionString("Default")!;
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    await conn.ExecuteAsync(@"
        EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1, @read_only=1;
        EXEC sp_set_session_context @key=N'TenantId', @value=@tid, @read_only=1;",
        new { tid = dto.TenantId });

    var hash = HashPassword(dto.Password);
    var userId = Guid.NewGuid();
    try
    {
        await conn.ExecuteAsync(@"
            INSERT INTO app.Users(UserId, TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
            VALUES (@userId, @tid, @email, @name, 'local', @hash);",
            new { userId, tid = dto.TenantId, email = dto.Email, name = dto.DisplayName, hash });
    }
    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
    {
        return Results.Conflict("Email already registered for this tenant.");
    }
    return Results.Ok(new { userId });
}).AllowAnonymous();

// --------------------------------------------------------------------------
// POST /auth/register-tenant — create a brand-new tenant + admin user,
// returns JWT so the caller is signed in immediately.
// --------------------------------------------------------------------------
app.MapPost("/auth/register-tenant", async (RegisterTenantDto dto, IConfiguration cfg) =>
{
    if (string.IsNullOrWhiteSpace(dto.OrgName))       return Results.BadRequest("OrgName required.");
    if (string.IsNullOrWhiteSpace(dto.Slug))          return Results.BadRequest("Slug required.");
    if (string.IsNullOrWhiteSpace(dto.AdminEmail))    return Results.BadRequest("AdminEmail required.");
    if (string.IsNullOrWhiteSpace(dto.AdminPassword)) return Results.BadRequest("AdminPassword required.");

    if (!Regex.IsMatch(dto.Slug, @"^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$"))
        return Results.BadRequest("Slug must be 3–64 lowercase letters, digits, or hyphens.");

    var tenantId = Guid.NewGuid();
    var userId   = Guid.NewGuid();
    var hash     = HashPassword(dto.AdminPassword);

    var cs = cfg.GetConnectionString("Default")!;
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    await conn.ExecuteAsync(@"
        EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1, @read_only=1;
        EXEC sp_set_session_context @key=N'TenantId',        @value=@tid, @read_only=1;",
        new { tid = tenantId }, tx);

    try
    {
        await conn.ExecuteAsync(@"
            INSERT INTO app.Tenants (TenantId, Slug, Domain, Name, ThemeJson)
            VALUES (@tenantId, @slug, @domain, @name, '{}');",
            new { tenantId, slug = dto.Slug, domain = $"{dto.Slug}.localtest.me",
                  name = dto.OrgName }, tx);
    }
    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
    {
        await tx.RollbackAsync();
        return Results.Conflict("A tenant with that slug already exists.");
    }

    try
    {
        await conn.ExecuteAsync(@"
            INSERT INTO app.Users (UserId, TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
            VALUES (@userId, @tenantId, @email, @displayName, 'local', @hash);",
            new { userId, tenantId, email = dto.AdminEmail,
                  displayName = string.IsNullOrWhiteSpace(dto.AdminDisplayName)
                                ? dto.AdminEmail : dto.AdminDisplayName, hash }, tx);
    }
    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
    {
        await tx.RollbackAsync();
        return Results.UnprocessableEntity("Email already registered. Try signing in instead.");
    }

    await tx.CommitAsync();

    var user  = new UserRow { UserId = userId, TenantId = tenantId, Email = dto.AdminEmail,
                              DisplayName = dto.AdminDisplayName };
    var token = IssueLocalToken(cfg, user, dto.Slug);
    return Results.Ok(new { tenantId, slug = dto.Slug, userId,
                            access_token = token, token_type = "Bearer", expires_in = 3600 });
}).AllowAnonymous();

// --------------------------------------------------------------------------
// POST /auth/login — issue a JWT for a local user (tenant-scoped)
// Returns the tenant slug so the UI can redirect to /t/{slug}.
// --------------------------------------------------------------------------
app.MapPost("/auth/login", async (LoginDto dto, IConfiguration cfg) =>
{
    if (dto.TenantId == Guid.Empty) return Results.BadRequest("TenantId required.");

    var cs = cfg.GetConnectionString("Default")!;
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();
    await conn.ExecuteAsync(@"
        EXEC sp_set_session_context @key=N'TenantId', @value=@tid, @read_only=1;",
        new { tid = dto.TenantId });

    var user = await conn.QuerySingleOrDefaultAsync<UserRow>(@"
        SELECT UserId, TenantId, Email, DisplayName, PasswordHash
        FROM app.Users
        WHERE TenantId = @tid AND Email = @email AND IdentityProvider = 'local';",
        new { tid = dto.TenantId, email = dto.Email });

    if (user is null || !VerifyPassword(dto.Password, user.PasswordHash))
        return Results.Unauthorized();

    // Fetch slug so the client can redirect to /t/{slug} without needing a second call.
    var slug = await conn.QuerySingleOrDefaultAsync<string>(
        "SELECT Slug FROM app.Tenants WHERE TenantId = @tid;",
        new { tid = dto.TenantId });

    var token = IssueLocalToken(cfg, user, slug);
    return Results.Ok(new { access_token = token, token_type = "Bearer",
                            expires_in = 3600, slug });
}).AllowAnonymous();

// --------------------------------------------------------------------------
// POST /auth/admin-login — platform admin only, no tenant context required.
// Password is checked against a hardcoded constant (demo only).
// --------------------------------------------------------------------------
app.MapPost("/auth/admin-login", async (AdminLoginDto dto, IConfiguration cfg) =>
{
    if (!string.Equals(dto.Email, PlatformAdminEmail, StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();
    if (!string.Equals(dto.Password, PlatformAdminPassword, StringComparison.Ordinal))
        return Results.Unauthorized();

    var cs = cfg.GetConnectionString("Default")!;
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();
    await conn.ExecuteAsync(
        "EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1, @read_only=1;");

    var user = await conn.QuerySingleOrDefaultAsync<UserRow>(@"
        SELECT UserId, TenantId, Email, DisplayName, PasswordHash
        FROM app.Users WHERE Email = @email AND IdentityProvider = 'local';",
        new { email = PlatformAdminEmail });

    if (user is null) return Results.StatusCode(503); // seed not applied yet

    var token = IssueLocalToken(cfg, user, null); // platform admin has no tenant slug
    return Results.Ok(new { access_token = token, token_type = "Bearer", expires_in = 3600,
                            slug = (string?)null });
}).AllowAnonymous();

app.MapGet("/", () => Results.Ok(new { service = "identity-api", version = "1.0" }));

app.Run();

// --------------------------------------------------------------------------
// helpers
// --------------------------------------------------------------------------
static string HashPassword(string pwd)
{
    var salt = new byte[16];
    System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
    var key = KeyDerivation.Pbkdf2(pwd, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);
    return $"pbkdf2${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
}

static bool VerifyPassword(string pwd, string? stored)
{
    if (string.IsNullOrWhiteSpace(stored)) return false;
    if (!stored.StartsWith("pbkdf2$")) return false;
    var parts = stored.Split('$');
    if (parts.Length != 3) return false;
    var salt     = Convert.FromBase64String(parts[1]);
    var expected = Convert.FromBase64String(parts[2]);
    var actual   = KeyDerivation.Pbkdf2(pwd, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
}

static string IssueLocalToken(IConfiguration cfg, UserRow user, string? tenantSlug)
{
    var issuer   = cfg["Jwt:Issuer"]!;
    var audience = cfg["Jwt:Audience"]!;
    var key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
    var creds    = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new("tid_app", user.TenantId.ToString()),
        new("name", user.DisplayName ?? user.Email)
    };

    if (!string.IsNullOrEmpty(tenantSlug))
        claims.Add(new Claim("tenant_slug", tenantSlug));

    if (string.Equals(user.Email, PlatformAdminEmail, StringComparison.OrdinalIgnoreCase))
        claims.Add(new Claim("role", "platform-admin"));

    var jwt = new JwtSecurityToken(issuer, audience, claims,
        expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(jwt);
}

public record RegisterDto(Guid TenantId, string Email, string Password, string? DisplayName);
public record RegisterTenantDto(string OrgName, string Slug, string AdminEmail,
                                string AdminPassword, string? AdminDisplayName);
public record LoginDto(Guid TenantId, string Email, string Password);
public record AdminLoginDto(string Email, string Password);

public record UserRow
{
    public Guid    UserId      { get; init; }
    public Guid    TenantId    { get; init; }
    public string  Email       { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? PasswordHash { get; init; }
}
