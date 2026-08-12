using Endo.Core.Commands;

namespace Endo.Core.Ai;

/// <summary>endo ai status claude-cli — read-only report of whether the claude CLI is installed/logged in.</summary>
public sealed class ClaudeCliStatusCommand : ICommand
{
    private readonly IClaudeCliInstaller _installer;

    public ClaudeCliStatusCommand(IClaudeCliInstaller installer)
    {
        _installer = installer;
    }

    public string Name => "claudeCli.status";
    public string Description => "Report whether the claude CLI (Claude Code) is installed on PATH and whether it currently has a logged-in session.";
    public IReadOnlyList<string> Parameters => [];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var status = _installer.GetStatus();
        if (!status.Installed)
        {
            return CommandResult.Ok("claude CLI is not installed.");
        }

        return CommandResult.Ok(status.LoggedIn == true
            ? $"claude CLI is installed and logged in as {status.Email}."
            : "claude CLI is installed but not logged in.");
    }
}
