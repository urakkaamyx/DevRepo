using Endo.Core.Environment;
using Endo.Core.Projects;

namespace Endo.Core.Tests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EnvironmentState _state;
    private readonly ProjectService _service = new();

    public ProjectServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _state = new EnvironmentState { Paths = new PathsInfo { Workspace = Path.Combine(_tempDir, "Projects") } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void CreateProject_UsesGameModdingHierarchy()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        Assert.True(result.Success);
        var expectedPath = Path.Combine(_state.Paths.Workspace, "GameModding", "Skyrim", "MyMod");
        Assert.True(Directory.Exists(expectedPath));
        Assert.True(File.Exists(Path.Combine(expectedPath, "project.json")));
        Assert.True(Directory.Exists(Path.Combine(expectedPath, ".agents")));
    }

    [Fact]
    public void CreateProject_RegistersProjectRefInEnvironmentState()
    {
        _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        Assert.True(_state.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
    }

    [Fact]
    public void CreateProject_Duplicate_Fails()
    {
        _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");
        var second = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        Assert.False(second.Success);
    }

    [Fact]
    public void CreateProject_MissingArgument_Fails()
    {
        var result = _service.CreateProject(_state, "GameModding", "", "MyMod");

        Assert.False(result.Success);
        Assert.False(_state.Projects.ContainsKey("GameModding//MyMod"));
    }

    [Fact]
    public void CreateProject_TasksActiveIsAlwaysAList()
    {
        var result = _service.CreateProject(_state, "Applications", "Web", "Site");

        Assert.NotNull(result.Project);
        Assert.Empty(result.Project!.Tasks.Active);
        Assert.IsType<List<string>>(result.Project.Tasks.Active);
    }

    [Fact]
    public void CheckProject_HealthyAfterCreation()
    {
        _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        var check = _service.CheckProject(_state, "GameModding/Skyrim/MyMod");

        Assert.True(check.Healthy);
    }

    [Fact]
    public void CheckProject_UnregisteredKey_ReportsUnhealthy()
    {
        var check = _service.CheckProject(_state, "GameModding/Skyrim/DoesNotExist");

        Assert.False(check.Healthy);
        Assert.NotEmpty(check.Findings);
    }

    [Fact]
    public void CheckProject_DirectoryDeletedAfterRegistration_DetectsDrift()
    {
        _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");
        Directory.Delete(_state.Projects["GameModding/Skyrim/MyMod"].Path, recursive: true);

        var check = _service.CheckProject(_state, "GameModding/Skyrim/MyMod");

        Assert.False(check.Healthy);
    }

    [Fact]
    public void ResolveOpenTarget_ExplicitOverride_TakesPrecedenceOverProjectJson()
    {
        _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        var (path, ide) = _service.ResolveOpenTarget(_state, "GameModding/Skyrim/MyMod", "vscode");

        Assert.Equal("vscode", ide);
        Assert.EndsWith("MyMod", path);
    }

    [Fact]
    public void ResolveOpenTarget_NoOverrideNoConfiguredIde_ReturnsNullIde()
    {
        _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        var (_, ide) = _service.ResolveOpenTarget(_state, "GameModding/Skyrim/MyMod", null);

        Assert.Null(ide);
    }
}
