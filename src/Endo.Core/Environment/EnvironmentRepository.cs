using Endo.Core.Diagnostics;
using Endo.Core.Json;

namespace Endo.Core.Environment;

/// <summary>
/// Loads, saves, and reconciles environment.json. All writes go through AtomicJsonWriter so a
/// process interruption never leaves a partially written file. Important state changes should be
/// persisted immediately by callers (see 03-ENVIRONMENT-SPEC.md "Persistence").
/// </summary>
public sealed class EnvironmentRepository
{
    private readonly string _root;
    private readonly Logger _logger;

    public string EnvironmentFilePath => Path.Combine(_root, "config", "environment.json");

    public EnvironmentRepository(string root, Logger logger)
    {
        _root = root;
        _logger = logger;
    }

    public bool Exists() => File.Exists(EnvironmentFilePath);

    public EnvironmentState Load()
    {
        if (!AtomicJsonWriter.TryRead<EnvironmentState>(EnvironmentFilePath, out var state) || state is null)
        {
            throw new InvalidOperationException(
                $"environment.json not found or unreadable at '{EnvironmentFilePath}'. Run 'endo setup' first.");
        }

        return state;
    }

    /// <summary>Force-save mechanism: always writes regardless of whether the caller batched other changes.</summary>
    public void Save(EnvironmentState state)
    {
        AtomicJsonWriter.Write(EnvironmentFilePath, state);
        _logger.Debug("environment.json saved", new { path = EnvironmentFilePath });
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
            if (!Directory.Exists(projectRef.Path))
            {
                report.MissingProjectDirectories.Add(key);
                continue;
            }

            var projectJsonPath = Path.Combine(projectRef.Path, "project.json");
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
