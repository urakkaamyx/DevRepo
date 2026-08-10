using Endo.Core.Commands;

namespace Endo.Core.Setup;

/// <summary>
/// Deterministic, non-interactive form of `endo setup`, for AI orchestration or scripted use.
/// Interactive first-run setup (SetupService.RunInteractive) gathers these same answers via
/// prompts and calls the identical Apply() core, so both paths behave identically.
/// </summary>
public sealed class SetupCommand : ICommand
{
    private readonly SetupService _setupService;

    public SetupCommand(SetupService setupService) => _setupService = setupService;

    public string Name => "setup";
    public string Description => "Establish or update the Endo managed root, workspace location, DevRepo, AI provider, and update preferences.";

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("root", out var root) || !args.TryGetValue("workspace", out var workspace))
        {
            return CommandResult.Fail("setup requires 'root' and 'workspace' arguments when invoked non-interactively.");
        }

        args.TryGetValue("aiProvider", out var aiProvider);
        var initDevRepo = args.TryGetValue("initDevRepo", out var initDevRepoRaw) && bool.TryParse(initDevRepoRaw, out var parsedInit) && parsedInit;
        var autoCheckUpdates = !args.TryGetValue("autoCheckUpdates", out var autoCheckRaw) || !bool.TryParse(autoCheckRaw, out var parsedAuto) || parsedAuto;

        var result = _setupService.Apply(new SetupAnswers(root, workspace, initDevRepo, aiProvider, autoCheckUpdates));

        return result.Success
            ? CommandResult.Ok(result.Message, changedFiles: result.ChangedFiles, diagnostics: result.Diagnostics)
            : CommandResult.Fail(result.Message, diagnostics: result.Diagnostics);
    }
}
