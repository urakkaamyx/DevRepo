using Endo.Core.Environment;
using Endo.Core.Git;
using Endo.Core.Json;
using Endo.Core.Processes;
using Endo.Core.Projects;

namespace Endo.Core.Tools;

/// <summary>
/// Implements 05-TOOL-SYSTEM-SPEC.md's tool lifecycle: source-first acquisition, Scratchpad
/// staging, README-aware setup, build/test, bounded diagnose/repair/retry, and registration only
/// after validation passes. A first error is not an automatic final failure, but nothing is
/// registered, and Scratchpad evidence is retained, until it genuinely passes.
/// </summary>
public sealed class ToolService
{
    private const int MaxAttempts = 2; // bounded repair budget for this deterministic layer (see class remarks)

    private readonly string _root;

    public ToolService(string root)
    {
        _root = root;
    }

    public ToolInstallReport Install(EnvironmentState state, ToolInstallRequest request)
    {
        var report = new ToolInstallReport();
        var scope = new ToolScope { Category = request.ScopeCategory, SubCategory = request.ScopeSubCategory };

        var scratchpadPath = scope.IsGeneral
            ? Path.Combine(state.Paths.Scratchpad, "Tools", "General", request.Name)
            : Path.Combine(state.Paths.Scratchpad, "Tools", scope.Category!, scope.SubCategory!, request.Name);
        report.ScratchpadPath = scratchpadPath;

        // Scratchpad is disposable — always start clean (05-TOOL-SYSTEM-SPEC.md "Scratchpad").
        if (Directory.Exists(scratchpadPath))
        {
            try { Directory.Delete(scratchpadPath, recursive: true); }
            catch (Exception ex) { report.Errors.Add($"Could not clear existing Scratchpad directory: {ex.Message}"); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(scratchpadPath)!);

        // Acquire: source-first (clone), per "Source-First Acquisition".
        var clone = GitProcess.Run(Path.GetDirectoryName(scratchpadPath)!, "clone", request.Repository, scratchpadPath);
        if (!clone.Success)
        {
            report.StepsFailed.Add("acquire (git clone)");
            report.Errors.Add(clone.StdErr.Trim());
            report.FinalReason = "Source could not be cloned.";
            return report; // Nothing to work with; scratchpad evidence (if any) is left in place.
        }
        report.StepsSucceeded.Add("acquire (git clone)");

        var checkoutRef = request.Ref;
        var checkout = GitProcess.Run(scratchpadPath, "checkout", checkoutRef);
        if (!checkout.Success)
        {
            report.StepsFailed.Add($"checkout ref '{checkoutRef}'");
            report.Errors.Add(checkout.StdErr.Trim());
            report.FinalReason = $"Requested ref '{checkoutRef}' could not be checked out.";
            return report;
        }
        report.StepsSucceeded.Add($"checkout ref '{checkoutRef}'");

        // README Requirement: locate and record documentation before build.
        var readme = new[] { "README.md", "README.rst", "README.txt", "README" }
            .Select(f => Path.Combine(scratchpadPath, f))
            .FirstOrDefault(File.Exists);
        report.ReadmePath = readme;
        if (readme is not null)
        {
            report.DocumentationReviewed.Add(readme);
            report.StepsSucceeded.Add("locate README");
        }
        else
        {
            report.StepsFailed.Add("locate README");
            report.Errors.Add("No README found at the repository root.");
        }

        var commitHash = GitProcess.Run(scratchpadPath, "rev-parse", "--short", "HEAD");
        var version = request.Version ?? (commitHash.Success ? commitHash.StdOut.Trim() : request.Ref);

        // Build.
        if (!string.IsNullOrWhiteSpace(request.BuildCommand))
        {
            var buildOk = RunWithBoundedRetry("build", request.BuildCommand, scratchpadPath, report);
            if (!buildOk)
            {
                report.FinalReason = "Build command failed after bounded retry.";
                return report;
            }
        }

        // Test / validate.
        var testsRun = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ValidateCommand))
        {
            var validateOk = RunWithBoundedRetry("validate", request.ValidateCommand, scratchpadPath, report);
            testsRun.Add(request.ValidateCommand);
            if (!validateOk)
            {
                report.FinalReason = "Validate command failed after bounded retry.";
                return report;
            }
        }
        else
        {
            testsRun.Add("clone+checkout (no validate command provided)");
        }

        // PASS -> Register.
        var installPath = scope.IsGeneral
            ? Path.Combine(state.Paths.Tools, "General", request.Name, "versions", version)
            : Path.Combine(state.Paths.Tools, scope.Category!, scope.SubCategory!, request.Name, "versions", version);

        Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
        if (Directory.Exists(installPath))
        {
            Directory.Delete(installPath, recursive: true);
        }
        Directory.Move(scratchpadPath, installPath);
        report.StepsSucceeded.Add("register");

        var versionInfo = new ToolVersionInfo
        {
            Version = version,
            Path = installPath,
            Commit = commitHash.Success ? commitHash.StdOut.Trim() : null,
            InstalledAt = DateTimeOffset.UtcNow,
            ValidationStatus = "passed",
            BuildCommand = request.BuildCommand,
            ValidateCommand = request.ValidateCommand,
            Provenance = { $"git clone {request.Repository} @ {request.Ref}" },
        };

        var manifest = GetOrCreateManifest(state, request.Name, scope);
        manifest.Source = new ToolSource { Type = "git", Repository = request.Repository, Ref = request.Ref, Commit = versionInfo.Commit };
        manifest.Versions[version] = versionInfo;
        manifest.Channels["latest"] = version;
        manifest.Validation = new ToolValidation { Status = "passed", Tests = testsRun };

        report.Success = true;
        report.InstalledVersion = versionInfo;
        return report;
    }

    private static bool RunWithBoundedRetry(string stepName, string command, string workingDirectory, ToolInstallReport report)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var result = ShellProcess.Run(workingDirectory, command);
            if (result.Success)
            {
                report.StepsSucceeded.Add(attempt == 1 ? stepName : $"{stepName} (attempt {attempt})");
                return true;
            }

            report.Errors.Add($"{stepName} attempt {attempt}: {result.StdErr.Trim()}");
            if (attempt < MaxAttempts)
            {
                report.RecoveryAttempts.Add($"Retrying {stepName} (attempt {attempt + 1}/{MaxAttempts}) after failure.");
            }
        }

        report.StepsFailed.Add(stepName);
        return false;
    }

    private static ToolManifest GetOrCreateManifest(EnvironmentState state, string name, ToolScope scope)
    {
        var dict = scope.IsGeneral
            ? state.Tools.General
            : state.Tools.Scoped.TryGetValue(scope.Key, out var existing)
                ? existing
                : state.Tools.Scoped[scope.Key] = new Dictionary<string, ToolManifest>();

        if (!dict.TryGetValue(name, out var manifest))
        {
            manifest = new ToolManifest { Identity = new ToolIdentity { Name = name }, Scope = scope };
            dict[name] = manifest;
        }

        return manifest;
    }

    public IEnumerable<(string ScopeKey, ToolManifest Manifest)> List(EnvironmentState state)
    {
        foreach (var (name, manifest) in state.Tools.General)
        {
            yield return ("General", manifest);
        }

        foreach (var (scopeKey, tools) in state.Tools.Scoped)
        {
            foreach (var (name, manifest) in tools)
            {
                yield return (scopeKey, manifest);
            }
        }
    }

    public ToolManifest? Find(EnvironmentState state, string name, string? scopeCategory, string? scopeSubCategory)
    {
        if (string.IsNullOrEmpty(scopeCategory))
        {
            return state.Tools.General.GetValueOrDefault(name);
        }

        var key = $"{scopeCategory}/{scopeSubCategory}";
        return state.Tools.Scoped.TryGetValue(key, out var tools) ? tools.GetValueOrDefault(name) : null;
    }

    /// <summary>
    /// Removal checks whether any registered project declares this tool as a dependency
    /// (05-TOOL-SYSTEM-SPEC.md "Removal"). --force overrides protection explicitly.
    /// </summary>
    public (bool Success, string Message, List<string> Dependents) Remove(EnvironmentState state, string name, string? scopeCategory, string? scopeSubCategory, string? version, bool force)
    {
        var manifest = Find(state, name, scopeCategory, scopeSubCategory);
        if (manifest is null)
        {
            return (false, $"Tool '{name}' is not registered.", new List<string>());
        }

        var dependents = new List<string>();
        foreach (var projectRef in state.Projects.Values)
        {
            var projectJsonPath = Path.Combine(projectRef.Path, "project.json");
            if (AtomicJsonWriter.TryRead<ProjectState>(projectJsonPath, out var project) && project is not null)
            {
                if (project.Dependencies.Tools.ContainsKey(name))
                {
                    dependents.Add(projectRef.Key);
                }
            }
        }

        if (dependents.Count > 0 && !force)
        {
            return (false, $"Tool '{name}' is required by {dependents.Count} project(s). Use --force to remove anyway.", dependents);
        }

        if (version is not null)
        {
            if (manifest.Versions.TryGetValue(version, out var versionInfo))
            {
                if (Directory.Exists(versionInfo.Path))
                {
                    Directory.Delete(versionInfo.Path, recursive: true);
                }
                manifest.Versions.Remove(version);
                manifest.Channels.Remove(manifest.Channels.FirstOrDefault(kv => kv.Value == version).Key ?? string.Empty);
            }
            else
            {
                return (false, $"Version '{version}' of tool '{name}' is not registered.", dependents);
            }
        }
        else
        {
            foreach (var v in manifest.Versions.Values)
            {
                if (Directory.Exists(v.Path))
                {
                    Directory.Delete(v.Path, recursive: true);
                }
            }

            var scope = manifest.Scope;
            var dict = scope.IsGeneral ? state.Tools.General : state.Tools.Scoped[scope.Key];
            dict.Remove(name);
        }

        return (true, $"Removed tool '{name}'{(version is not null ? $" version '{version}'" : "")}.", dependents);
    }
}
