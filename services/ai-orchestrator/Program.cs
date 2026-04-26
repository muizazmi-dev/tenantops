using Azure;
using Azure.AI.OpenAI;
using Dapper;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using TenantOps.AiOrchestrator.Rag;
using TenantOps.AiOrchestrator.Search;
using TenantOps.Shared;
using TenantOps.Shared.Data;
using TenantOps.Shared.Tenancy;

var builder = WebApplication.CreateBuilder(args);
builder.AddTenantOpsDefaults();

// -----------------------------------------------------------------
// Semantic Kernel registration
// -----------------------------------------------------------------
var cfg = builder.Configuration;
var aoaiEndpoint = cfg["AzureOpenAI:Endpoint"]
    ?? throw new InvalidOperationException("AzureOpenAI:Endpoint missing");
var aoaiKey = cfg["AzureOpenAI:ApiKey"]
    ?? throw new InvalidOperationException("AzureOpenAI:ApiKey missing");
var chatDeployment  = cfg["AzureOpenAI:ChatDeployment"]      ?? "gpt-4o-mini";
var embedDeployment = cfg["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

// Shared client — both chat and embeddings target the same endpoint/key.
var azureClient = new AzureOpenAIClient(new Uri(aoaiEndpoint), new AzureKeyCredential(aoaiKey));

#pragma warning disable SKEXP0010
var kernelBuilder = Kernel.CreateBuilder();
kernelBuilder.AddAzureOpenAIChatCompletion(chatDeployment, azureClient);
kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(embedDeployment, azureClient);
var kernel = kernelBuilder.Build();
#pragma warning restore SKEXP0010

builder.Services.AddSingleton(kernel);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Kernel>().GetRequiredService<IChatCompletionService>());
builder.Services.AddSingleton(sp => sp.GetRequiredService<Kernel>().GetRequiredService<ITextEmbeddingGenerationService>());
builder.Services.AddSingleton<SearchIndexBootstrap>();
builder.Services.AddSingleton<SearchRetriever>();
builder.Services.AddSingleton<DocumentIndexer>();
builder.Services.AddSingleton<RagOrchestrator>();

var app = builder.Build();
app.UseTenantOpsDefaults();

// Bootstrap the search index on startup (idempotent)
using (var scope = app.Services.CreateScope())
{
    var boot = scope.ServiceProvider.GetRequiredService<SearchIndexBootstrap>();
    try { await boot.EnsureCreatedAsync(); }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Search index bootstrap skipped (AI Search unavailable?)"); }
}

// -----------------------------------------------------------------
// POST /chat — grounded, tenant-scoped RAG chat
// -----------------------------------------------------------------
app.MapPost("/chat", async (ChatRequest req, TenantContext tc, RagOrchestrator rag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest("Message required.");
    var result = await rag.AskAsync(tc.TenantId, req.Message, ct);
    return Results.Ok(result);
}).RequireAuthorization();

// -----------------------------------------------------------------
// POST /documents/index — re-index this tenant's documents from SQL
// Reads Documents via tenant-scoped connection (RLS-bound), then uploads
// chunks into AI Search stamped with tenantId.
// -----------------------------------------------------------------
app.MapPost("/documents/index", async (TenantContext tc, ITenantDbConnectionFactory db,
                                       DocumentIndexer indexer, CancellationToken ct) =>
{
    await using var conn = await db.OpenAsync(ct);
    var rows = (await conn.QueryAsync<(Guid DocumentId, Guid TenantId, string Title, string Content, DateTime CreatedAt)>(
        @"SELECT DocumentId, TenantId, Title, Content, CreatedAt FROM app.Documents;")).ToList();

    foreach (var r in rows)
    {
        await indexer.IndexAsync(new DocumentToIndex(
            r.DocumentId, r.TenantId, r.Title, r.Content, new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)), ct);
        await conn.ExecuteAsync(
            "UPDATE app.Documents SET IndexedAt = SYSUTCDATETIME() WHERE DocumentId = @id;",
            new { id = r.DocumentId });
    }
    return Results.Ok(new { indexed = rows.Count });
}).RequireAuthorization();

// -----------------------------------------------------------------
// POST /documents/{id}/index — index a single document
// -----------------------------------------------------------------
app.MapPost("/documents/{id:guid}/index", async (Guid id, TenantContext tc,
    ITenantDbConnectionFactory db, DocumentIndexer indexer, CancellationToken ct) =>
{
    await using var conn = await db.OpenAsync(ct);
    var row = await conn.QuerySingleOrDefaultAsync<(Guid DocumentId, Guid TenantId, string Title, string Content, DateTime CreatedAt)?>(
        @"SELECT DocumentId, TenantId, Title, Content, CreatedAt FROM app.Documents WHERE DocumentId = @id;",
        new { id });
    if (row is null) return Results.NotFound();

    var r = row.Value;
    await indexer.IndexAsync(new DocumentToIndex(r.DocumentId, r.TenantId, r.Title, r.Content,
        new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)), ct);
    await conn.ExecuteAsync("UPDATE app.Documents SET IndexedAt = SYSUTCDATETIME() WHERE DocumentId = @id;", new { id });
    return Results.Ok(new { id, indexed = true });
}).RequireAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "ai-orchestrator", version = "1.0" }));

app.Run();

public record ChatRequest(string Message);
