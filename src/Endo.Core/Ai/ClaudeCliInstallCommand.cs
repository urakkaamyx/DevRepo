using Endo.Core.Commands;

namespace Endo.Core.Ai;

/// <summary>endo ai install claude-cli — installs the claude CLI via npm if it isn't already present. No-op if already installed.</summary>
public sealed class ClaudeCliInstallCommand : ICommand
{
    private readonly IClaudeCliInstaller _installer;

    public ClaudeCliInstallCommand(IClaudeCliInstaller installer)
    {
        _installer = installer;
    }

    public string Name => "claudeCli.install";
    public string Description => "Install the claude CLI (Claude Code) via 'npm install -g @anthropic-ai/claude-code' if it isn't already on PATH.";
    public IReadOnlyList<string> Parameters => [];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var status = _installer.GetStatus();
        if (status.Installed)
        {
            return CommandResult.Ok("claude CLI is already installed.");
        }

        var result = _installer.InstallViaNpm();
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
