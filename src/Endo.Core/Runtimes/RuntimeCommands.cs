using Endo.Core.Commands;

namespace Endo.Core.Runtimes;

public sealed class RuntimeListCommand : ICommand
{
    private readonly RuntimeService _runtimeService;
    public RuntimeListCommand(RuntimeService runtimeService) => _runtimeService = runtimeService;

    public string Name => "runtime.list";
    public string Description => "List registered runtimes and their installed versions.";
    public IReadOnlyList<string> Parameters => [];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();
        var lines = _runtimeService.List(state)
            .Select(r => $"{r.Name}: versions=[{string.Join(", ", r.Versions.Keys)}] latestInstalled={r.LatestInstalled ?? "(none)"}")
            .ToList();

        return CommandResult.Ok(lines.Count == 0 ? "No runtimes registered." : string.Join("\n", lines));
    }
}

public sealed class RuntimeInstallCommand : ICommand
{
    private readonly RuntimeService _runtimeService;
    public RuntimeInstallCommand(RuntimeService runtimeService) => _runtimeService = runtimeService;

    public string Name => "runtime.install";
    public string Description => "Register an already-present runtime installation at a given path (see RuntimeService remarks on scope).";
    public IReadOnlyList<string> Parameters => ["name", "version", "path", "notes"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (!args.TryGetValue("name", out var name) || !args.TryGetValue("version", out var version) || !args.TryGetValue("path", out var path))
        {
            return CommandResult.Fail("runtime.install requires 'name', 'version', and 'path' arguments.");
        }

        if (!Directory.Exists(path))
        {
            return CommandResult.Fail($"Path '{path}' does not exist.");
        }

        args.TryGetValue("notes", out var notes);
        _runtimeService.Register(state, name, version, path, notes);

        context.EnvironmentRepository.AppendHistory(state, "runtime.install", $"Registered {name} {version} at '{path}'.");
        context.EnvironmentRepository.Save(state);

        return CommandResult.Ok($"Registered runtime {name} {version} at '{path}'.", affectedState: new[] { $"runtimes.{name}" });
    }
}

public sealed class RuntimeSetCommand : ICommand
{
    private readonly RuntimeService _runtimeService;
    public RuntimeSetCommand(RuntimeService runtimeService) => _runtimeService = runtimeService;

    public string Name => "runtime.set";
    public string Description => "Select an installed runtime version for a specific project. Availability and selection are separate.";
    public IReadOnlyList<string> Parameters => ["project", "runtime", "version"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (!args.TryGetValue("project", out var projectKey) || !args.TryGetValue("runtime", out var runtimeName))
        {
            return CommandResult.Fail("runtime.set requires 'project' and 'runtime' arguments.");
        }

        args.TryGetValue("version", out var version);

        var (success, message) = _runtimeService.SelectForProject(state, projectKey, runtimeName, version);

        return success ? CommandResult.Ok(message, affectedState: new[] { $"projects.{projectKey}.runtime" }) : CommandResult.Fail(message);
    }
}
