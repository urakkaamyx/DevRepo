using Endo.Core.Environment;
using Endo.Core.Git;
using Endo.Core.Tools;

namespace Endo.Core.Tests;

/// <summary>
/// Phase 10 hardening: interrupted/failed builds and unreachable sources must fail honestly —
/// evidence retained, nothing registered, no crash — matching 05-TOOL-SYSTEM-SPEC.md's "Error
/// Recovery" and "Failure Reports" sections.
/// </summary>
public sealed class ToolServiceHardeningTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceRepoPath;
    private readonly string _defaultBranch;
    private readonly EnvironmentState _state;
    private readonly ToolService _service;

    public ToolServiceHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _sourceRepoPath = Path.Combine(_tempDir, "source-repo");
        Directory.CreateDirectory(_sourceRepoPath);
        GitProcess.Run(_sourceRepoPath, "init");
        GitProcess.Run(_sourceRepoPath, "config", "user.email", "test@example.com");
        GitProcess.Run(_sourceRepoPath, "config", "user.name", "Endo Tests");
        File.WriteAllText(Path.Combine(_sourceRepoPath, "README.md"), "# Sample Tool");
        GitProcess.Run(_sourceRepoPath, "add", "-A");
        GitProcess.Run(_sourceRepoPath, "commit", "-m", "initial");
        _defaultBranch = GitProcess.Run(_sourceRepoPath, "rev-parse", "--abbrev-ref", "HEAD").StdOut.Trim();

        var root = Path.Combine(_tempDir, "EndoRoot");
        _state = new EnvironmentState
        {
            Paths = new PathsInfo
            {
                Root = root,
                Scratchpad = Path.Combine(root, "Cache", "Scratchpad"),
                Tools = Path.Combine(root, "Tools"),
            }
        };
        _service = new ToolService(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Install_BuildFailsMidInstall_RetainsScratchpadEvidenceAndRegistersNothing()
    {
        var request = new ToolInstallRequest
        {
            Name = "SampleTool",
            Repository = _sourceRepoPath,
            Ref = _defaultBranch,
            BuildCommand = "exit 1",
        };

        var report = _service.Install(_state, request);

        Assert.False(report.Success);
        Assert.True(Directory.Exists(report.ScratchpadPath), "Scratchpad evidence must survive a failed build, per 05-TOOL-SYSTEM-SPEC.md 'Scratchpad'.");
        Assert.Empty(_state.Tools.General);
        Assert.NotEmpty(report.Errors);
        Assert.NotEmpty(report.RecoveryAttempts); // bounded retry attempted before giving up
    }

    [Fact]
    public void Install_UnreachableRepository_FailsHonestlyWithoutCrashing()
    {
        var request = new ToolInstallRequest
        {
            Name = "GhostTool",
            Repository = Path.Combine(_tempDir, "does-not-exist-on-this-machine"),
            Ref = "main",
        };

        var report = _service.Install(_state, request);

        Assert.False(report.Success);
        Assert.Contains("acquire (git clone)", report.StepsFailed);
        Assert.NotEmpty(report.Errors);
        Assert.Empty(_state.Tools.General);
    }

    [Fact]
    public void Install_ValidBuildAndValidate_RegistersAndMovesOutOfScratchpad()
    {
        var request = new ToolInstallRequest
        {
            Name = "SampleTool",
            Repository = _sourceRepoPath,
            Ref = _defaultBranch,
            ValidateCommand = "exit 0",
        };

        var report = _service.Install(_state, request);

        Assert.True(report.Success);
        Assert.False(Directory.Exists(report.ScratchpadPath), "Successful install moves out of Scratchpad rather than leaving a copy behind.");
        Assert.True(_state.Tools.General.ContainsKey("SampleTool"));
    }

    [Fact]
    public void Install_ReleaseAcquisition_MissingVersion_FailsBeforeAttemptingDownload()
    {
        var request = new ToolInstallRequest
        {
            Name = "SomeReleaseTool",
            ReleaseUrl = "https://example.invalid/does-not-matter.zip",
        };

        var report = _service.Install(_state, request);

        Assert.False(report.Success);
        Assert.Contains("acquire (release)", report.StepsFailed);
        Assert.Empty(_state.Tools.General);
    }

    [Fact]
    public void Install_ReleaseAcquisition_UnreachableUrl_FailsHonestlyWithoutCrashing()
    {
        var request = new ToolInstallRequest
        {
            Name = "SomeReleaseTool",
            ReleaseUrl = "https://this-host-does-not-exist.invalid/archive.zip",
            Version = "1.0.0",
        };

        var report = _service.Install(_state, request);

        Assert.False(report.Success);
        Assert.Contains("acquire (release download)", report.StepsFailed);
        Assert.NotEmpty(report.Errors);
        Assert.Empty(_state.Tools.General);
    }
}
