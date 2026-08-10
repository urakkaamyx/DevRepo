using Endo.Core.Environment;
using Endo.Core.Diagnostics;

namespace Endo.Core.Commands;

/// <summary>
/// Everything a command needs to run. Carries the loaded environment state so a single
/// CommandEngine.Execute call can both read and persist state without each command re-locating
/// the root or re-loading environment.json.
/// </summary>
public sealed class CommandContext
{
    public required string Root { get; init; }
    public required EnvironmentRepository EnvironmentRepository { get; init; }
    public required Logger Logger { get; init; }

    /// <summary>Loaded lazily by commands that need it; null when environment.json does not exist yet (e.g. during setup).</summary>
    public EnvironmentState? Environment { get; set; }
}

/// <summary>
/// One named, deterministic Endo operation. Per 01-ARCHITECTURE.md: "If AI can perform an
/// operation, a deterministic Endo command must exist for it." Every CLI entry point and every
/// AI-orchestrated operation must go through a registered ICommand — neither is allowed to
/// perform state-changing work outside the command engine.
/// </summary>
public interface ICommand
{
    /// <summary>Stable dotted name, e.g. "project.new", "tool.install". This is what the AI layer is allowed to invoke by.</summary>
    string Name { get; }

    /// <summary>Human/AI-readable description of what this command does, used for CLI knowledge (06-AI-SPEC.md).</summary>
    string Description { get; }

    CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args);
}
