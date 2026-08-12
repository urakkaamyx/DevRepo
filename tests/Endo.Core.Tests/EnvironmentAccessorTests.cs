using Endo.Core.Diagnostics;
using Endo.Core.Environment;

namespace Endo.Core.Tests;

/// <summary>
/// Environment.Projects.Add/Remove/Disable/Enable/Search() — the fluent section-manager layer
/// over EnvironmentRepository, so callers don't hand-roll load/mutate/save bookkeeping.
/// </summary>
public sealed class EnvironmentAccessorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EnvironmentRepository _repository;

    public EnvironmentAccessorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _repository = new EnvironmentRepository(_tempDir, Logger.CreateNullLogger());
        _repository.Save(new EnvironmentState { Paths = new PathsInfo { Root = _tempDir, Workspace = Path.Combine(_tempDir, "Projects") } });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Add_CreatesAndRegistersProject_PersistedAcrossReopen()
    {
        var env = _repository.Open();

        var result = env.Projects.Add("GameModding", "Skyrim", "MyMod");

        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "Projects", "GameModding", "Skyrim", "MyMod")));

        var reopened = _repository.Open();
        Assert.True(reopened.State.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
    }

    [Fact]
    public void Remove_UnregistersButLeavesDirectoryAndGitHistoryOnDisk()
    {
        var env = _repository.Open();
        env.Projects.Add("GameModding", "Skyrim", "MyMod");
        var projectDir = Path.Combine(_tempDir, "Projects", "GameModding", "Skyrim", "MyMod");

        var (success, _) = env.Projects.Remove("GameModding/Skyrim/MyMod");

        Assert.True(success);
        Assert.False(env.State.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
        Assert.True(Directory.Exists(projectDir));
        Assert.True(Directory.Exists(Path.Combine(projectDir, ".git")));

        var reopened = _repository.Open();
        Assert.False(reopened.State.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
    }

    [Fact]
    public void Remove_UnknownKey_ReportsFailureWithoutThrowing()
    {
        var env = _repository.Open();

        var (success, message) = env.Projects.Remove("GameModding/Skyrim/DoesNotExist");

        Assert.False(success);
        Assert.Contains("not registered", message);
    }

    [Fact]
    public void Disable_ExcludesFromSearchByDefault_ButStaysRegistered()
    {
        var env = _repository.Open();
        env.Projects.Add("GameModding", "Skyrim", "MyMod");

        var (success, _) = env.Projects.Disable("GameModding/Skyrim/MyMod");

        Assert.True(success);
        Assert.True(env.State.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
        Assert.Empty(env.Projects.Search());
        Assert.Single(env.Projects.Search(includeDisabled: true));

        var reopened = _repository.Open();
        Assert.True(reopened.State.Projects["GameModding/Skyrim/MyMod"].Disabled);
    }

    [Fact]
    public void Enable_ReversesDisable()
    {
        var env = _repository.Open();
        env.Projects.Add("GameModding", "Skyrim", "MyMod");
        env.Projects.Disable("GameModding/Skyrim/MyMod");

        env.Projects.Enable("GameModding/Skyrim/MyMod");

        Assert.Single(env.Projects.Search());
    }

    [Fact]
    public void Search_FiltersByCategoryAndSubCategory()
    {
        var env = _repository.Open();
        env.Projects.Add("GameModding", "Skyrim", "ModA");
        env.Projects.Add("GameModding", "ScrapMechanic", "ModB");
        env.Projects.Add("Applications", "Web", "SiteC");

        var skyrimOnly = env.Projects.Search(category: "GameModding", subCategory: "Skyrim");

        Assert.Single(skyrimOnly);
        Assert.Equal("ModA", skyrimOnly[0].Name);
    }
}
