using Endo.Core.Environment;
using Endo.Core.Json;
using Endo.Core.Projects;

namespace Endo.Core.Runtimes;

/// <summary>
/// Runtime availability (Endo-wide, multiple versions coexist) vs. selection (per-project,
/// stored in project.json), per 04-PROJECT-SPEC.md "Runtime Versions" and the Phase 3 roadmap.
///
/// The spec defines an explicit source-first acquisition pipeline for Tools but leaves runtime
/// acquisition unspecified. Rather than inventing a download/build system the spec doesn't
/// describe, `endo runtime install` registers an already-present installation at a given path —
/// Endo then manages multiple versions, selection, and the "latest installed" default from there.
/// </summary>
public sealed class RuntimeService
{
    public RuntimeManifest Register(EnvironmentState state, string name, string version, string path, string? notes)
    {
        if (!state.Runtimes.TryGetValue(name, out var manifest))
        {
            manifest = new RuntimeManifest { Name = name };
            state.Runtimes[name] = manifest;
        }

        manifest.Versions[version] = new RuntimeVersionInfo
        {
            Version = version,
            Path = path,
            InstalledAt = DateTimeOffset.UtcNow,
            Notes = notes,
        };

        return manifest;
    }

    public IReadOnlyList<RuntimeManifest> List(EnvironmentState state) => state.Runtimes.Values.ToList();

    /// <summary>Selects an installed version for a project. Availability does not imply selection until this is called.</summary>
    public (bool Success, string Message) SelectForProject(EnvironmentState state, string projectKey, string runtimeName, string? version)
    {
        if (!state.Projects.TryGetValue(projectKey, out var projectRef))
        {
            return (false, $"'{projectKey}' is not a registered project.");
        }

        if (!state.Runtimes.TryGetValue(runtimeName, out var manifest))
        {
            return (false, $"Runtime '{runtimeName}' is not registered with Endo. Install it first.");
        }

        var resolvedVersion = version ?? manifest.LatestInstalled;
        if (resolvedVersion is null || !manifest.Versions.ContainsKey(resolvedVersion))
        {
            return (false, $"Version '{version}' of runtime '{runtimeName}' is not installed. Installed versions: {string.Join(", ", manifest.Versions.Keys)}");
        }

        var projectJsonPath = Path.Combine(projectRef.ResolvePath(state.Paths), "project.json");
        if (!AtomicJsonWriter.TryRead<ProjectState>(projectJsonPath, out var project) || project is null)
        {
            return (false, $"project.json could not be read at '{projectJsonPath}'.");
        }

        project.Runtime[runtimeName] = resolvedVersion;
        AtomicJsonWriter.Write(projectJsonPath, project);

        return (true, $"Project '{projectKey}' now selects {runtimeName} {resolvedVersion}.");
    }
}
