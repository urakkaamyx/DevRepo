using Endo.Core.Diagnostics;
using Endo.Core.Environment;

namespace Endo.Core.Tests;

public sealed class EnvironmentRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EnvironmentRepository _repository;

    public EnvironmentRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _repository = new EnvironmentRepository(_tempDir, Logger.CreateNullLogger());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Exists_FalseBeforeFirstSave()
    {
        Assert.False(_repository.Exists());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProjectRegistration()
    {
        var state = new EnvironmentState();
        state.Projects["GameModding/Skyrim/MyMod"] = new ProjectRef
        {
            Key = "GameModding/Skyrim/MyMod",
            Name = "MyMod",
            Category = "GameModding",
            SubCategory = "Skyrim",
            Path = Path.Combine(_tempDir, "Projects", "GameModding", "Skyrim", "MyMod"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Save(state);
        var loaded = _repository.Load();

        Assert.True(loaded.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
        Assert.Equal("MyMod", loaded.Projects["GameModding/Skyrim/MyMod"].Name);
    }

    [Fact]
    public void AppendHistory_IsPreservedAcrossSaveLoad()
    {
        var state = new EnvironmentState();
        _repository.AppendHistory(state, "test", "did a thing", new[] { "file.txt" });
        _repository.Save(state);

        var loaded = _repository.Load();

        Assert.Single(loaded.History);
        Assert.Equal("test", loaded.History[0].Kind);
    }

    [Fact]
    public void DetectDrift_MissingProjectDirectory_IsReported()
    {
        var state = new EnvironmentState();
        var missingPath = Path.Combine(_tempDir, "Projects", "GameModding", "Skyrim", "Ghost");
        state.Projects["GameModding/Skyrim/Ghost"] = new ProjectRef
        {
            Key = "GameModding/Skyrim/Ghost",
            Name = "Ghost",
            Category = "GameModding",
            SubCategory = "Skyrim",
            Path = missingPath,
        };

        var drift = _repository.DetectDrift(state);

        Assert.True(drift.HasDrift);
        Assert.Contains("GameModding/Skyrim/Ghost", drift.MissingProjectDirectories);
    }

    [Fact]
    public void DetectDrift_NoRegisteredProjects_ReportsNoDrift()
    {
        var state = new EnvironmentState();

        var drift = _repository.DetectDrift(state);

        Assert.False(drift.HasDrift);
    }
}
