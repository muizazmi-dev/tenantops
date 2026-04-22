using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;

namespace TenantOps.AiOrchestrator.Search;

public sealed record DocumentToIndex(Guid DocumentId, Guid TenantId, string Title, string Content, DateTimeOffset CreatedAt);

/// <summary>
/// Chunks documents and writes them (with tenantId stamped on EVERY chunk)
/// into the shared vector index. Re-indexing the same DocumentId replaces
/// its chunks deterministically so we don't accumulate orphans.
/// </summary>
public sealed class DocumentIndexer
{
    private const int ChunkSize = 1000;      // characters; fine for demo, swap for tokenizer in prod
    private const int ChunkOverlap = 150;

    private readonly SearchClient _docs;
    private readonly ITextEmbeddingGenerationService _embeddings;
    private readonly ILogger<DocumentIndexer> _log;

    public DocumentIndexer(IConfiguration cfg,
                           ITextEmbeddingGenerationService embeddings,
                           ILogger<DocumentIndexer> log)
    {
        var endpoint = cfg["AzureSearch:Endpoint"]!;
        var adminKey = cfg["AzureSearch:AdminKey"]!;
        var index = cfg["AzureSearch:IndexName"] ?? "tenantops-kb";
        _docs = new SearchClient(new Uri(endpoint), index, new AzureKeyCredential(adminKey));
        _embeddings = embeddings;
        _log = log;
    }

    public async Task IndexAsync(DocumentToIndex doc, CancellationToken ct = default)
    {
        if (doc.TenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId is empty — refusing to index.");

        // 1) Delete any existing chunks for this document (idempotent re-index)
        await DeleteChunksForDocumentAsync(doc.DocumentId, ct);

        // 2) Chunk
        var chunks = ChunkText(doc.Content, ChunkSize, ChunkOverlap).ToList();
        if (chunks.Count == 0) return;

        // 3) Embed
        var embeddings = await _embeddings.GenerateEmbeddingsAsync(chunks, cancellationToken: ct);

        // 4) Upload
        var batch = IndexDocumentsBatch.Upload(chunks.Select((text, i) => new SearchDocument
        {
            ["chunkId"]       = $"{doc.DocumentId}:{i:D4}",
            ["tenantId"]      = doc.TenantId.ToString(),          // <-- isolation key
            ["documentId"]    = doc.DocumentId.ToString(),
            ["title"]         = doc.Title,
            ["content"]       = text,
            ["createdAt"]     = doc.CreatedAt,
            ["contentVector"] = embeddings[i].ToArray()
        }));

        await _docs.IndexDocumentsAsync(batch, cancellationToken: ct);
        _log.LogInformation("Indexed {Chunks} chunks for doc {Doc} (tenant {Tenant})",
            chunks.Count, doc.DocumentId, doc.TenantId);
    }

    public async Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var filter = $"documentId eq '{documentId}'";
        var ids = new List<string>();
        var results = await _docs.SearchAsync<SearchDocument>("*",
            new SearchOptions { Filter = filter, Size = 1000, Select = { "chunkId" } }, ct);
        await foreach (var r in results.Value.GetResultsAsync())
            if (r.Document.TryGetValue("chunkId", out var v) && v is string s) ids.Add(s);

        if (ids.Count > 0)
            await _docs.DeleteDocumentsAsync("chunkId", ids, cancellationToken: ct);
    }

    private static IEnumerable<string> ChunkText(string content, int size, int overlap)
    {
        if (string.IsNullOrWhiteSpace(content)) yield break;
        var i = 0;
        while (i < content.Length)
        {
            var len = Math.Min(size, content.Length - i);
            yield return content.Substring(i, len);
            if (i + len >= content.Length) yield break;
            i += size - overlap;
        }
    }
}
