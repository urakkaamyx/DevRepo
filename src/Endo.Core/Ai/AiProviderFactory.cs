using System.Text.Json.Nodes;
using Endo.Core.Environment;

namespace Endo.Core.Ai;

/// <summary>
/// Builds the configured <see cref="IAiProvider"/> from environment.json's <c>ai</c> section.
/// Two independent roles, per user request: "Orchestrator" (Endo AI proper — command dispatch,
/// via AiOrchestrator, constrained to registered commands) and "Builder" (free-form generation —
/// e.g. turning a project's BOOTSTRAP.md into architecture docs — the same provider machinery,
/// deliberately never routed through AiOrchestrator's command-only constraint). Each role has its
/// own provider/model under <c>ai.orchestrator.*</c> / <c>ai.builder.*</c>; the orchestrator role
/// also reads the older flat <c>ai.provider</c>/<c>ai.model</c> as a fallback so environments
/// created before the role split keep working unchanged.
///
/// Nothing here is a secret; credentials still never touch environment.json (06-AI-SPEC.md
/// "Security") — they come from each provider's own resolution (env vars / OAuth profile for
/// Anthropic; the already-logged in `claude` CLI session for claude-cli; none needed for a local
/// Ollama server). An unset or unrecognized provider resolves to <see cref="NullAiProvider"/> —
/// honestly "not configured" rather than silently assuming a default the user never chose.
/// </summary>
public static class AiProviderFactory
{
    /// <summary>The Orchestrator role — Endo AI proper, used by AiOrchestrator.</summary>
    public static IAiProvider Create(EnvironmentState state)
    {
        var role = GetSection(state.Ai, "orchestrator");
        var provider = GetString(role, "provider")?.Trim().ToLowerInvariant()
            ?? GetString(state.Ai, "provider")?.Trim().ToLowerInvariant(); // pre-role-split fallback
        var model = GetString(role, "model") ?? GetString(state.Ai, "model");
        var baseUrl = GetString(role, "baseUrl") ?? GetString(state.Ai, "baseUrl");

        return FromProviderName(provider, model, baseUrl);
    }

    /// <summary>The Builder role — free-form generation (e.g. BOOTSTRAP.md -> architecture docs), never routed through AiOrchestrator's command constraint.</summary>
    public static IAiProvider CreateBuilder(EnvironmentState state)
    {
        var role = GetSection(state.Ai, "builder");
        var provider = GetString(role, "provider")?.Trim().ToLowerInvariant();
        var model = GetString(role, "model");
        var baseUrl = GetString(role, "baseUrl");

        return FromProviderName(provider, model, baseUrl);
    }

    private static IAiProvider FromProviderName(string? provider, string? model, string? baseUrl) => provider switch
    {
        "anthropic" => new AnthropicAiProvider(),
        "claude-cli" => new ClaudeCliAiProvider(model: model),
        "ollama" => new OllamaAiProvider(baseUrl ?? "http://127.0.0.1:11434", model ?? "llama3.2"),
        _ => new NullAiProvider(),
    };

    private static JsonObject GetSection(JsonObject ai, string role) =>
        ai.TryGetPropertyValue(role, out var node) && node is JsonObject obj ? obj : new JsonObject();

    private static string? GetString(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() : null;
}
