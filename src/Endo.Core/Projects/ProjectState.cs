using System.Text.Json.Nodes;

namespace Endo.Core.Projects;

public sealed class ProjectSchemaInfo
{
    public int Version { get; set; } = 1;
}

public sealed class ProjectIdentity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
}

public sealed class ProjectPaths
{
    public string Root { get; set; } = string.Empty;
}

public sealed class ProjectRepository
{
    public string Type { get; set; } = "git";
    public string? Remote { get; set; }
}

public sealed class ProjectDependencies
{
    /// <summary>Explicit named tool -> version constraint. Availability of a tool does not make it a dependency (05-TOOL-SYSTEM-SPEC.md).</summary>
    public Dictionary<string, string> Tools { get; set; } = new();
}

public sealed class ProjectTasks
{
    /// <summary>Plural by design — 04-PROJECT-SPEC.md: "Never design active task state as a singular task field."</summary>
    public List<string> Active { get; set; } = new();
}

public sealed class ProjectAgents
{
    public string Path { get; set; } = ".agents";
}

/// <summary>
/// project.json — matches 11-JSON-SCHEMAS-DRAFT.md's project.json draft field-for-field.
/// Rich narrative belongs in Markdown, not here (04-PROJECT-SPEC.md).
/// </summary>
public sealed class ProjectState
{
    public ProjectSchemaInfo Schema { get; set; } = new();
    public ProjectIdentity Identity { get; set; } = new();
    public ProjectPaths Paths { get; set; } = new();
    public ProjectRepository Repository { get; set; } = new();

    /// <summary>Runtime name -> selected version. Availability (Endo-wide) and selection (per-project) are separate concepts.</summary>
    public Dictionary<string, string> Runtime { get; set; } = new();

    public ProjectDependencies Dependencies { get; set; } = new();

    /// <summary>Null means no IDE preference; `endo project open` then opens the directory.</summary>
    public string? Ide { get; set; }

    public ProjectTasks Tasks { get; set; } = new();
    public ProjectAgents Agents { get; set; } = new();
    public JsonObject Metadata { get; set; } = new();
}
