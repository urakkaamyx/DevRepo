using Endo.Core.Environment;
using Endo.Core.Restore;

namespace Endo.Core.Tests;

/// <summary>
/// Phase 10 hardening: simulates restoring on a "fresh machine" — an environment.json that
/// references state which is not actually present on disk, the exact situation
/// 10-RESTORE-MIGRATION-SPEC.md's migration flow describes. Restore must reconcile honestly:
/// reuse what's already correct, repair what it safely can from state it already has, and report
/// — never fabricate — what it cannot.
/// </summary>
public sealed class RestoreServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RestoreService _service = new();

    public RestoreServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void RestoreProjects_ProjectDirectoryMissingOnFreshMachine_ReportsMissingAndUnresolved_NoCrash()
    {
        // environment.json says this project exists; the "fresh machine" filesystem says otherwise,
        // and no remote is recorded to reconstruct it from.
        var state = new EnvironmentState { Paths = new PathsInfo { Workspace = Path.Combine(_tempDir, "Projects") } };
        state.Projects["GameModding/Skyrim/MyMod"] = new ProjectRef
        {
            Key = "GameModding/Skyrim/MyMod",
            Name = "MyMod",
            Category = "GameModding",
            SubCategory = "Skyrim",
        };

        var report = _service.RestoreProjects(state);

        Assert.False(report.FullySuccessful);
        Assert.Contains("GameModding/Skyrim/MyMod", report.Missing);
        Assert.NotEmpty(report.Unresolved);
        Assert.Empty(report.Restored); // must not claim it fabricated a restore
    }

    [Fact]
    public void RestoreProjects_DirectoryPresentButProjectJsonMissing_RepairsFromKnownState()
    {
        var projectPath = Path.Combine(_tempDir, "Projects", "GameModding", "Skyrim", "MyMod");
        Directory.CreateDirectory(projectPath);

        var state = new EnvironmentState { Paths = new PathsInfo { Workspace = Path.Combine(_tempDir, "Projects") } };
        state.Projects["GameModding/Skyrim/MyMod"] = new ProjectRef
        {
            Key = "GameModding/Skyrim/MyMod",
            Name = "MyMod",
            Category = "GameModding",
            SubCategory = "Skyrim",
        };

        var report = _service.RestoreProjects(state);

        Assert.Contains(report.Repaired, r => r.Contains("MyMod") || r.Contains("GameModding/Skyrim/MyMod"));
        Assert.True(File.Exists(Path.Combine(projectPath, "project.json")), "project.json should be regenerated from the already-known ProjectRef, not fabricated content.");
    }

    [Fact]
    public void RestoreTools_MissingInstallPath_ReportsUnresolvedRatherThanSilentlyRefetching()
    {
        var state = new EnvironmentState();
        var manifest = new Tools.ToolManifest
        {
            Identity = new Tools.ToolIdentity { Name = "LOOT" },
            Source = new Tools.ToolSource { Repository = "https://example.com/loot.git", Ref = "main" },
        };
        manifest.Versions["1.0"] = new Tools.ToolVersionInfo
        {
            Version = "1.0",
            Path = Path.Combine(_tempDir, "Tools", "General", "LOOT", "versions", "1.0"),
        };
        state.Tools.General["LOOT"] = manifest;

        var report = _service.RestoreTools(state);

        Assert.False(report.FullySuccessful);
        Assert.Contains("General/LOOT@1.0", report.Missing);
        Assert.NotEmpty(report.Unresolved);
    }

    [Fact]
    public void RestoreAll_OnCompletelyFreshMachine_NeverReportsSuccessWithUnresolvedItems()
    {
        var state = new EnvironmentState { Paths = new PathsInfo { Workspace = Path.Combine(_tempDir, "Projects") } };
        state.Projects["GameModding/Skyrim/MyMod"] = new ProjectRef
        {
            Key = "GameModding/Skyrim/MyMod",
            Name = "MyMod",
            Category = "GameModding",
            SubCategory = "Skyrim",
        };

        var report = _service.RestoreAll(state);

        Assert.False(report.FullySuccessful);
        Assert.NotEmpty(report.Unresolved);
    }
}
