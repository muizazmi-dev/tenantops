using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TenantOps.AiOrchestrator.Search;

public sealed record RetrievedChunk(string ChunkId, string DocumentId, string Title, string Content, double Score);

/// <summary>
/// Issues a vector + text hybrid query against the shared index with a MANDATORY
/// tenant filter. The filter is built here — not passed in from callers — so
/// the caller CANNOT accidentally or deliberately omit it.
///
/// A runtime guard rejects any constructed filter that would match across
/// tenants. See <see cref="BuildTenantFilter"/>.
/// </summary>
public sealed class SearchRetriever
{
    private readonly SearchClient _client;
    private readonly ILogger<SearchRetriever> _log;

    public SearchRetriever(IConfiguration cfg, ILogger<SearchRetriever> log)
    {
        var endpoint = cfg["AzureSearch:Endpoint"] ?? throw new InvalidOperationException("AzureSearch:Endpoint");
        var adminKey = cfg["AzureSearch:AdminKey"] ?? throw new InvalidOperationException("AzureSearch:AdminKey");
        var index    = cfg["AzureSearch:IndexName"] ?? "tenantops-kb";
        _client = new SearchClient(new Uri(endpoint), index, new AzureKeyCredential(adminKey));
        _log = log;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        Guid tenantId, string userQuery, ReadOnlyMemory<float> queryEmbedding,
        int topK = 5, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId is empty — refusing to query AI Search.");

        var filter = BuildTenantFilter(tenantId);
        AssertFilterIsTenantScoped(filter, tenantId);   // belt-and-braces

        var options = new SearchOptions
        {
            Filter = filter,
            Size = topK,
            VectorSearch = new()
            {
                Queries = { new VectorizedQuery(queryEmbedding) { KNearestNeighborsCount = topK, Fields = { "contentVector" } } }
            }
        };
        options.Select.Add("chunkId");
        options.Select.Add("documentId");
        options.Select.Add("title");
        options.Select.Add("content");

        Response<SearchResults<SearchDocument>> results = await _client.SearchAsync<SearchDocument>(userQuery, options, ct);

        var list = new List<RetrievedChunk>();
        await foreach (var r in results.Value.GetResultsAsync())
        {
            list.Add(new RetrievedChunk(
                ChunkId:    r.Document.GetString("chunkId") ?? "",
                DocumentId: r.Document.GetString("documentId") ?? "",
                Title:      r.Document.GetString("title") ?? "",
                Content:    r.Document.GetString("content") ?? "",
                Score:      r.Score ?? 0
            ));
        }

        _log.LogInformation("Retrieved {Count} chunks for tenant {Tenant}", list.Count, tenantId);
        return list;
    }

    // OData filter — see https://learn.microsoft.com/azure/search/search-query-odata-filter
    private static string BuildTenantFilter(Guid tenantId) => $"tenantId eq '{tenantId}'";

    private static void AssertFilterIsTenantScoped(string filter, Guid tenantId)
    {
        var expected = $"tenantId eq '{tenantId}'";
        if (!filter.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Tenant filter invariant broken — refusing to query.");
    }
}
