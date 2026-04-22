using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace TenantOps.Shared.Auth;

/// <summary>
/// Registers two JWT Bearer schemes — "Entra" for Microsoft Entra ID OIDC tokens
/// (primary path) and "Local" for ASP.NET Identity-issued tokens (fallback).
/// The policy scheme "TenantOps" inspects the issuer claim and forwards to the
/// correct validator so a single [Authorize] covers both.
///
/// Docs:
///   OIDC on Entra: https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc
///   AddJwtBearer:  https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication
/// </summary>
public static class AuthSetup
{
    public const string PolicyScheme = "TenantOps";
    public const string EntraScheme = "Entra";
    public const string LocalScheme = "Local";

    public static IServiceCollection AddTenantOpsAuth(this IServiceCollection services, IConfiguration cfg)
    {
        var entraIssuer   = cfg["Entra:Issuer"]   ?? "https://login.microsoftonline.com/common/v2.0";
        var entraAudience = cfg["Entra:Audience"] ?? "api://tenantops";
        var localIssuer   = cfg["Jwt:Issuer"]     ?? "https://tenantops.local/identity";
        var localAudience = cfg["Jwt:Audience"]   ?? "tenantops-api";
        var localKey      = cfg["Jwt:Key"]        ?? throw new InvalidOperationException("Jwt:Key missing");

        services.AddAuthentication(PolicyScheme)
            .AddPolicyScheme(PolicyScheme, PolicyScheme, options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    // Peek at issuer to decide which validator to use.
                    var auth = ctx.Request.Headers.Authorization.ToString();
                    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = auth["Bearer ".Length..].Trim();
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        if (handler.CanReadToken(token))
                        {
                            var parsed = handler.ReadJwtToken(token);
                            if (parsed.Issuer.Contains("login.microsoftonline", StringComparison.OrdinalIgnoreCase))
                                return EntraScheme;
                        }
                    }
                    return LocalScheme;
                };
            })
            .AddJwtBearer(EntraScheme, o =>
            {
                o.Authority = entraIssuer;
                o.Audience = entraAudience;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(2)
                };
            })
            .AddJwtBearer(LocalScheme, o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = localIssuer,
                    ValidateAudience = true,
                    ValidAudience = localAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(localKey)),
                    ClockSkew = TimeSpan.FromMinutes(2)
                };
            });

        services.AddAuthorization();
        return services;
    }
}
