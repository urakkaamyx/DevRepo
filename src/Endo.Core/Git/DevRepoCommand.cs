using Endo.Core.Commands;

namespace Endo.Core.Git;

/// <summary>
/// endo devrepo checkpoint — locates PUSH.md, reviews actual DevRepo changes, generates the
/// Recommended Push message, and commits, per 07-GIT-DEVREPO-SPEC.md.
/// </summary>
public sealed class DevRepoCheckpointCommand : ICommand
{
    private readonly Func<CommandContext, DevRepoService> _serviceFactory;

    public DevRepoCheckpointCommand(Func<CommandContext, DevRepoService> serviceFactory) => _serviceFactory = serviceFactory;

    public string Name => "devrepo.checkpoint";
    public string Description => "Locate PUSH.md, review actual DevRepo changes, generate the Recommended Push message, and commit.";
    public IReadOnlyList<string> Parameters => ["message"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var service = _serviceFactory(context);

        var pushMd = service.FindPushMd();
        var diagnostics = new List<string>();
        diagnostics.Add(pushMd.Path is null
            ? "No PUSH.md found; proceeding with a message generated from actual changed state only."
            : $"PUSH.md located at '{pushMd.Path}'.");

        args.TryGetValue("message", out var overrideMessage);
        var result = service.Checkpoint(string.IsNullOrWhiteSpace(overrideMessage) ? null : overrideMessage);

        if (!result.Success)
        {
            return CommandResult.Fail(result.Message, diagnostics: diagnostics);
        }

        return CommandResult.Ok(
            result.Message,
            affectedState: new[] { "devrepo" },
            changedFiles: result.ChangedFiles,
            diagnostics: diagnostics);
    }
}
