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
        Directory.Delete(_state.Projects["GameModding/Skyrim/MyMod"].ResolvePath(_state.Paths), recursive: true);

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

    [Fact]
    public void CreateProject_NoTemplate_CreatesNoScaffoldFiles()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        var projectPath = result.Project!.Paths.Root;
        Assert.Empty(Directory.EnumerateFiles(projectPath, "*.sln*"));
    }

    [Fact]
    public void CreateProject_DotNetClassLibTemplate_ScaffoldsSlnAndCsproj()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod", template: ProjectTemplates.DotNetClassLib);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var projectPath = result.Project!.Paths.Root;
        Assert.True(File.Exists(Path.Combine(projectPath, "MyMod.slnx")) || File.Exists(Path.Combine(projectPath, "MyMod.sln")));
        Assert.True(File.Exists(Path.Combine(projectPath, "MyMod", "MyMod.csproj")));
        Assert.Equal("dotnet-classlib", result.Project.Metadata["template"]!.GetValue<string>());
    }

    [Fact]
    public void CreateProject_UnknownTemplate_StillCreatesProjectButReportsDiagnostic()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod", template: "not-a-real-template");

        Assert.True(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Contains("Unknown template"));
        Assert.True(_state.Projects.ContainsKey("GameModding/Skyrim/MyMod"));
    }

    [Fact]
    public void CreateProject_AlwaysScaffoldsBootstrapFile_RegardlessOfTemplate()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        var bootstrapPath = Path.Combine(result.Project!.Paths.Root, "docs", "Bootstrap", ProjectBootstrap.FileName);
        Assert.True(File.Exists(bootstrapPath));
    }

    [Fact]
    public void ReadSpec_StillPlaceholder_ReportsNotFilledIn()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");

        var (ok, message, content) = ProjectBootstrap.ReadSpec(result.Project!.Paths.Root);

        Assert.False(ok);
        Assert.Contains("placeholder", message);
        Assert.Null(content);
    }

    [Fact]
    public void ReadSpec_FilledIn_ReturnsContent()
    {
        var result = _service.CreateProject(_state, "GameModding", "Skyrim", "MyMod");
        var bootstrapPath = Path.Combine(result.Project!.Paths.Root, "docs", "Bootstrap", ProjectBootstrap.FileName);
        File.WriteAllText(bootstrapPath, "Build a companion tracker for Scrap Mechanic.");

        var (ok, _, content) = ProjectBootstrap.ReadSpec(result.Project!.Paths.Root);

        Assert.True(ok);
        Assert.Equal("Build a companion tracker for Scrap Mechanic.", content);
    }
}
