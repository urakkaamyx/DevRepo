using Endo.Core.Commands;

namespace Endo.Core.Restore;

/// <summary>endo setup --restore all | projects</summary>
public sealed class RestoreCommand : ICommand
{
    private readonly RestoreService _restoreService;

    public RestoreCommand(RestoreService restoreService) => _restoreService = restoreService;

    public string Name => "restore";
    public string Description => "Reconcile environment.json against the actual machine. scope=all|projects|tools|runtimes.";

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        args.TryGetValue("scope", out var scope);
        scope ??= "all";

        var report = scope.ToLowerInvariant() switch
        {
            "all" => _restoreService.RestoreAll(state),
            "projects" => _restoreService.RestoreProjects(state),
            "tools" => _restoreService.RestoreTools(state),
            "runtimes" => _restoreService.RestoreRuntimes(state),
            _ => null,
        };

        if (report is null)
        {
            return CommandResult.Fail($"Unknown restore scope '{scope}'. Valid scopes: all, projects, tools, runtimes.");
        }

        var diagnostics = new List<string>();
        diagnostics.AddRange(report.Restored.Select(x => $"Restored: {x}"));
        diagnostics.AddRange(report.AlreadyPresent.Select(x => $"Already present: {x}"));
        diagnostics.AddRange(report.Repaired.Select(x => $"Repaired: {x}"));
        diagnostics.AddRange(report.Changed.Select(x => $"Changed: {x}"));
        diagnostics.AddRange(report.Missing.Select(x => $"Missing: {x}"));
        diagnostics.AddRange(report.Unresolved.Select(x => $"Unresolved: {x}"));
        diagnostics.AddRange(report.ExistingButUnmanaged.Select(x => $"Existing but unmanaged: {x}"));
        diagnostics.AddRange(report.Warnings.Select(x => $"Warning: {x}"));

        // Never report "restore successful" when required components remain unresolved (10-RESTORE-MIGRATION-SPEC.md).
        return report.FullySuccessful
            ? CommandResult.Ok(report.Summarize(), diagnostics: diagnostics)
            : CommandResult.Fail($"Restore completed with unresolved items. {report.Summarize()}", diagnostics: diagnostics);
    }
}
