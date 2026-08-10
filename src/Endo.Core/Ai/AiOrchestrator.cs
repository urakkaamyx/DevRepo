using System.Text.Json;
using Endo.Core.Commands;

namespace Endo.Core.Ai;

public sealed record AiAskResult(bool Success, string Message, CommandResult? CommandResult, string? ChosenCommand);

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

        var response = await _provider.CompleteAsync(new AiCompletionRequest(systemPrompt, naturalLanguageRequest), cancellationToken);

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

    private static string BuildSystemPrompt(IReadOnlyList<CommandDescriptor> catalog)
    {
        var lines = catalog.Select(c => $"- {c.Name}: {c.Description}");
        return "You are Endo AI. You may only invoke commands from this exact list — never invent a command name:\n" +
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
