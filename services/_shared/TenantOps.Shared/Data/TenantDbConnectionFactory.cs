using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TenantOps.Shared.Data;

/// <summary>
/// Opens a SqlConnection and immediately sets SESSION_CONTEXT('TenantId') so
/// that Row-Level Security predicates (see infra/sql/migrations/002_rls.sql)
/// filter every SELECT/INSERT/UPDATE/DELETE transparently.
///
/// read_only = 1 means the session context key cannot be overwritten on this
/// connection — so even a compromised repository method cannot "switch tenant"
/// mid-request. The connection must be disposed before a different tenant
/// could ever be set.
///
/// Because connections are pooled, SESSION_CONTEXT is cleared on close/reset,
/// so no leakage occurs between pooled uses. Verified behaviour documented at:
///   https://learn.microsoft.com/sql/t-sql/functions/session-context-transact-sql
/// </summary>
public interface ITenantDbConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);
    Task<SqlConnection> OpenPlatformAdminAsync(CancellationToken ct = default);
}

public sealed class TenantDbConnectionFactory : ITenantDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly Tenancy.TenantContext _tenantContext;

    public TenantDbConnectionFactory(IConfiguration config, Tenancy.TenantContext tenantContext)
    {
        _connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default missing");
        _tenantContext = tenantContext;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        if (!_tenantContext.IsSet)
            throw new InvalidOperationException(
                "TenantContext not set — OpenAsync must be called inside a tenant-scoped request.");

        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            EXEC sp_set_session_context @key=N'TenantId',        @value=@tid, @read_only=1;
            EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=0,    @read_only=1;";
        cmd.Parameters.Add(new SqlParameter("@tid", _tenantContext.TenantId));
        await cmd.ExecuteNonQueryAsync(ct);
        return conn;
    }

    /// <summary>
    /// Escape hatch used ONLY by tenant-api for catalogue reads on app.Tenants.
    /// Should never be exposed through business endpoints. Audited.
    /// </summary>
    public async Task<SqlConnection> OpenPlatformAdminAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1, @read_only=1;";
        await cmd.ExecuteNonQueryAsync(ct);
        return conn;
    }
}
