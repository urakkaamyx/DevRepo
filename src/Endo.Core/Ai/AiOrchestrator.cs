using System.Linq;
using System.Text.Json;
using Endo.Core.Commands;
using Endo.Core.Environment;

namespace Endo.Core.Ai;

public sealed record AiAskResult(bool Success, string Message, CommandResult? CommandResult, string? ChosenCommand, bool NeedsClarification = false);

public sealed record AiConversationTurn(string User, string Response);

public sealed record DiscoveredToolCandidate(string? Name, string? Repository, string? Ref, string? Notes);
public sealed record ToolDiscoveryResult(string Name, bool Success, string Message);
public sealed record ToolDiscoveryReport(bool Success, string Message, List<ToolDiscoveryResult> Results);

/// <summary>Search-only outcome — nothing has been installed yet. See <see cref="AiOrchestrator.FindCandidatesAsync"/>.</summary>
public sealed record ToolCandidateSearchResult(bool Success, string Message, IReadOnlyList<DiscoveredToolCandidate> Candidates);

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

    /// <param name="history">
    /// Prior turns in this session, oldest first. <see cref="IAiProvider"/> is a single-shot
    /// completion contract with no session concept of its own (the claude-cli provider, for one,
    /// deliberately runs with --no-session-persistence) — so continuity across turns only exists if
    /// the caller folds it into the prompt itself. Callers that are genuinely one-shot (the CLI's
    /// "endo ai ask") simply omit it; a persistent surface (the GUI's chat window) is expected to
    /// accumulate and pass its own turn history back in on every call.
    /// </param>
    public async Task<AiAskResult> AskAsync(string naturalLanguageRequest, CommandContext context, IReadOnlyList<AiConversationTurn>? history = null, CancellationToken cancellationToken = default)
    {
        // Read-only peek for the system prompt's "known scopes" hint — deliberately does not
        // assign to context.Environment when environment.json doesn't exist yet, so a command
        // that actually needs it still gets the real "run endo setup first" error rather than
        // silently operating against a synthetic empty state.
        var knownScopeState = context.Environment ?? (context.EnvironmentRepository.Exists() ? context.EnvironmentRepository.Load() : null);
        var catalog = _commandEngine.ListCommands();
        var systemPrompt = BuildSystemPrompt(catalog, knownScopeState);
        var userPrompt = BuildUserPrompt(naturalLanguageRequest, history);

        var response = await _provider.CompleteAsync(new AiCompletionRequest(systemPrompt, userPrompt, ForceJsonOutput: true), cancellationToken);

        if (!response.Available)
        {
            return new AiAskResult(false, response.UnavailableReason ?? "AI provider unavailable.", null, null);
        }

        AiCommandDecision? decision;
        try
        {
            var text = (response.Text ?? string.Empty).Trim();
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            decision = JsonSerializer.Deserialize<AiCommandDecision>(ExtractJsonObject(text), jsonOptions);
        }
        catch (JsonException ex)
        {
            return new AiAskResult(false, $"Provider response was not valid structured output: {ex.Message}", null, null);
        }

        if (decision is null || string.IsNullOrWhiteSpace(decision.Command))
        {
            // Not a failure — the model is legitimately asking for more information, not
            // reporting that something went wrong. Callers should treat this differently from a
            // real error (e.g. not render it as one).
            return new AiAskResult(false, decision?.Clarification ?? "The provider did not choose a command.", null, null, NeedsClarification: true);
        }

        if (!_commandEngine.TryGetCommand(decision.Command, out _))
        {
            // Refuse rather than invent — the provider must choose from the real command catalog.
            return new AiAskResult(false, $"Provider proposed unknown command '{decision.Command}'; refusing to invent it.", null, decision.Command);
        }

        var args = decision.Args ?? new Dictionary<string, string>();
        var result = _commandEngine.Execute(decision.Command, context, args);

        if (decision.Command == "project.new" && result.Success)
        {
            // 04-PROJECT-SPEC.md "GameModding Discovery": "When a new GameModding game is
            // created ... Endo should notify the user of available tools" — unconditional, not
            // something the user has to separately remember to ask for. 06-AI-SPEC.md's
            // "Command Chaining" example is this exact scenario end to end. Deliberately reports
            // candidates rather than auto-installing them — the user asked to choose which ones,
            // and since conversation history is now threaded through every turn, a follow-up like
            // "install SmSdk" has everything (name, repository) it needs to resolve correctly.
            var search = await MaybeChainGameModdingDiscoveryAsync(args, context, cancellationToken);
            if (search is not null)
            {
                var combined = $"{result.Output}\n\nNew game for this environment — searching for modding tools...\n{FormatSearchForChat(search)}";
                return new AiAskResult(true, combined, result, decision.Command);
            }
        }

        return new AiAskResult(result.Success, result.Success ? result.Output : (result.Error ?? "Command failed."), result, decision.Command);
    }

    /// <summary>
    /// Only the *first* project registered under a given GameModding/&lt;game&gt; pair should
    /// trigger discovery — later projects for a game Endo already knows about would otherwise
    /// re-run (and re-charge) a web search for no new information.
    /// </summary>
    private async Task<ToolCandidateSearchResult?> MaybeChainGameModdingDiscoveryAsync(Dictionary<string, string> args, CommandContext context, CancellationToken cancellationToken)
    {
        if (!args.TryGetValue("category", out var category) || !category.Equals("GameModding", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!args.TryGetValue("subCategory", out var subCategory) || string.IsNullOrWhiteSpace(subCategory))
        {
            return null;
        }

        var state = context.Environment ??= context.EnvironmentRepository.Load();
        var prefix = $"{category}/{subCategory}/";
        var projectsForThisGame = state.Projects.Keys.Count(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (projectsForThisGame != 1)
        {
            return null;
        }

        return await FindCandidatesAsync(category, subCategory, cancellationToken);
    }

    private static string FormatSearchForChat(ToolCandidateSearchResult search)
    {
        var wellFormed = search.Candidates.Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Repository)).ToList();
        if (!search.Success || wellFormed.Count == 0)
        {
            return search.Message;
        }

        var lines = wellFormed.Select(c =>
            $"  - {c.Name} — {c.Repository}" + (string.IsNullOrWhiteSpace(c.Notes) ? "" : $" ({c.Notes})"));
        return $"Found {wellFormed.Count} candidate tool(s):\n{string.Join("\n", lines)}\n\n" +
               $"Reply naming which ones to install (e.g. \"install {wellFormed[0].Name}\"), or \"install all\".";
    }

    private static string BuildUserPrompt(string request, IReadOnlyList<AiConversationTurn>? history)
    {
        if (history is null || history.Count == 0)
        {
            return request;
        }

        var transcript = string.Join("\n", history.Select(t => $"User: {t.User}\nAssistant: {t.Response}"));
        return $"Conversation so far:\n{transcript}\n\nNew message: {request}";
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    /// <summary>
    /// Implements 05-TOOL-SYSTEM-SPEC.md "Unknown Games" / "GitHub Discovery" end to end: search,
    /// then validate and register every candidate found. Used by the standalone "endo ai discover"
    /// CLI verb, where there's no separate UI step for picking which candidates to keep. Callers
    /// that want the user to choose first (the chat's auto-chain, the guided project-creation
    /// flow) should call <see cref="FindCandidatesAsync"/> and <see cref="InstallCandidates"/>
    /// separately instead.
    /// </summary>
    public async Task<ToolDiscoveryReport> DiscoverToolsAsync(string category, string subCategory, CommandContext context, CancellationToken cancellationToken = default)
    {
        var search = await FindCandidatesAsync(category, subCategory, cancellationToken);
        if (!search.Success || search.Candidates.Count == 0)
        {
            return new ToolDiscoveryReport(search.Success, search.Message, new List<ToolDiscoveryResult>());
        }

        return InstallCandidates(category, subCategory, search.Candidates, context);
    }

    /// <summary>
    /// Research-only half of discovery: search the web/GitHub for real, currently-maintained
    /// third-party tools for a modding category. Never invents a repository — every candidate must
    /// have come from an actual search hit. Installs nothing; that's <see cref="InstallCandidates"/>.
    /// </summary>
    public async Task<ToolCandidateSearchResult> FindCandidatesAsync(string category, string subCategory, CancellationToken cancellationToken = default)
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
            return new ToolCandidateSearchResult(false, response.UnavailableReason ?? "AI provider unavailable.", []);
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
            return new ToolCandidateSearchResult(false, $"Provider response was not valid structured output: {ex.Message}", []);
        }

        if (candidates is null || candidates.Count == 0)
        {
            return new ToolCandidateSearchResult(true, $"No credible tools found for '{category}/{subCategory}'.", []);
        }

        return new ToolCandidateSearchResult(true, $"Found {candidates.Count} candidate(s) for '{category}/{subCategory}'.", candidates);
    }

    /// <summary>
    /// Validates and registers each given candidate through the exact same <c>tool.install</c>
    /// command a human would use (source-first clone, Scratchpad, README check, build/validate,
    /// register only on success) — never registers on the provider's say-so. A candidate missing a
    /// name or repository is reported as skipped rather than attempted.
    /// </summary>
    public ToolDiscoveryReport InstallCandidates(string category, string subCategory, IReadOnlyList<DiscoveredToolCandidate> candidates, CommandContext context)
    {
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

    private static string BuildSystemPrompt(IReadOnlyList<CommandDescriptor> catalog, EnvironmentState? state)
    {
        var lines = catalog.Select(c =>
            $"- {c.Name}({string.Join(", ", c.Parameters)}): {c.Description}");

        // Existing Category/SubCategory pairs already on record — reusing one is what keeps a
        // project and the tools scoped to it under the same taxonomy instead of each AI call
        // inventing its own spelling (e.g. "Games/Scrap Mechanic" for the tool vs.
        // "Games/Mod" for the project it belongs to). Null before 'endo setup' has run.
        var knownScopes = state is null
            ? new List<string>()
            : state.Projects.Keys
                .Select(k => string.Join('/', k.Split('/').Take(2)))
                .Concat(state.Tools.Scoped.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        var scopeGuidance = knownScopes.Count > 0
            ? "\nCategory/SubCategory pairs already in use in this environment — reuse one of these exactly " +
              "(same spelling) whenever the request clearly refers to the same game/project family, rather than " +
              $"inventing a new spelling for something that already exists: {string.Join(", ", knownScopes)}.\n"
            : "\n";

        return "You are Endo AI. You may only invoke commands from this exact list — never invent a command name, " +
               "and put args under exactly the parameter names shown in parentheses (case-sensitive, e.g. \"category\" not \"Category\") — never rename or invent argument keys:\n" +
               string.Join("\n", lines) +
               "\n\nProject hierarchy rule (04-PROJECT-SPEC.md): a project for modding a game always uses " +
               "category=\"GameModding\" and subCategory=<the game's name> — GameModding is the category, never " +
               "\"Games\", \"Mods\", or the literal word \"Mod\". Example: a mod for Scrap Mechanic is " +
               "category=\"GameModding\", subCategory=\"ScrapMechanic\", name=<the mod's own name> — never " +
               "category=\"Games\" with subCategory=\"Mod\"." +
               scopeGuidance +
               "\nRespond with JSON: {\"command\": \"<name-or-null>\", \"args\": {...}, \"clarification\": \"<if you cannot proceed>\"}.";
    }

    private sealed class AiCommandDecision
    {
        public string? Command { get; set; }
        public Dictionary<string, string>? Args { get; set; }
        public string? Clarification { get; set; }
    }
}
