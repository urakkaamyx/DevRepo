using System.Linq;
using Anthropic;
using Anthropic.Models.Messages;

namespace Endo.Core.Ai;

/// <summary>
/// Claude/Anthropic implementation of the provider-neutral interface — one of the providers
/// 06-AI-SPEC.md names explicitly ("Local model, OpenAI, Claude, Future providers").
///
/// Credentials are never read from or written to environment.json, per 06-AI-SPEC.md
/// "Security": secrets must not end up in AI prompts, logs, commit messages, or anything DevRepo
/// checkpoints commit. The zero-arg <see cref="AnthropicClient"/> resolves credentials itself,
/// in order: ANTHROPIC_API_KEY, then ANTHROPIC_AUTH_TOKEN, then the active `ant auth login`
/// OAuth profile, then Workload Identity Federation — so a user authenticated via the `ant` CLI
/// needs no API key at all. Rather than pre-checking which of those is present (fragile, and the
/// `ant` CLI itself warns not to script against its status output), this provider just attempts
/// the call and reports honestly if authentication fails.
/// </summary>
public sealed class AnthropicAiProvider : IAiProvider
{
    private const string Model = "claude-opus-5";

    private readonly AnthropicClient _client = new();

    public string Name => "anthropic";

    public async Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        Message response;
        try
        {
            response = await _client.Messages.Create(
                new MessageCreateParams
                {
                    Model = Model,
                    MaxTokens = 2048,
                    System = request.SystemPrompt,
                    Messages = [new() { Role = Role.User, Content = request.UserPrompt }],
                },
                cancellationToken);
        }
        catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
        {
            return new AiCompletionResponse(
                false, null,
                "Not authenticated with Anthropic. Run 'ant auth login', or set ANTHROPIC_API_KEY.");
        }
        catch (Exception ex)
        {
            return new AiCompletionResponse(false, null, $"Anthropic API request failed: {ex.Message}");
        }

        if (response.StopReason == "refusal")
        {
            return new AiCompletionResponse(false, null, "Claude declined this request (safety refusal).");
        }

        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AiCompletionResponse(false, null, $"Claude returned no text content (stop_reason: {response.StopReason}).");
        }

        return new AiCompletionResponse(true, text, null);
    }
}
