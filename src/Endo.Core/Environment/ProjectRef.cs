namespace Endo.Core.Environment;

/// <summary>
/// Endo's registration record for a project. The project's own project.json remains the
/// authoritative detail; this is the pointer environment.json keeps so Endo can enumerate
/// and reconcile projects without walking the whole workspace.
/// </summary>
public sealed class ProjectRef
{
    /// <summary>"Category/SubCategory/Name" — for GameModding, SubCategory is the game name.</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
