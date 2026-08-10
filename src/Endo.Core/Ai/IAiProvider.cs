namespace Endo.Core.Ai;

/// <summary>
/// <paramref name="EnableWebSearch"/> is a hint, not a contract every provider must honor —
/// providers without web search (or a future local model) simply ignore it and answer from what
/// they already know. <paramref name="MaxTokens"/> lets callers with longer expected output (e.g.
/// research-style requests) ask for more room than the default.
/// </summary>
public sealed record AiCompletionRequest(string SystemPrompt, string UserPrompt, bool EnableWebSearch = false, int MaxTokens = 2048);
public sealed record AiCompletionResponse(bool Available, string? Text, string? UnavailableReason);

/// <summary>
/// Provider-neutral interface Endo AI talks to (06-AI-SPEC.md "Provider Architecture"). Cloud
/// providers may be used initially, but nothing in Endo.Core may depend on a specific provider —
/// only this interface. A future local-model provider implements the same contract.
/// </summary>
public interface IAiProvider
{
    string Name { get; }
    Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default);
}
