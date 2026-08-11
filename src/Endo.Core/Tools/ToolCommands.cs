using Endo.Core.Commands;

namespace Endo.Core.Tools;

public sealed class ToolListCommand : ICommand
{
    private readonly ToolService _toolService;
    public ToolListCommand(ToolService toolService) => _toolService = toolService;

    public string Name => "tool.list";
    public string Description => "List all registered tools (general and scoped) with their installed versions and active channels.";
    public IReadOnlyList<string> Parameters => [];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();
        var lines = _toolService.List(state)
            .Select(t => $"{t.ScopeKey}/{t.Manifest.Identity.Name}: versions=[{string.Join(", ", t.Manifest.Versions.Keys)}] channels=[{string.Join(", ", t.Manifest.Channels.Select(c => $"{c.Key}->{c.Value}"))}]")
            .ToList();

        return CommandResult.Ok(lines.Count == 0 ? "No tools registered." : string.Join("\n", lines));
    }
}

public sealed class ToolInfoCommand : ICommand
{
    private readonly ToolService _toolService;
    public ToolInfoCommand(ToolService toolService) => _toolService = toolService;

    public string Name => "tool.info";
    public string Description => "Show the full manifest for one tool.";
    public IReadOnlyList<string> Parameters => ["name", "scopeCategory", "scopeSubCategory"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();
        if (!args.TryGetValue("name", out var name))
        {
            return CommandResult.Fail("tool.info requires a 'name' argument.");
        }

        args.TryGetValue("scopeCategory", out var scopeCategory);
        args.TryGetValue("scopeSubCategory", out var scopeSubCategory);

        var manifest = _toolService.Find(state, name, scopeCategory, scopeSubCategory);
        if (manifest is null)
        {
            return CommandResult.Fail($"Tool '{name}' is not registered.");
        }

        var output = $"{manifest.Identity.Name} [{manifest.Scope.Key}]\n" +
                     $"  source: {manifest.Source.Repository} ({manifest.Source.Type}, ref={manifest.Source.Ref})\n" +
                     $"  validation: {manifest.Validation.Status}\n" +
                     $"  channels: {string.Join(", ", manifest.Channels.Select(c => $"{c.Key}->{c.Value}"))}\n" +
                     $"  versions: {string.Join(", ", manifest.Versions.Keys)}";

        return CommandResult.Ok(output);
    }
}

public sealed class ToolInstallCommand : ICommand
{
    private readonly ToolService _toolService;
    public ToolInstallCommand(ToolService toolService) => _toolService = toolService;

    public string Name => "tool.install";
    public string Description => "Acquire a tool (git clone, source-first, or a release archive as fallback) into the Scratchpad, build/validate it, and register it only if validation passes.";
    public IReadOnlyList<string> Parameters => ["name", "repository", "releaseUrl", "ref", "version", "scopeCategory", "scopeSubCategory", "buildCommand", "validateCommand"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        args.TryGetValue("repository", out var repository);
        args.TryGetValue("releaseUrl", out var releaseUrl);

        if (!args.TryGetValue("name", out var name) ||
            (string.IsNullOrWhiteSpace(repository) && string.IsNullOrWhiteSpace(releaseUrl)))
        {
            return CommandResult.Fail("tool.install requires 'name' and either 'repository' (git) or 'releaseUrl' (archive fallback).");
        }

        args.TryGetValue("ref", out var gitRef);
        args.TryGetValue("version", out var version);
        args.TryGetValue("scopeCategory", out var scopeCategory);
        args.TryGetValue("scopeSubCategory", out var scopeSubCategory);
        args.TryGetValue("buildCommand", out var buildCommand);
        args.TryGetValue("validateCommand", out var validateCommand);

        var request = new ToolInstallRequest
        {
            Name = name,
            Repository = string.IsNullOrWhiteSpace(repository) ? null : repository,
            Ref = string.IsNullOrWhiteSpace(gitRef) ? "main" : gitRef,
            ReleaseUrl = string.IsNullOrWhiteSpace(releaseUrl) ? null : releaseUrl,
            Version = version,
            ScopeCategory = string.IsNullOrWhiteSpace(scopeCategory) ? null : scopeCategory,
            ScopeSubCategory = string.IsNullOrWhiteSpace(scopeSubCategory) ? null : scopeSubCategory,
            BuildCommand = buildCommand,
            ValidateCommand = validateCommand,
        };

        var report = _toolService.Install(state, request);

        var diagnostics = new List<string>();
        diagnostics.AddRange(report.DocumentationReviewed.Select(d => $"Documentation reviewed: {d}"));
        diagnostics.AddRange(report.StepsSucceeded.Select(s => $"Succeeded: {s}"));
        diagnostics.AddRange(report.StepsFailed.Select(s => $"Failed: {s}"));
        diagnostics.AddRange(report.Errors.Select(e => $"Error: {e}"));
        diagnostics.AddRange(report.RecoveryAttempts.Select(r => $"Recovery: {r}"));
        diagnostics.Add($"Scratchpad: {report.ScratchpadPath}");

        if (!report.Success)
        {
            return CommandResult.Fail(
                report.FinalReason ?? "Tool installation failed.",
                diagnostics: diagnostics,
                recoveryInformation: $"Scratchpad evidence retained at '{report.ScratchpadPath}' for diagnosis.");
        }

        context.EnvironmentRepository.AppendHistory(state, "tool.install", $"Installed {name} {report.InstalledVersion!.Version}.", new[] { report.InstalledVersion.Path });
        context.EnvironmentRepository.Save(state);

        return CommandResult.Ok(
            $"Installed {name} {report.InstalledVersion!.Version} at '{report.InstalledVersion.Path}'.",
            affectedState: new[] { $"tools.{(request.ScopeCategory is null ? "General" : $"{request.ScopeCategory}/{request.ScopeSubCategory}")}.{name}" },
            changedFiles: new[] { report.InstalledVersion.Path },
            diagnostics: diagnostics);
    }
}

public sealed class ToolRemoveCommand : ICommand
{
    private readonly ToolService _toolService;
    public ToolRemoveCommand(ToolService toolService) => _toolService = toolService;

    public string Name => "tool.remove";
    public string Description => "Remove a tool (or one version of it). Protected by dependent-project checks unless --force is set.";
    public IReadOnlyList<string> Parameters => ["name", "scopeCategory", "scopeSubCategory", "version", "force"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();
        if (!args.TryGetValue("name", out var name))
        {
            return CommandResult.Fail("tool.remove requires a 'name' argument.");
        }

        args.TryGetValue("scopeCategory", out var scopeCategory);
        args.TryGetValue("scopeSubCategory", out var scopeSubCategory);
        args.TryGetValue("version", out var version);
        var force = args.TryGetValue("force", out var forceRaw) && bool.TryParse(forceRaw, out var parsedForce) && parsedForce;

        var (success, message, dependents) = _toolService.Remove(state, name, scopeCategory, scopeSubCategory, version, force);

        if (!success)
        {
            return CommandResult.Fail(message, diagnostics: dependents.Select(d => $"Depends on this tool: {d}").ToList());
        }

        context.EnvironmentRepository.AppendHistory(state, "tool.remove", message);
        context.EnvironmentRepository.Save(state);

        return CommandResult.Ok(message, affectedState: new[] { $"tools.{name}" });
    }
}
