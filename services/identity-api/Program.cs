using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using TenantOps.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddTenantOpsDefaults();

var app = builder.Build();
app.UseTenantOpsDefaults();

// --------------------------------------------------------------------------
// POST /auth/register  — create a local user under a tenant (demo only)
// --------------------------------------------------------------------------
app.MapPost("/auth/register", async (RegisterDto dto, IConfiguration cfg) =>
{
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest("Email and password required.");
    if (dto.TenantId == Guid.Empty) return Results.BadRequest("TenantId required.");

    var cs = cfg.GetConnectionString("Default")!;
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    // This endpoint is unauthenticated so we temporarily elevate for the
    // Users insert. Audited. In production this flow is replaced by Entra.
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
// POST /auth/login — issue a local JWT carrying the tenant claim
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

    var token = IssueLocalToken(cfg, user);
    return Results.Ok(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
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
    if (!stored.StartsWith("pbkdf2$")) return false;          // placeholder seed user
    var parts = stored.Split('$');
    if (parts.Length != 3) return false;
    var salt = Convert.FromBase64String(parts[1]);
    var expected = Convert.FromBase64String(parts[2]);
    var actual = KeyDerivation.Pbkdf2(pwd, salt, KeyDerivationPrf.HMACSHA256, 100_000, 32);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
}

static string IssueLocalToken(IConfiguration cfg, UserRow user)
{
    var issuer = cfg["Jwt:Issuer"]!;
    var audience = cfg["Jwt:Audience"]!;
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        // the canonical tenant claim read by TenantResolutionMiddleware
        new("tid_app", user.TenantId.ToString()),
        new("name", user.DisplayName ?? user.Email)
    };

    var jwt = new JwtSecurityToken(issuer, audience, claims,
        expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(jwt);
}

public record RegisterDto(Guid TenantId, string Email, string Password, string? DisplayName);
public record LoginDto(Guid TenantId, string Email, string Password);

public record UserRow
{
    public Guid UserId { get; init; }
    public Guid TenantId { get; init; }
    public string Email { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? PasswordHash { get; init; }
}
