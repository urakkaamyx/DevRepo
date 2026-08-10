using Endo.Core.Diagnostics;
using Endo.Core.Git;

namespace Endo.Core.Tests;

/// <summary>Exercises DevRepoService against a real `git` working tree (git must be on PATH, as it is everywhere else in this codebase).</summary>
public sealed class DevRepoServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _devRepoPath;
    private readonly string _configPath;

    public DevRepoServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        _devRepoPath = Path.Combine(_tempDir, "DevRepo");
        _configPath = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(_devRepoPath);
        Directory.CreateDirectory(_configPath);
        GitProcess.Run(_devRepoPath, "init");
        GitProcess.Run(_devRepoPath, "config", "user.email", "test@example.com");
        GitProcess.Run(_devRepoPath, "config", "user.name", "Endo Tests");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            // git marks objects read-only on Windows; clear that before recursive delete.
            foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Checkpoint_NoConfigYet_ReportsNothingToCheckpoint()
    {
        var service = new DevRepoService(_devRepoPath, _tempDir, Logger.CreateNullLogger(), _configPath);

        var result = service.Checkpoint();

        Assert.True(result.Success);
        Assert.Null(result.CommitHash);
    }

    [Fact]
    public void Checkpoint_SnapshotsConfigAndCommits()
    {
        File.WriteAllText(Path.Combine(_configPath, "environment.json"), "{}");
        var service = new DevRepoService(_devRepoPath, _tempDir, Logger.CreateNullLogger(), _configPath);

        var result = service.Checkpoint();

        Assert.True(result.Success);
        Assert.NotNull(result.CommitHash);
        Assert.True(File.Exists(Path.Combine(_devRepoPath, "config", "environment.json")));
    }

    [Fact]
    public void Checkpoint_SecondRunWithNoChanges_ReportsClean()
    {
        File.WriteAllText(Path.Combine(_configPath, "environment.json"), "{}");
        var service = new DevRepoService(_devRepoPath, _tempDir, Logger.CreateNullLogger(), _configPath);
        service.Checkpoint();

        var second = service.Checkpoint();

        Assert.True(second.Success);
        Assert.Null(second.CommitHash);
    }

    [Fact]
    public void FindPushMd_NotPresent_ReturnsNullPath()
    {
        var service = new DevRepoService(_devRepoPath, _tempDir, Logger.CreateNullLogger(), _configPath);

        var result = service.FindPushMd();

        Assert.Null(result.Path);
    }

    [Fact]
    public void FindPushMd_PresentInDevRepo_IsFound()
    {
        File.WriteAllText(Path.Combine(_devRepoPath, "PUSH.md"), "# push guidance");
        var service = new DevRepoService(_devRepoPath, _tempDir, Logger.CreateNullLogger(), _configPath);

        var result = service.FindPushMd();

        Assert.NotNull(result.Path);
        Assert.Contains("push guidance", result.Content);
    }
}
