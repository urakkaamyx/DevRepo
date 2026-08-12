using Endo.Core.Environment;
using Endo.Core.Git;
using Endo.Core.Json;

namespace Endo.Core.Projects;

public sealed record ProjectCreationResult(bool Success, string Message, ProjectState? Project, List<string> ChangedFiles, List<string> Diagnostics);

public sealed record ProjectCheckResult(bool Healthy, List<string> Findings);

/// <summary>
/// Implements 04-PROJECT-SPEC.md: project hierarchy, project.json, independent project Git,
/// .agents/, and project opening with IDE preference/override.
/// </summary>
public sealed class ProjectService
{
    public static string ProjectKey(string category, string subCategory, string name) => $"{category}/{subCategory}/{name}";

    public ProjectCreationResult CreateProject(EnvironmentState state, string category, string subCategory, string name, string? ide = null, string? template = null)
    {
        var diagnostics = new List<string>();
        var changedFiles = new List<string>();

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(subCategory) || string.IsNullOrWhiteSpace(name))
        {
            return new ProjectCreationResult(false, "Category, SubCategory, and ProjectName are all required.", null, changedFiles, diagnostics);
        }

        var key = ProjectKey(category, subCategory, name);

        if (state.Projects.ContainsKey(key))
        {
            return new ProjectCreationResult(false, $"Project '{key}' is already registered.", null, changedFiles, diagnostics);
        }

        var projectRoot = Path.Combine(state.Paths.Workspace, category, subCategory, name);

        if (Directory.Exists(projectRoot) && Directory.EnumerateFileSystemEntries(projectRoot).Any())
        {
            return new ProjectCreationResult(
                false,
                $"'{projectRoot}' already exists and is not empty. Endo will not overwrite unrecognized existing state.",
                null,
                changedFiles,
                diagnostics);
        }

        Directory.CreateDirectory(projectRoot);
        changedFiles.Add(projectRoot);

        // Optional starter scaffold (e.g. a .sln + class library) -- explicit opt-in via
        // 'template', never guessed from Category/IDE. Non-fatal: a failed scaffold (e.g. the
        // .NET SDK isn't installed) still leaves a valid, registered project directory behind.
        if (!string.IsNullOrWhiteSpace(template) && !template.Equals(ProjectTemplates.None, StringComparison.OrdinalIgnoreCase))
        {
            var scaffold = ProjectTemplates.Scaffold(template, projectRoot, name);
            diagnostics.Add(scaffold.Message);
            changedFiles.AddRange(scaffold.ChangedFiles);
        }

        var agentsPath = Path.Combine(projectRoot, ".agents");
        Directory.CreateDirectory(agentsPath);
        changedFiles.Add(agentsPath);

        // Every project gets a bootstrap spec slot, independent of 'template' — see ProjectBootstrap.
        ProjectBootstrap.Scaffold(projectRoot, name);
        changedFiles.Add(Path.Combine(projectRoot, "docs", "Bootstrap", ProjectBootstrap.FileName));

        // Every project maintains its own Git repository, independent from Endo's DevRepo (07-GIT-DEVREPO-SPEC.md).
        var gitInit = GitProcess.Run(projectRoot, "init");
        if (!gitInit.Success)
        {
            diagnostics.Add($"git init failed: {gitInit.StdErr.Trim()}");
        }

        var project = new ProjectState
        {
            Identity = new ProjectIdentity { Name = name, Category = category, SubCategory = subCategory },
            Paths = new ProjectPaths { Root = projectRoot },
            Repository = new ProjectRepository { Type = "git", Remote = null },
            Ide = string.IsNullOrWhiteSpace(ide) ? null : ide,
        };

        if (!string.IsNullOrWhiteSpace(template) && !template.Equals(ProjectTemplates.None, StringComparison.OrdinalIgnoreCase))
        {
            project.Metadata["template"] = System.Text.Json.Nodes.JsonValue.Create(template);
        }

        var projectJsonPath = Path.Combine(projectRoot, "project.json");
        AtomicJsonWriter.Write(projectJsonPath, project);
        changedFiles.Add(projectJsonPath);

        var now = DateTimeOffset.UtcNow;
        state.Projects[key] = new ProjectRef
        {
            Key = key,
            Name = name,
            Category = category,
            SubCategory = subCategory,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return new ProjectCreationResult(true, $"Created project '{key}' at '{projectRoot}'.", project, changedFiles, diagnostics);
    }

    public ProjectCheckResult CheckProject(EnvironmentState state, string key)
    {
        var findings = new List<string>();

        if (!state.Projects.TryGetValue(key, out var projectRef))
        {
            return new ProjectCheckResult(false, new List<string> { $"'{key}' is not registered in environment.json." });
        }

        var projectPath = projectRef.ResolvePath(state.Paths);

        if (!Directory.Exists(projectPath))
        {
            findings.Add($"Registered project directory is missing: '{projectPath}'.");
            return new ProjectCheckResult(false, findings);
        }

        var projectJsonPath = Path.Combine(projectPath, "project.json");
        if (!File.Exists(projectJsonPath))
        {
            findings.Add($"project.json is missing at '{projectJsonPath}'.");
            return new ProjectCheckResult(false, findings);
        }

        if (!AtomicJsonWriter.TryRead<ProjectState>(projectJsonPath, out var project) || project is null)
        {
            findings.Add($"project.json at '{projectJsonPath}' could not be parsed.");
            return new ProjectCheckResult(false, findings);
        }

        if (project.Identity.Category != projectRef.Category || project.Identity.SubCategory != projectRef.SubCategory || project.Identity.Name != projectRef.Name)
        {
            findings.Add("project.json identity does not match environment.json registration.");
        }

        if (!GitProcess.IsGitRepository(projectPath))
        {
            findings.Add($"'{projectPath}' is registered as a project but has no .git directory.");
        }

        return new ProjectCheckResult(findings.Count == 0, findings);
    }

    /// <summary>
    /// Resolves the directory and effective IDE for `endo project open`. --ide (ideOverride) applies
    /// only to this operation and is never persisted, per 02-CLI-SPEC.md / 04-PROJECT-SPEC.md.
    /// </summary>
    public (string ProjectPath, string? EffectiveIde) ResolveOpenTarget(EnvironmentState state, string key, string? ideOverride)
    {
        if (!state.Projects.TryGetValue(key, out var projectRef))
        {
            throw new InvalidOperationException($"'{key}' is not a registered project.");
        }

        var projectPath = projectRef.ResolvePath(state.Paths);

        if (!string.IsNullOrWhiteSpace(ideOverride))
        {
            return (projectPath, ideOverride);
        }

        var projectJsonPath = Path.Combine(projectPath, "project.json");
        if (AtomicJsonWriter.TryRead<ProjectState>(projectJsonPath, out var project) && project is not null)
        {
            return (projectPath, project.Ide);
        }

        return (projectPath, null);
    }
}
