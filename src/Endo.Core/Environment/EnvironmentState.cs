using System.Text.Json.Nodes;
using Endo.Core.Runtimes;
using Endo.Core.Tools;

namespace Endo.Core.Environment;

public sealed class SchemaInfo
{
    public int Version { get; set; } = 1;
}

public sealed class ToolsSection
{
    /// <summary>Tool name -> manifest.</summary>
    public Dictionary<string, ToolManifest> General { get; set; } = new();

    /// <summary>"Category/SubCategory" -> (tool name -> manifest).</summary>
    public Dictionary<string, Dictionary<string, ToolManifest>> Scoped { get; set; } = new();
}

/// <summary>
/// environment.json — the backbone/state description of the Endo environment, per
/// 03-ENVIRONMENT-SPEC.md and 11-JSON-SCHEMAS-DRAFT.md.
///
/// Sections that Endo actively reasons about (paths, projects, tools, runtimes, updates,
/// history) are strongly typed. Sections that are primarily user/AI narrative or forward-looking
/// (identity, workspace, repositories, libraries, ai, preferences, restore, metadata) are kept as
/// open JsonObject bags so unknown fields are always preserved and the schema can evolve without
/// data loss, per the No-Loss Rule.
/// </summary>
public sealed class EnvironmentState
{
    public SchemaInfo Schema { get; set; } = new();

    public JsonObject Identity { get; set; } = new();
    public PathsInfo Paths { get; set; } = new();
    public JsonObject Workspace { get; set; } = new();
    public JsonObject Repositories { get; set; } = new();

    /// <summary>"Category/SubCategory/Name" -> project reference.</summary>
    public Dictionary<string, ProjectRef> Projects { get; set; } = new();

    public ToolsSection Tools { get; set; } = new();

    /// <summary>Runtime name -> manifest.</summary>
    public Dictionary<string, RuntimeManifest> Runtimes { get; set; } = new();

    public JsonObject Libraries { get; set; } = new();
    public JsonObject Ai { get; set; } = new();
    public UpdatesInfo Updates { get; set; } = new();
    public JsonObject Preferences { get; set; } = new();
    public JsonObject Restore { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
    public JsonObject Metadata { get; set; } = new();
}
