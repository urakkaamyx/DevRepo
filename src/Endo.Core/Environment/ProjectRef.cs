namespace Endo.Core.Environment;

/// <summary>
/// Endo's registration record for a project. The project's own project.json remains the
/// authoritative detail; this is the pointer environment.json keeps so Endo can enumerate
/// and reconcile projects without walking the whole workspace.
///
/// Deliberately does not store an absolute path: Category/SubCategory/Name already determine it
/// relative to the current environment's Workspace, and a machine's Workspace can live anywhere
/// (different drive letter, different folder layout after a restore). Storing a baked absolute
/// path here would go stale the moment the environment moves to a different machine. Use
/// <see cref="ProjectRefExtensions.ResolvePath"/> to get the real directory.
/// </summary>
public sealed class ProjectRef
{
    /// <summary>"Category/SubCategory/Name" — for GameModding, SubCategory is the game name.</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Soft-deactivated: still registered (environment.json keeps the record, disk/git history
    /// is untouched) but excluded from default listings/searches. Distinct from actually removing
    /// the registration — see <see cref="ProjectsManager.Disable"/> vs. <see cref="ProjectsManager.Remove"/>.
    /// </summary>
    public bool Disabled { get; set; }
}

public static class ProjectRefExtensions
{
    /// <summary>Derives the project's directory from the current environment's Workspace path.</summary>
    public static string ResolvePath(this ProjectRef projectRef, PathsInfo paths) =>
        System.IO.Path.Combine(paths.Workspace, projectRef.Category, projectRef.SubCategory, projectRef.Name);
}
