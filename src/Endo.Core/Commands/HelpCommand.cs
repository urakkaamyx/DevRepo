namespace Endo.Core.Commands;

/// <summary>
/// Lists every registered command's name, parameters, and description — the exact same catalog
/// <see cref="Ai.AiOrchestrator"/> sends the AI provider, surfaced for humans too. Deliberately
/// reads live off <see cref="CommandEngine.ListCommands"/> rather than a hand-maintained usage
/// string, so this can never drift out of sync with what commands actually exist or what their
/// real parameter names are — each command's own <c>Description</c> is its reference documentation.
/// </summary>
public sealed class HelpCommand : ICommand
{
    private readonly CommandEngine _commandEngine;

    public HelpCommand(CommandEngine commandEngine)
    {
        _commandEngine = commandEngine;
    }

    public string Name => "help";
    public string Description => "List every registered Endo command with its parameters and description.";
    public IReadOnlyList<string> Parameters => [];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var lines = _commandEngine.ListCommands()
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => $"{c.Name}({string.Join(", ", c.Parameters)})\n    {c.Description}");

        return CommandResult.Ok(string.Join("\n\n", lines));
    }
}
