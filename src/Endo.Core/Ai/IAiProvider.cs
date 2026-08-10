namespace Endo.Core.Ai;

public sealed record AiCompletionRequest(string SystemPrompt, string UserPrompt);
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
