using Endo.Core.Environment;
using Endo.Core.Git;
using Endo.Core.Json;
using Endo.Core.Projects;

namespace Endo.Core.Restore;

/// <summary>
/// Implements 10-RESTORE-MIGRATION-SPEC.md's reconciliation flow:
/// Saved State -> Inspect Current Machine -> Compare -> Restore Missing -> Reuse Compatible
/// Existing -> Report Differences. Never wipes the machine by default; unknown existing state is
/// preserved and reported, never deleted.
/// </summary>
public sealed class RestoreService
{
    public RestoreReport RestoreProjects(EnvironmentState state)
    {
        var report = new RestoreReport();

        foreach (var (key, projectRef) in state.Projects)
        {
            var dirExists = Directory.Exists(projectRef.Path);
            var projectJsonPath = Path.Combine(projectRef.Path, "project.json");
            var hasProjectJson = dirExists && File.Exists(projectJsonPath);
            var hasGit = dirExists && GitProcess.IsGitRepository(projectRef.Path);

            if (dirExists && hasProjectJson && hasGit)
            {
                report.AlreadyPresent.Add(key);
                continue;
            }

            if (!dirExists)
            {
                // Reconstruction requires a recorded remote; source is never fabricated.
                var projectJsonOnDisk = hasProjectJson; // impossible here, kept for clarity
                report.Missing.Add(key);
                report.Unresolved.Add(
                    $"{key}: directory missing at '{projectRef.Path}' and no local source is recorded to reconstruct it from. " +
                    "Project Git repositories are independent of DevRepo, so Endo cannot fabricate their history.");
                continue;
            }

            // Directory exists but something is incomplete: repair what can be safely reconstructed
            // from the ProjectRef Endo already has, without inventing content.
            var repairedAnything = false;

            if (!hasProjectJson)
            {
                var reconstructed = new ProjectState
                {
                    Identity = new ProjectIdentity { Name = projectRef.Name, Category = projectRef.Category, SubCategory = projectRef.SubCategory },
                    Paths = new ProjectPaths { Root = projectRef.Path },
                    Repository = new ProjectRepository { Type = "git", Remote = null },
                };
                AtomicJsonWriter.Write(projectJsonPath, reconstructed);
                report.Repaired.Add($"{key}: regenerated project.json from environment.json registration.");
                repairedAnything = true;
            }

            if (!hasGit)
            {
                var init = GitProcess.Run(projectRef.Path, "init");
                if (init.Success)
                {
                    report.Repaired.Add($"{key}: re-initialized missing Git repository (no prior history existed to recover).");
                    report.Warnings.Add($"{key}: Git history could not be restored because none was recorded.");
                    repairedAnything = true;
                }
                else
                {
                    report.Unresolved.Add($"{key}: git init failed during repair: {init.StdErr.Trim()}");
                }
            }

            if (!repairedAnything)
            {
                report.AlreadyPresent.Add(key);
            }
        }

        // Existing but unmanaged: project directories on disk that environment.json does not know about.
        if (Directory.Exists(state.Paths.Workspace))
        {
            var known = state.Projects.Values.Select(p => Path.GetFullPath(p.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var categoryDir in SafeEnumerateDirectories(state.Paths.Workspace))
            foreach (var subCategoryDir in SafeEnumerateDirectories(categoryDir))
            foreach (var projectDir in SafeEnumerateDirectories(subCategoryDir))
            {
                if (!known.Contains(Path.GetFullPath(projectDir)))
                {
                    report.ExistingButUnmanaged.Add(projectDir);
                }
            }
        }

        return report;
    }

    public RestoreReport RestoreTools(EnvironmentState state)
    {
        var report = new RestoreReport();

        void CheckManifest(string scopeLabel, string toolName, Tools.ToolManifest manifest)
        {
            foreach (var (version, info) in manifest.Versions)
            {
                var label = $"{scopeLabel}/{toolName}@{version}";
                if (Directory.Exists(info.Path))
                {
                    report.AlreadyPresent.Add(label);
                }
                else
                {
                    report.Missing.Add(label);
                    report.Unresolved.Add(
                        $"{label}: install path '{info.Path}' is missing. Re-acquisition from " +
                        $"'{manifest.Source.Repository}' (ref '{manifest.Source.Ref}') requires running " +
                        "'endo tool install' again; Endo does not silently re-fetch during restore.");
                }
            }
        }

        foreach (var (toolName, manifest) in state.Tools.General)
        {
            CheckManifest("General", toolName, manifest);
        }

        foreach (var (scopeKey, tools) in state.Tools.Scoped)
        {
            foreach (var (toolName, manifest) in tools)
            {
                CheckManifest(scopeKey, toolName, manifest);
            }
        }

        return report;
    }

    public RestoreReport RestoreRuntimes(EnvironmentState state)
    {
        var report = new RestoreReport();

        foreach (var (runtimeName, manifest) in state.Runtimes)
        {
            foreach (var (version, info) in manifest.Versions)
            {
                var label = $"{runtimeName}@{version}";
                if (!string.IsNullOrEmpty(info.Path) && Directory.Exists(info.Path))
                {
                    report.AlreadyPresent.Add(label);
                }
                else
                {
                    report.Missing.Add(label);
                    report.Unresolved.Add($"{label}: recorded install path is missing or was never recorded; reinstall with 'endo runtime install'.");
                }
            }
        }

        return report;
    }

    public RestoreReport RestoreAll(EnvironmentState state)
    {
        var combined = new RestoreReport();

        void Merge(RestoreReport r)
        {
            combined.Restored.AddRange(r.Restored);
            combined.AlreadyPresent.AddRange(r.AlreadyPresent);
            combined.Repaired.AddRange(r.Repaired);
            combined.Changed.AddRange(r.Changed);
            combined.Missing.AddRange(r.Missing);
            combined.Unresolved.AddRange(r.Unresolved);
            combined.ExistingButUnmanaged.AddRange(r.ExistingButUnmanaged);
            combined.Warnings.AddRange(r.Warnings);
        }

        Merge(RestoreProjects(state));
        Merge(RestoreTools(state));
        Merge(RestoreRuntimes(state));

        return combined;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Enumerable.Empty<string>();
        }
        catch (IOException) { return Enumerable.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
    }
}
