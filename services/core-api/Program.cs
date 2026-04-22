using Dapper;
using TenantOps.Shared;
using TenantOps.Shared.Data;
using TenantOps.Shared.Tenancy;

var builder = WebApplication.CreateBuilder(args);
builder.AddTenantOpsDefaults();

var app = builder.Build();
app.UseTenantOpsDefaults();

// Every endpoint below is tenant-scoped. The app writes queries as if
// single-tenant — RLS transparently rewrites them to filter by TenantId.
// Notice: NOT A SINGLE "WHERE TenantId = @tid" CLAUSE. That's the point.
// If a bug ever omits the tenant filter, RLS still enforces it.

// -------------------- TICKETS ---------------------------------------------

app.MapGet("/tickets", async (ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var rows = await conn.QueryAsync<Ticket>(@"
        SELECT TicketId, TenantId, Title, Body, Status, CreatedAt, UpdatedAt
        FROM app.Tickets ORDER BY CreatedAt DESC;");
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/tickets/{id:guid}", async (Guid id, ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var row = await conn.QuerySingleOrDefaultAsync<Ticket>(@"
        SELECT TicketId, TenantId, Title, Body, Status, CreatedAt, UpdatedAt
        FROM app.Tickets WHERE TicketId = @id;", new { id });
    return row is null ? Results.NotFound() : Results.Ok(row);
}).RequireAuthorization();

app.MapPost("/tickets", async (CreateTicket dto, TenantContext tc, ITenantDbConnectionFactory db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Title)) return Results.BadRequest("Title required.");

    await using var conn = await db.OpenAsync();
    var id = Guid.NewGuid();
    await conn.ExecuteAsync(@"
        INSERT INTO app.Tickets (TicketId, TenantId, Title, Body, Status, CreatedBy)
        VALUES (@id, @tid, @title, @body, 'open', @uid);",
        new { id, tid = tc.TenantId, title = dto.Title, body = dto.Body, uid = tc.UserId });
    return Results.Created($"/tickets/{id}", new { ticketId = id });
}).RequireAuthorization();

app.MapPut("/tickets/{id:guid}", async (Guid id, UpdateTicket dto, ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var affected = await conn.ExecuteAsync(@"
        UPDATE app.Tickets
        SET Title = COALESCE(@title, Title),
            Body  = COALESCE(@body,  Body),
            Status = COALESCE(@status, Status),
            UpdatedAt = SYSUTCDATETIME()
        WHERE TicketId = @id;",
        new { id, dto.Title, dto.Body, dto.Status });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/tickets/{id:guid}", async (Guid id, ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var affected = await conn.ExecuteAsync("DELETE FROM app.Tickets WHERE TicketId = @id;", new { id });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization();

// -------------------- DOCUMENTS (Knowledge Base) --------------------------

app.MapGet("/documents", async (ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var rows = await conn.QueryAsync<DocumentListItem>(@"
        SELECT DocumentId, TenantId, Title, CreatedAt, IndexedAt
        FROM app.Documents ORDER BY CreatedAt DESC;");
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/documents/{id:guid}", async (Guid id, ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var row = await conn.QuerySingleOrDefaultAsync<Document>(@"
        SELECT DocumentId, TenantId, Title, Content, CreatedAt, IndexedAt
        FROM app.Documents WHERE DocumentId = @id;", new { id });
    return row is null ? Results.NotFound() : Results.Ok(row);
}).RequireAuthorization();

app.MapPost("/documents", async (CreateDocument dto, TenantContext tc, ITenantDbConnectionFactory db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content))
        return Results.BadRequest("Title and Content required.");

    await using var conn = await db.OpenAsync();
    var id = Guid.NewGuid();
    await conn.ExecuteAsync(@"
        INSERT INTO app.Documents (DocumentId, TenantId, Title, Content, CreatedBy)
        VALUES (@id, @tid, @title, @content, @uid);",
        new { id, tid = tc.TenantId, title = dto.Title, content = dto.Content, uid = tc.UserId });
    return Results.Created($"/documents/{id}", new { documentId = id });
}).RequireAuthorization();

app.MapDelete("/documents/{id:guid}", async (Guid id, ITenantDbConnectionFactory db) =>
{
    await using var conn = await db.OpenAsync();
    var affected = await conn.ExecuteAsync("DELETE FROM app.Documents WHERE DocumentId = @id;", new { id });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "core-api", version = "1.0" }));

app.Run();

// records
public record Ticket(Guid TicketId, Guid TenantId, string Title, string? Body, string Status, DateTime CreatedAt, DateTime UpdatedAt);
public record CreateTicket(string Title, string? Body);
public record UpdateTicket(string? Title, string? Body, string? Status);

public record Document(Guid DocumentId, Guid TenantId, string Title, string Content, DateTime CreatedAt, DateTime? IndexedAt);
public record DocumentListItem(Guid DocumentId, Guid TenantId, string Title, DateTime CreatedAt, DateTime? IndexedAt);
public record CreateDocument(string Title, string Content);
