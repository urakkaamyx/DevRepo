namespace Endo.Core.Commands;

public sealed record CommandDescriptor(string Name, string Description, IReadOnlyList<string> Parameters);

/// <summary>
/// The single dispatch point for every deterministic Endo operation. Both the CLI (Endo.Cli) and
/// the AI orchestrator (Endo.Core.Ai) call through here — neither is allowed a second, hidden
/// implementation of Endo's behavior (01-ARCHITECTURE.md).
/// </summary>
public sealed class CommandEngine
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);

    public void Register(ICommand command)
    {
        if (!_commands.TryAdd(command.Name, command))
        {
            throw new InvalidOperationException($"A command named '{command.Name}' is already registered.");
        }
    }

    /// <summary>Exposed so the AI layer can know the actual command set instead of inventing commands (06-AI-SPEC.md "CLI Knowledge").</summary>
    public IReadOnlyList<CommandDescriptor> ListCommands() =>
        _commands.Values
            .Select(c => new CommandDescriptor(c.Name, c.Description, c.Parameters))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

    public bool TryGetCommand(string name, out ICommand? command) => _commands.TryGetValue(name, out command);

    public CommandResult Execute(string name, CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        if (!_commands.TryGetValue(name, out var command))
        {
            return CommandResult.Fail(
                $"Unknown command '{name}'.",
                recoveryInformation: "Use the list of registered commands (CommandEngine.ListCommands) rather than inventing a command name.");
        }

        try
        {
            return command.Execute(context, args);
        }
        catch (Exception ex)
        {
            context.Logger.Error($"Command '{name}' threw an unhandled exception.", new { error = ex.ToString() });
            return CommandResult.Fail(
                $"Command '{name}' failed: {ex.Message}",
                diagnostics: new[] { ex.GetType().FullName ?? ex.GetType().Name });
        }
    }
}
