namespace Endo.Core.Ai;

/// <summary>
/// <paramref name="EnableWebSearch"/> is a hint, not a contract every provider must honor —
/// providers without web search (or a future local model) simply ignore it and answer from what
/// they already know. <paramref name="MaxTokens"/> lets callers with longer expected output (e.g.
/// research-style requests) ask for more room than the default. <paramref name="ForceJsonOutput"/>
/// is another hint: providers that can constrain decoding to syntactically valid JSON (e.g.
/// Ollama's <c>format: "json"</c>) should do so — smaller local models otherwise answer a
/// JSON-only instruction in plain prose fairly often, which AiOrchestrator can only detect after
/// the fact by failing to parse it.
/// </summary>
public sealed record AiCompletionRequest(string SystemPrompt, string UserPrompt, bool EnableWebSearch = false, int MaxTokens = 2048, bool ForceJsonOutput = false);
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
