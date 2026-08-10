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
            var parameters = new MessageCreateParams
            {
                Model = Model,
                MaxTokens = request.MaxTokens,
                System = request.SystemPrompt,
                Messages = [new() { Role = Role.User, Content = request.UserPrompt }],
                Tools = request.EnableWebSearch ? [new ToolUnion(new WebSearchTool20260209())] : null,
            };

            response = await _client.Messages.Create(parameters, cancellationToken);
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

        // Concatenate every text block rather than just the first — a web-search-enabled response
        // typically interleaves server_tool_use/web_search_tool_result blocks with several text
        // blocks (Claude narrating between searches), and the final answer is often not block 0.
        var textBlocks = response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text).ToList();
        var text = string.Join("\n", textBlocks);

        if (string.IsNullOrWhiteSpace(text))
        {
            // stop_reason "pause_turn" means the server-side tool loop (default 10 iterations) hit
            // its cap before finishing. Resuming requires replaying the full response content back
            // as the next turn's assistant message; not implemented here (see AnthropicAiProvider
            // remarks) — so a pause with no text yet is reported honestly rather than silently
            // truncated or retried indefinitely.
            return new AiCompletionResponse(false, null, $"Claude returned no usable text content (stop_reason: {response.StopReason}).");
        }

        return new AiCompletionResponse(true, text, null);
    }
}
