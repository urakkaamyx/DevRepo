using Endo.Core.Commands;

namespace Endo.Core.Ai;

/// <summary>endo ai login claude-cli — launches the interactive 'claude auth login' OAuth flow. No-op if already logged in.</summary>
public sealed class ClaudeCliLoginCommand : ICommand
{
    private readonly IClaudeCliInstaller _installer;

    public ClaudeCliLoginCommand(IClaudeCliInstaller installer)
    {
        _installer = installer;
    }

    public string Name => "claudeCli.login";
    public string Description => "Launch 'claude auth login' interactively (opens a browser-based OAuth flow) and report the resulting auth status.";
    public IReadOnlyList<string> Parameters => [];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var status = _installer.GetStatus();
        if (!status.Installed)
        {
            return CommandResult.Fail("claude CLI is not installed. Run 'endo ai install claude-cli' first.");
        }

        if (status.LoggedIn == true)
        {
            return CommandResult.Ok($"Already logged in as {status.Email}.");
        }

        var result = _installer.Login();
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
