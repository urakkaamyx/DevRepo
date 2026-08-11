using System.Text.Json.Nodes;
using Endo.Core.Environment;

namespace Endo.Core.Ai;

/// <summary>
/// Builds the configured <see cref="IAiProvider"/> from environment.json's <c>ai</c> section —
/// provider name, and provider-specific non-secret config (model, base URL). Nothing here is a
/// secret; credentials still never touch environment.json (06-AI-SPEC.md "Security") — they come
/// from each provider's own resolution (env vars / OAuth profile for Anthropic; the already-logged
/// in `claude` CLI session for claude-cli; none needed for a local Ollama server). An unset or
/// unrecognized provider resolves to <see cref="NullAiProvider"/> — honestly "not configured"
/// rather than silently assuming a default provider the user never chose.
/// </summary>
public static class AiProviderFactory
{
    public static IAiProvider Create(EnvironmentState state)
    {
        var provider = GetString(state.Ai, "provider")?.Trim().ToLowerInvariant();

        return provider switch
        {
            "anthropic" => new AnthropicAiProvider(),
            "claude-cli" => new ClaudeCliAiProvider(model: GetString(state.Ai, "model")),
            "ollama" => new OllamaAiProvider(
                GetString(state.Ai, "baseUrl") ?? "http://127.0.0.1:11434",
                GetString(state.Ai, "model") ?? "llama3.2"),
            _ => new NullAiProvider(),
        };
    }

    private static string? GetString(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() : null;
}
