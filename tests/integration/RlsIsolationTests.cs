using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace TenantOps.Tests;

/// <summary>
/// Proof that SESSION_CONTEXT + RLS filter + block predicates eliminate
/// cross-tenant data access regardless of what the application layer does.
///
/// These tests assume the SQL migrations and seed script have run against
/// the target server (docker-compose does this automatically for local).
///
/// Run with:   dotnet test tests/integration/TenantOps.Tests.csproj
///
/// Connection string is read from TENANTOPS_TEST_SQL (defaults to the local
/// compose stack).
/// </summary>
public class RlsIsolationTests
{
    private const string DefaultCs = "Server=localhost,1433;Database=tenantops;User Id=tenantops_app;Password=App_Strong_Passw0rd!;TrustServerCertificate=True;Encrypt=False;";
    private static readonly Guid ContosoId  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FabrikamId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string Cs => Environment.GetEnvironmentVariable("TENANTOPS_TEST_SQL") ?? DefaultCs;

    private static async Task<SqlConnection> OpenAs(Guid tenantId)
    {
        var c = new SqlConnection(Cs);
        await c.OpenAsync();
        await c.ExecuteAsync(
            "EXEC sp_set_session_context @key=N'TenantId', @value=@tid, @read_only=1;",
            new { tid = tenantId });
        return c;
    }

    [Fact]
    public async Task Contoso_sees_only_its_own_documents()
    {
        await using var c = await OpenAs(ContosoId);
        var rows = await c.QueryAsync<(Guid TenantId, string Title)>(
            "SELECT TenantId, Title FROM app.Documents;");
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.TenantId == ContosoId);
    }

    [Fact]
    public async Task Fabrikam_sees_only_its_own_documents()
    {
        await using var c = await OpenAs(FabrikamId);
        var rows = await c.QueryAsync<(Guid TenantId, string Title)>(
            "SELECT TenantId, Title FROM app.Documents;");
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.TenantId == FabrikamId);
    }

    [Fact]
    public async Task Cross_tenant_read_via_explicit_where_clause_returns_nothing()
    {
        // Attempt: connected as Contoso but query for Fabrikam's data.
        // Expectation: RLS FILTER predicate hides every Fabrikam row, so the
        // result is empty even though the WHERE clause would match.
        await using var c = await OpenAs(ContosoId);
        var leaked = await c.QueryAsync<Guid>(
            "SELECT DocumentId FROM app.Documents WHERE TenantId = @tid;",
            new { tid = FabrikamId });
        leaked.Should().BeEmpty("RLS must hide rows for other tenants");
    }

    [Fact]
    public async Task Cross_tenant_insert_is_blocked()
    {
        // Connected as Contoso, try to INSERT a row stamped with Fabrikam's id.
        // The BLOCK predicate should raise an error.
        await using var c = await OpenAs(ContosoId);
        Func<Task> act = () => c.ExecuteAsync(@"
            INSERT INTO app.Documents (DocumentId, TenantId, Title, Content)
            VALUES (NEWID(), @tid, 'malicious', 'should fail');",
            new { tid = FabrikamId });

        await act.Should().ThrowAsync<SqlException>("the BLOCK AFTER INSERT predicate must reject");
    }

    [Fact]
    public async Task Session_context_cannot_be_overwritten()
    {
        // read_only=1 means the second sp_set_session_context with the same
        // key must fail, preventing "tenant switching" on a single connection.
        await using var c = await OpenAs(ContosoId);
        Func<Task> act = () => c.ExecuteAsync(
            "EXEC sp_set_session_context @key=N'TenantId', @value=@tid;",
            new { tid = FabrikamId });
        await act.Should().ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task Connection_without_tenant_context_sees_nothing()
    {
        // Open without setting SESSION_CONTEXT('TenantId') — predicate returns
        // no rows because the CAST yields NULL which != any row TenantId.
        await using var c = new SqlConnection(Cs);
        await c.OpenAsync();
        var rows = await c.QueryAsync<Guid>("SELECT DocumentId FROM app.Documents;");
        rows.Should().BeEmpty("no tenant set => no rows visible");
    }
}
