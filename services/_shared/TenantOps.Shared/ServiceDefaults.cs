using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TenantOps.Shared.Auth;
using TenantOps.Shared.Data;
using TenantOps.Shared.Tenancy;

namespace TenantOps.Shared;

/// <summary>
/// One-liner bootstrap used by all four .NET services. Keeps service Program.cs
/// files focused on their own endpoints.
/// </summary>
public static class ServiceDefaults
{
    public static WebApplicationBuilder AddTenantOpsDefaults(this WebApplicationBuilder b)
    {
        b.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", ctx.HostingEnvironment.ApplicationName)
            .WriteTo.Console());

        b.Services.AddHttpContextAccessor();
        b.Services.AddScoped<TenantContext>();
        b.Services.AddScoped<ITenantDbConnectionFactory, TenantDbConnectionFactory>();
        b.Services.AddTenantOpsAuth(b.Configuration);

        b.Services.AddHealthChecks();
        b.Services.AddEndpointsApiExplorer();
        b.Services.AddSwaggerGen();

        return b;
    }

    public static WebApplication UseTenantOpsDefaults(this WebApplication app)
    {
        app.UseSerilogRequestLogging();

        // Correlation ID: echo incoming header or mint a new one.
        app.Use(async (ctx, next) =>
        {
            var corr = ctx.Request.Headers["x-correlation-id"].ToString();
            if (string.IsNullOrWhiteSpace(corr)) corr = Guid.NewGuid().ToString("N");
            ctx.Response.Headers["x-correlation-id"] = corr;
            using (Serilog.Context.LogContext.PushProperty("CorrelationId", corr))
                await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseTenantResolution();

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        return app;
    }
}
