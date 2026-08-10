namespace Endo.Core.Ai;

/// <summary>
/// Default provider when none is configured. Always honestly reports unavailability rather than
/// fabricating a response — 06-AI-SPEC.md "No Invented State": the AI must never claim success
/// without evidence, and an unconfigured provider is not evidence of anything.
/// </summary>
public sealed class NullAiProvider : IAiProvider
{
    public string Name => "none";

    public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiCompletionResponse(
            Available: false,
            Text: null,
            UnavailableReason: "No AI provider is configured. Run 'endo setup' and choose a provider, or invoke Endo commands directly."));
    }
}
