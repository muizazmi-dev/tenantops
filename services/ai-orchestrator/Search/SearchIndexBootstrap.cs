using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TenantOps.AiOrchestrator.Search;

/// <summary>
/// Creates (idempotently) the single shared vector index that stores chunks
/// from every tenant's documents. Isolation is enforced at QUERY time via a
/// MANDATORY filter on the `tenantId` field — see SearchRetriever.
///
/// We deliberately do NOT use an index-per-tenant because:
///   * Azure AI Search caps index count per service tier (15 on Basic, 50 on S1).
///   * Cold-start for new tenants would require provisioning an index.
/// The trade-off is that the tenant filter becomes a security-critical
/// control. We assert it is present in every query and add a defensive
/// exception if it is ever dropped.
///
/// Index schema ref:
///   https://learn.microsoft.com/azure/search/vector-search-how-to-create-index
///   Supported types:  https://learn.microsoft.com/rest/api/searchservice/supported-data-types
/// </summary>
public sealed class SearchIndexBootstrap
{
    private readonly SearchIndexClient _client;
    private readonly string _indexName;
    private readonly int _embeddingDimensions;
    private readonly ILogger<SearchIndexBootstrap> _log;

    public SearchIndexBootstrap(IConfiguration cfg, ILogger<SearchIndexBootstrap> log)
    {
        var endpoint = cfg["AzureSearch:Endpoint"] ?? throw new InvalidOperationException("AzureSearch:Endpoint");
        var adminKey = cfg["AzureSearch:AdminKey"] ?? throw new InvalidOperationException("AzureSearch:AdminKey");
        _indexName = cfg["AzureSearch:IndexName"] ?? "tenantops-kb";
        _embeddingDimensions = int.TryParse(cfg["AzureSearch:EmbeddingDimensions"], out var d) ? d : 1536;

        _client = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(adminKey));
        _log = log;
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        try
        {
            await _client.GetIndexAsync(_indexName, ct);
            _log.LogInformation("Search index {Index} already exists", _indexName);
            return;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* fall through */ }

        var profile = "default-vector-profile";
        var algorithm = "hnsw-config";

        var index = new SearchIndex(_indexName)
        {
            Fields =
            {
                new SimpleField("chunkId",    SearchFieldDataType.String)             { IsKey = true, IsFilterable = true },
                // CRITICAL: filterable tenant discriminator. This IS the tenant boundary in the index.
                new SimpleField("tenantId",   SearchFieldDataType.String)             { IsFilterable = true, IsFacetable = false },
                new SimpleField("documentId", SearchFieldDataType.String)             { IsFilterable = true },
                new SearchableField("title")                                          { IsFilterable = false, IsSortable = false },
                new SearchableField("content"),
                new SimpleField("createdAt",  SearchFieldDataType.DateTimeOffset)     { IsFilterable = true, IsSortable = true },
                new VectorSearchField("contentVector", _embeddingDimensions, profile)
            },
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration(algorithm) },
                Profiles   = { new VectorSearchProfile(profile, algorithm) }
            }
        };

        await _client.CreateIndexAsync(index, ct);
        _log.LogInformation("Created search index {Index} (dim={Dim})", _indexName, _embeddingDimensions);
    }
}
