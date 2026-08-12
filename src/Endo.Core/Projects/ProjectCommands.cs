using Endo.Core.Commands;

namespace Endo.Core.Projects;

/// <summary>endo project new &lt;Category&gt; &lt;SubCategory&gt; &lt;ProjectName&gt;</summary>
public sealed class ProjectNewCommand : ICommand
{
    private readonly ProjectService _projectService;

    public ProjectNewCommand(ProjectService projectService) => _projectService = projectService;

    public string Name => "project.new";
    public string Description => "Create a new project at Projects/<Category>/<SubCategory>/<ProjectName>, with its own Git repository, .agents/, and project.json. Optional 'ide' sets project.json's IDE preference at creation time.";
    public IReadOnlyList<string> Parameters => ["category", "subCategory", "name", "ide"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (!args.TryGetValue("category", out var category) ||
            !args.TryGetValue("subCategory", out var subCategory) ||
            !args.TryGetValue("name", out var name))
        {
            return CommandResult.Fail("project.new requires 'category', 'subCategory', and 'name' arguments.");
        }

        args.TryGetValue("ide", out var ide);

        var result = _projectService.CreateProject(state, category, subCategory, name, ide);
        if (!result.Success)
        {
            return CommandResult.Fail(result.Message, diagnostics: result.Diagnostics);
        }

        context.EnvironmentRepository.AppendHistory(state, "project.new", result.Message, result.ChangedFiles);
        context.EnvironmentRepository.Save(state);

        return CommandResult.Ok(
            result.Message,
            affectedState: new[] { $"projects.{ProjectService.ProjectKey(category, subCategory, name)}" },
            changedFiles: result.ChangedFiles,
            diagnostics: result.Diagnostics);
    }
}

/// <summary>endo project check</summary>
public sealed class ProjectCheckCommand : ICommand
{
    private readonly ProjectService _projectService;

    public ProjectCheckCommand(ProjectService projectService) => _projectService = projectService;

    public string Name => "project.check";
    public string Description => "Validate a registered project's directory, project.json, and Git repository against environment.json.";
    public IReadOnlyList<string> Parameters => ["key"]; // key is "Category/SubCategory/Name"

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (!args.TryGetValue("key", out var key))
        {
            return CommandResult.Fail("project.check requires a 'key' argument (Category/SubCategory/Name).");
        }

        var result = _projectService.CheckProject(state, key);

        return result.Healthy
            ? CommandResult.Ok($"Project '{key}' is healthy.")
            : CommandResult.Fail($"Project '{key}' has issues.", diagnostics: result.Findings);
    }
}

/// <summary>endo project open [--ide &lt;ide&gt;]</summary>
public sealed class ProjectOpenCommand : ICommand
{
    private readonly ProjectService _projectService;

    public ProjectOpenCommand(ProjectService projectService) => _projectService = projectService;

    public string Name => "project.open";
    public string Description => "Open a project directory. Uses the project's configured IDE by default; --ide overrides for this operation only.";
    public IReadOnlyList<string> Parameters => ["key", "ide"]; // key is "Category/SubCategory/Name"; ide is optional

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (!args.TryGetValue("key", out var key))
        {
            return CommandResult.Fail("project.open requires a 'key' argument (Category/SubCategory/Name).");
        }

        args.TryGetValue("ide", out var ideOverride);

        (string ProjectPath, string? EffectiveIde) target;
        try
        {
            target = _projectService.ResolveOpenTarget(state, key, ideOverride);
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Fail(ex.Message);
        }

        var launch = string.IsNullOrWhiteSpace(target.EffectiveIde)
            ? ProjectLauncher.OpenDirectory(target.ProjectPath)
            : ProjectLauncher.OpenWithIde(target.ProjectPath, target.EffectiveIde);

        return launch.Success
            ? CommandResult.Ok(launch.Message)
            : CommandResult.Fail(launch.Message);
    }
}
