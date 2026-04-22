using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using TenantOps.AiOrchestrator.Search;

namespace TenantOps.AiOrchestrator.Rag;

public sealed record Citation(string DocumentId, string Title, string Snippet);
public sealed record ChatResult(string Answer, IReadOnlyList<Citation> Citations);

/// <summary>
/// Orchestrates the RAG pipeline with Semantic Kernel:
///   1) Embed the user query.
///   2) Retrieve top-K chunks from AI Search with a mandatory tenant filter.
///   3) Compose a grounded prompt that REQUIRES the model to cite sources.
///   4) Call Azure OpenAI chat completion.
///   5) Return the answer plus structured citations.
///
/// Why Semantic Kernel (vs. calling Azure.AI.OpenAI directly)?
///   * Unified kernel DI surface for connectors + plugins.
///   * Pluggable ChatCompletion / Embeddings services (swap models in config).
///   * Future extension points for plugins (e.g. a TicketsPlugin that lets
///     the agent open a ticket from inside a chat — out of scope for the demo
///     but wired-in friendly).
///
/// Docs:
///   Semantic Kernel:  https://learn.microsoft.com/semantic-kernel/
///   Azure OpenAI chat: https://learn.microsoft.com/azure/ai-services/openai/chatgpt-quickstart
/// </summary>
public sealed class RagOrchestrator
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ITextEmbeddingGenerationService _embeddings;
    private readonly SearchRetriever _retriever;

    public RagOrchestrator(Kernel kernel,
                           IChatCompletionService chat,
                           ITextEmbeddingGenerationService embeddings,
                           SearchRetriever retriever)
    {
        _kernel = kernel;
        _chat = chat;
        _embeddings = embeddings;
        _retriever = retriever;
    }

    public async Task<ChatResult> AskAsync(Guid tenantId, string userMessage, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new InvalidOperationException("TenantId required.");
        if (string.IsNullOrWhiteSpace(userMessage)) throw new ArgumentException("Empty message.", nameof(userMessage));

        // 1) Embed the question
        var qEmbedding = (await _embeddings.GenerateEmbeddingsAsync(new[] { userMessage }, cancellationToken: ct))[0];

        // 2) Retrieve (mandatory tenant filter applied inside SearchRetriever)
        var chunks = await _retriever.SearchAsync(tenantId, userMessage, qEmbedding, topK: 5, ct);

        // 3) Build grounded prompt
        var contextBlock = string.Join("\n\n", chunks.Select((c, i) =>
            $"[{i + 1}] ({c.Title})\n{c.Content}"));

        var systemPrompt = """
            You are TenantOps assistant. Answer the user's question using ONLY
            the provided context. If the context does not contain the answer,
            say "I don't have that information in the tenant's knowledge base."
            Cite sources inline using bracketed indices like [1], [2] matching
            the numbered context items. Keep answers concise.
            """;

        var history = new ChatHistory(systemPrompt);
        history.AddUserMessage($"""
            Context:
            {(chunks.Count == 0 ? "(no documents found for this tenant)" : contextBlock)}

            Question: {userMessage}
            """);

        // 4) Chat completion
        var response = await _chat.GetChatMessageContentAsync(history, kernel: _kernel, cancellationToken: ct);
        var answer = response.Content ?? "(no answer)";

        // 5) Build structured citations
        var citations = chunks.Select(c => new Citation(
            DocumentId: c.DocumentId,
            Title: c.Title,
            Snippet: c.Content.Length > 180 ? c.Content[..180] + "…" : c.Content
        )).ToList();

        return new ChatResult(answer, citations);
    }
}
