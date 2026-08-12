using Endo.Core.Diagnostics;
using Endo.Core.Json;

namespace Endo.Core.Environment;

/// <summary>
/// Loads, saves, and reconciles the environment description. Per 03-ENVIRONMENT-SPEC.md's
/// top-level structure, each section (paths, projects, tools, runtimes, ai, ...) lives in its
/// own file under config/ rather than one monolithic environment.json — a single project
/// registration no longer rewrites the entire tools/runtimes/history payload, and each section
/// stays independently readable/diffable. All writes go through AtomicJsonWriter so a process
/// interruption never leaves a partially written file; important state changes should be
/// persisted immediately by callers (see 03-ENVIRONMENT-SPEC.md "Persistence").
/// </summary>
public sealed class EnvironmentRepository
{
    private readonly string _root;
    private readonly Logger _logger;

    public EnvironmentRepository(string root, Logger logger)
    {
        _root = root;
        _logger = logger;
    }

    public string ConfigDirectory => Path.Combine(_root, "config");

    /// <summary>
    /// The marker file used to decide whether Endo has been set up at this root. paths.json is
    /// always written first during setup, before anything else, so its presence is a reliable
    /// "is there a real environment here" check without needing to probe every section file.
    /// </summary>
    public string EnvironmentFilePath => SectionPath("paths");

    /// <summary>Legacy single-file environment.json, from before the config/ section split. Only consulted for one-time migration.</summary>
    private string LegacyEnvironmentFilePath => Path.Combine(ConfigDirectory, "environment.json");

    private string SectionPath(string section) => Path.Combine(ConfigDirectory, $"{section}.json");

    public bool Exists() => File.Exists(EnvironmentFilePath) || File.Exists(LegacyEnvironmentFilePath);

    public EnvironmentState Load()
    {
        if (!File.Exists(EnvironmentFilePath) && File.Exists(LegacyEnvironmentFilePath))
        {
            return MigrateLegacyFile();
        }

        if (!File.Exists(EnvironmentFilePath))
        {
            throw new InvalidOperationException(
                $"No Endo environment found at '{ConfigDirectory}' (expected paths.json). Run 'endo setup' first.");
        }

        var state = new EnvironmentState
        {
            Schema = ReadSection<SchemaInfo>("schema") ?? new SchemaInfo(),
            Identity = ReadSection<System.Text.Json.Nodes.JsonObject>("identity") ?? new(),
            Paths = ReadSection<PathsInfo>("paths") ?? new PathsInfo(),
            Workspace = ReadSection<System.Text.Json.Nodes.JsonObject>("workspace") ?? new(),
            Repositories = ReadSection<System.Text.Json.Nodes.JsonObject>("repositories") ?? new(),
            Projects = ReadSection<Dictionary<string, ProjectRef>>("projects") ?? new(),
            Tools = ReadSection<ToolsSection>("tools") ?? new ToolsSection(),
            Runtimes = ReadSection<Dictionary<string, Runtimes.RuntimeManifest>>("runtimes") ?? new(),
            Libraries = ReadSection<System.Text.Json.Nodes.JsonObject>("libraries") ?? new(),
            Ai = ReadSection<System.Text.Json.Nodes.JsonObject>("ai") ?? new(),
            Updates = ReadSection<UpdatesInfo>("updates") ?? new UpdatesInfo(),
            Preferences = ReadSection<System.Text.Json.Nodes.JsonObject>("preferences") ?? new(),
            Restore = ReadSection<System.Text.Json.Nodes.JsonObject>("restore") ?? new(),
            History = ReadSection<List<HistoryEntry>>("history") ?? new(),
            Metadata = ReadSection<System.Text.Json.Nodes.JsonObject>("metadata") ?? new(),
        };

        return state;
    }

    /// <summary>Force-save mechanism: always writes every section regardless of whether the caller batched other changes.</summary>
    public void Save(EnvironmentState state)
    {
        Directory.CreateDirectory(ConfigDirectory);

        AtomicJsonWriter.Write(SectionPath("schema"), state.Schema);
        AtomicJsonWriter.Write(SectionPath("identity"), state.Identity);
        AtomicJsonWriter.Write(SectionPath("paths"), state.Paths);
        AtomicJsonWriter.Write(SectionPath("workspace"), state.Workspace);
        AtomicJsonWriter.Write(SectionPath("repositories"), state.Repositories);
        AtomicJsonWriter.Write(SectionPath("projects"), state.Projects);
        AtomicJsonWriter.Write(SectionPath("tools"), state.Tools);
        AtomicJsonWriter.Write(SectionPath("runtimes"), state.Runtimes);
        AtomicJsonWriter.Write(SectionPath("libraries"), state.Libraries);
        AtomicJsonWriter.Write(SectionPath("ai"), state.Ai);
        AtomicJsonWriter.Write(SectionPath("updates"), state.Updates);
        AtomicJsonWriter.Write(SectionPath("preferences"), state.Preferences);
        AtomicJsonWriter.Write(SectionPath("restore"), state.Restore);
        AtomicJsonWriter.Write(SectionPath("history"), state.History);
        AtomicJsonWriter.Write(SectionPath("metadata"), state.Metadata);

        // The legacy single file is authoritative for nothing once split; remove it so a stale
        // copy can never be read back in preference to the section files.
        if (File.Exists(LegacyEnvironmentFilePath))
        {
            File.Delete(LegacyEnvironmentFilePath);
        }

        _logger.Debug("environment saved", new { directory = ConfigDirectory, sections = 15 });
    }

    private T? ReadSection<T>(string section) =>
        AtomicJsonWriter.TryRead<T>(SectionPath(section), out var value) ? value : default;

    /// <summary>One-time upgrade from the original single environment.json into the config/ section split.</summary>
    private EnvironmentState MigrateLegacyFile()
    {
        if (!AtomicJsonWriter.TryRead<EnvironmentState>(LegacyEnvironmentFilePath, out var state) || state is null)
        {
            throw new InvalidOperationException(
                $"'{LegacyEnvironmentFilePath}' exists but could not be read as an environment description.");
        }

        _logger.Info("Migrating legacy single-file environment.json to the config/ section split.", new { path = LegacyEnvironmentFilePath });
        Save(state);
        return state;
    }

    public void AppendHistory(EnvironmentState state, string kind, string message, IEnumerable<string>? changedFiles = null)
    {
        state.History.Add(new HistoryEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            Message = message,
            ChangedFiles = changedFiles?.ToList() ?? new List<string>()
        });
    }

    /// <summary>
    /// Compares environment.json against the actual filesystem and reports drift. Never mutates
    /// state or the filesystem — callers decide what, if anything, to do about drift.
    /// </summary>
    public DriftReport DetectDrift(EnvironmentState state)
    {
        var report = new DriftReport();

        foreach (var (key, projectRef) in state.Projects)
        {
            var projectPath = projectRef.ResolvePath(state.Paths);
            if (!Directory.Exists(projectPath))
            {
                report.MissingProjectDirectories.Add(key);
                continue;
            }

            var projectJsonPath = Path.Combine(projectPath, "project.json");
            if (!File.Exists(projectJsonPath))
            {
                report.ProjectsMissingManifest.Add(key);
            }
        }

        foreach (var (name, manifest) in state.Tools.General)
        {
            foreach (var (version, info) in manifest.Versions)
            {
                if (!Directory.Exists(info.Path))
                {
                    report.MissingToolVersions.Add($"General/{name}@{version}");
                }
            }
        }

        foreach (var (scopeKey, tools) in state.Tools.Scoped)
        {
            foreach (var (name, manifest) in tools)
            {
                foreach (var (version, info) in manifest.Versions)
                {
                    if (!Directory.Exists(info.Path))
                    {
                        report.MissingToolVersions.Add($"{scopeKey}/{name}@{version}");
                    }
                }
            }
        }

        foreach (var (name, manifest) in state.Runtimes)
        {
            foreach (var (version, info) in manifest.Versions)
            {
                if (!Directory.Exists(info.Path) && !string.IsNullOrEmpty(info.Path))
                {
                    report.MissingRuntimeVersions.Add($"{name}@{version}");
                }
            }
        }

        return report;
    }

    /// <summary>
    /// Stateful convenience layer: loads once, then exposes typed section managers
    /// (<see cref="EnvironmentAccessor.Projects"/>, .Tools, .Runtimes) that mutate and
    /// persist through this same repository — e.g. <c>Environment.Projects.Add(...)</c>
    /// always knows exactly which file it's touching without the caller managing state/save
    /// bookkeeping by hand.
    /// </summary>
    public EnvironmentAccessor Open() => new(this, Load());
}

/// <summary>
/// Covers exactly the comparison described in 03-ENVIRONMENT-SPEC.md "Drift Detection":
/// environment.json vs. filesystem, tool manifests, project.json, and runtime installation state.
/// Detecting unknown *existing* state (things on disk Endo doesn't know about) is part of Restore
/// reconciliation (10-RESTORE-MIGRATION-SPEC.md), not this report.
/// </summary>
public sealed class DriftReport
{
    public List<string> MissingProjectDirectories { get; } = new();
    public List<string> ProjectsMissingManifest { get; } = new();
    public List<string> MissingToolVersions { get; } = new();
    public List<string> MissingRuntimeVersions { get; } = new();

    public bool HasDrift =>
        MissingProjectDirectories.Count > 0 ||
        ProjectsMissingManifest.Count > 0 ||
        MissingToolVersions.Count > 0 ||
        MissingRuntimeVersions.Count > 0;
}
