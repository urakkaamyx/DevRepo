using Endo.Core.Projects;
using Endo.Core.Runtimes;
using Endo.Core.Tools;

namespace Endo.Core.Environment;

/// <summary>
/// A loaded environment plus typed section managers that mutate and persist through the same
/// <see cref="EnvironmentRepository"/> they were opened from — <c>Environment.Projects.Add(...)</c>
/// always writes back to exactly the right section file, without the caller juggling
/// load/mutate/save by hand. Get one via <see cref="EnvironmentRepository.Open"/>.
/// </summary>
public sealed class EnvironmentAccessor
{
    private readonly EnvironmentRepository _repository;

    public EnvironmentState State { get; }
    public ProjectsManager Projects { get; }
    public ToolsManager Tools { get; }
    public RuntimesManager Runtimes { get; }

    internal EnvironmentAccessor(EnvironmentRepository repository, EnvironmentState state)
    {
        _repository = repository;
        State = state;
        Projects = new ProjectsManager(this);
        Tools = new ToolsManager(this);
        Runtimes = new RuntimesManager(this);
    }

    /// <summary>Force-save, e.g. after a caller mutates <see cref="State"/> directly instead of through a manager.</summary>
    public void Save() => _repository.Save(State);

    internal void SaveAfterMutation() => _repository.Save(State);
}

/// <summary>
/// Fluent access to environment.json's <c>projects</c> section. Add mirrors <see cref="ProjectService.CreateProject"/>
/// (creates the directory + Git repo + project.json, then registers it). Remove only unregisters —
/// per the No-Loss Rule (03-ENVIRONMENT-SPEC.md) and project Git's independence from DevRepo
/// (07-GIT-DEVREPO-SPEC.md), it never deletes the project directory or its Git history. Disable is
/// the reversible alternative: keep the registration, just stop surfacing it by default.
/// </summary>
public sealed class ProjectsManager
{
    private readonly EnvironmentAccessor _env;
    private readonly ProjectService _service = new();

    internal ProjectsManager(EnvironmentAccessor env) => _env = env;

    public ProjectCreationResult Add(string category, string subCategory, string name, string? ide = null)
    {
        var result = _service.CreateProject(_env.State, category, subCategory, name, ide);
        if (result.Success)
        {
            _env.SaveAfterMutation();
        }

        return result;
    }

    /// <summary>Unregisters the project from environment.json. The directory and its Git history are left untouched on disk.</summary>
    public (bool Success, string Message) Remove(string key)
    {
        if (!_env.State.Projects.Remove(key))
        {
            return (false, $"'{key}' is not registered.");
        }

        _env.SaveAfterMutation();
        return (true, $"Unregistered '{key}'. The project directory and its Git history were left untouched on disk.");
    }

    public (bool Success, string Message) Disable(string key) => SetDisabled(key, true);

    public (bool Success, string Message) Enable(string key) => SetDisabled(key, false);

    private (bool Success, string Message) SetDisabled(string key, bool disabled)
    {
        if (!_env.State.Projects.TryGetValue(key, out var projectRef))
        {
            return (false, $"'{key}' is not registered.");
        }

        if (projectRef.Disabled == disabled)
        {
            return (true, $"'{key}' is already {(disabled ? "disabled" : "enabled")}.");
        }

        projectRef.Disabled = disabled;
        projectRef.UpdatedAt = DateTimeOffset.UtcNow;
        _env.SaveAfterMutation();
        return (true, $"'{key}' is now {(disabled ? "disabled" : "enabled")}.");
    }

    public IReadOnlyList<ProjectRef> Search(string? category = null, string? subCategory = null, string? nameContains = null, bool includeDisabled = false)
    {
        return _env.State.Projects.Values.Where(p =>
                (includeDisabled || !p.Disabled) &&
                (category is null || p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) &&
                (subCategory is null || p.SubCategory.Equals(subCategory, StringComparison.OrdinalIgnoreCase)) &&
                (nameContains is null || p.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Fluent access to environment.json's <c>tools</c> section.</summary>
public sealed class ToolsManager
{
    private readonly EnvironmentAccessor _env;
    private readonly ToolService _service;

    internal ToolsManager(EnvironmentAccessor env)
    {
        _env = env;
        _service = new ToolService(env.State.Paths.Root);
    }

    public ToolInstallReport Add(ToolInstallRequest request)
    {
        var report = _service.Install(_env.State, request);
        if (report.Success)
        {
            _env.SaveAfterMutation();
        }

        return report;
    }

    public (bool Success, string Message, List<string> Dependents) Remove(string name, string? scopeCategory = null, string? scopeSubCategory = null, string? version = null, bool force = false)
    {
        var result = _service.Remove(_env.State, name, scopeCategory, scopeSubCategory, version, force);
        if (result.Success)
        {
            _env.SaveAfterMutation();
        }

        return result;
    }

    public IReadOnlyList<(string ScopeKey, ToolManifest Manifest)> Search(string? nameContains = null)
    {
        var all = _service.List(_env.State);
        return (nameContains is null
                ? all
                : all.Where(t => t.Manifest.Identity.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}

/// <summary>Fluent access to environment.json's <c>runtimes</c> section.</summary>
public sealed class RuntimesManager
{
    private readonly EnvironmentAccessor _env;
    private readonly RuntimeService _service = new();

    internal RuntimesManager(EnvironmentAccessor env) => _env = env;

    public RuntimeManifest Add(string name, string version, string path, string? notes = null)
    {
        var manifest = _service.Register(_env.State, name, version, path, notes);
        _env.SaveAfterMutation();
        return manifest;
    }

    public IReadOnlyList<RuntimeManifest> Search(string? nameContains = null)
    {
        var all = _service.List(_env.State);
        return nameContains is null
            ? all
            : all.Where(r => r.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
