using System.Linq;
using System.Text.Json;
using Endo.Core.Commands;

namespace Endo.Core.Ai;

public sealed record AiAskResult(bool Success, string Message, CommandResult? CommandResult, string? ChosenCommand);

public sealed record DiscoveredToolCandidate(string? Name, string? Repository, string? Ref, string? Notes);
public sealed record ToolDiscoveryResult(string Name, bool Success, string Message);
public sealed record ToolDiscoveryReport(bool Success, string Message, List<ToolDiscoveryResult> Results);

/// <summary>
/// Translates natural language into Endo commands and executes them through CommandEngine —
/// never a second, hidden implementation of Endo (01-ARCHITECTURE.md). The provider is only ever
/// shown the registered command catalog (name + description), not the full environment, per
/// 06-AI-SPEC.md "Context Sources": "Do not automatically send the entire environment to every
/// model request." Whatever command name the provider proposes is validated against the real
/// registry before anything executes; an unrecognized command name is refused, not improvised.
/// </summary>
public sealed class AiOrchestrator
{
    private readonly IAiProvider _provider;
    private readonly CommandEngine _commandEngine;

    public AiOrchestrator(IAiProvider provider, CommandEngine commandEngine)
    {
        _provider = provider;
        _commandEngine = commandEngine;
    }

    public async Task<AiAskResult> AskAsync(string naturalLanguageRequest, CommandContext context, CancellationToken cancellationToken = default)
    {
        var catalog = _commandEngine.ListCommands();
        var systemPrompt = BuildSystemPrompt(catalog);

        var response = await _provider.CompleteAsync(new AiCompletionRequest(systemPrompt, naturalLanguageRequest, ForceJsonOutput: true), cancellationToken);

        if (!response.Available)
        {
            return new AiAskResult(false, response.UnavailableReason ?? "AI provider unavailable.", null, null);
        }

        AiCommandDecision? decision;
        try
        {
            decision = JsonSerializer.Deserialize<AiCommandDecision>(response.Text ?? string.Empty,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (JsonException ex)
        {
            return new AiAskResult(false, $"Provider response was not valid structured output: {ex.Message}", null, null);
        }

        if (decision is null || string.IsNullOrWhiteSpace(decision.Command))
        {
            return new AiAskResult(false, decision?.Clarification ?? "The provider did not choose a command.", null, null);
        }

        if (!_commandEngine.TryGetCommand(decision.Command, out _))
        {
            // Refuse rather than invent — the provider must choose from the real command catalog.
            return new AiAskResult(false, $"Provider proposed unknown command '{decision.Command}'; refusing to invent it.", null, decision.Command);
        }

        var args = decision.Args ?? new Dictionary<string, string>();
        var result = _commandEngine.Execute(decision.Command, context, args);

        return new AiAskResult(result.Success, result.Success ? result.Output : (result.Error ?? "Command failed."), result, decision.Command);
    }

    /// <summary>
    /// Implements 05-TOOL-SYSTEM-SPEC.md "Unknown Games" / "GitHub Discovery": research the web
    /// and GitHub for real tools, then validate each candidate through the exact same
    /// <c>tool.install</c> command a human would use (source-first clone, Scratchpad, README
    /// check, build/validate, register only on success) — discovery finds candidates, it never
    /// registers anything itself. A candidate the provider names without having actually found a
    /// repository is not "discovery"; the install step is what proves a name isn't invented, since
    /// a fabricated URL simply fails to clone.
    /// </summary>
    public async Task<ToolDiscoveryReport> DiscoverToolsAsync(string category, string subCategory, CommandContext context, CancellationToken cancellationToken = default)
    {
        var systemPrompt =
            "You are Endo AI performing tool discovery for a new modding category, per 05-TOOL-SYSTEM-SPEC.md. " +
            $"Search the web and GitHub for currently-maintained, real, third-party tools commonly used to mod " +
            $"'{subCategory}' (category: {category}). Identify up to 5 candidates. For each, you must have found " +
            "an actual GitHub repository URL via search — never invent one. " +
            "Respond with ONLY a JSON array, no prose, no markdown code fences: " +
            "[{\"name\": \"...\", \"repository\": \"https://github.com/...\", \"ref\": \"main\", \"notes\": \"...\"}]. " +
            "If you find nothing credible, respond with an empty array: [].";

        var userPrompt = $"Find modding tools for: {category} / {subCategory}";

        var response = await _provider.CompleteAsync(
            new AiCompletionRequest(systemPrompt, userPrompt, EnableWebSearch: true, MaxTokens: 4096, ForceJsonOutput: true),
            cancellationToken);

        if (!response.Available)
        {
            return new ToolDiscoveryReport(false, response.UnavailableReason ?? "AI provider unavailable.", new List<ToolDiscoveryResult>());
        }

        List<DiscoveredToolCandidate>? candidates;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var text = (response.Text ?? string.Empty).Trim();

            // Providers asked for "a JSON array" sometimes return a single JSON object instead —
            // observed in practice with smaller local models under constrained ("format: json")
            // decoding, which guarantees valid JSON but not the specific top-level shape asked
            // for. Treat a bare object as a one-candidate result rather than failing outright.
            candidates = text.StartsWith('{')
                ? [JsonSerializer.Deserialize<DiscoveredToolCandidate>(text, jsonOptions)!]
                : JsonSerializer.Deserialize<List<DiscoveredToolCandidate>>(ExtractJsonArray(text), jsonOptions);
        }
        catch (JsonException ex)
        {
            return new ToolDiscoveryReport(false, $"Provider response was not valid structured output: {ex.Message}", new List<ToolDiscoveryResult>());
        }

        if (candidates is null || candidates.Count == 0)
        {
            return new ToolDiscoveryReport(true, $"No credible tools found for '{category}/{subCategory}'.", new List<ToolDiscoveryResult>());
        }

        var results = new List<ToolDiscoveryResult>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Name) || string.IsNullOrWhiteSpace(candidate.Repository))
            {
                results.Add(new ToolDiscoveryResult(candidate.Name ?? "(unnamed)", false, "Candidate missing name or repository; skipped."));
                continue;
            }

            var args = new Dictionary<string, string>
            {
                ["name"] = candidate.Name,
                ["repository"] = candidate.Repository,
                ["scopeCategory"] = category,
                ["scopeSubCategory"] = subCategory,
            };
            if (!string.IsNullOrWhiteSpace(candidate.Ref))
            {
                args["ref"] = candidate.Ref;
            }

            // Goes through CommandEngine like any other invocation — the real validation pipeline
            // (clone, Scratchpad, build/validate, register-only-on-success) is what turns a
            // discovered name into a trustworthy one, not the provider's say-so.
            var installResult = _commandEngine.Execute("tool.install", context, args);
            results.Add(new ToolDiscoveryResult(
                candidate.Name,
                installResult.Success,
                installResult.Success ? installResult.Output : (installResult.Error ?? "Install failed.")));
        }

        var succeeded = results.Count(r => r.Success);
        return new ToolDiscoveryReport(true, $"Discovery complete: {succeeded}/{results.Count} candidate(s) validated and registered.", results);
    }

    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static string BuildSystemPrompt(IReadOnlyList<CommandDescriptor> catalog)
    {
        var lines = catalog.Select(c =>
            $"- {c.Name}({string.Join(", ", c.Parameters)}): {c.Description}");
        return "You are Endo AI. You may only invoke commands from this exact list — never invent a command name, " +
               "and put args under exactly the parameter names shown in parentheses (case-sensitive, e.g. \"category\" not \"Category\") — never rename or invent argument keys:\n" +
               string.Join("\n", lines) +
               "\n\nRespond with JSON: {\"command\": \"<name-or-null>\", \"args\": {...}, \"clarification\": \"<if you cannot proceed>\"}.";
    }

    private sealed class AiCommandDecision
    {
        public string? Command { get; set; }
        public Dictionary<string, string>? Args { get; set; }
        public string? Clarification { get; set; }
    }
}
