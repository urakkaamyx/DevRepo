using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Core.Diagnostics;
using Endo.Core.Environment;

namespace Endo.Core.Tests;

/// <summary>
/// ClaudeCliInstaller shells out to real `claude`/`npm` executables, so its "not found" paths are
/// exercised here against executable names that can never resolve — proving honest failure without
/// touching this (or any) machine's real global npm/claude state. The success paths (actually
/// installing, actually logging in) were verified manually against the real CLI — see STATUS.md.
/// </summary>
public sealed class ClaudeCliInstallerTests
{
    [Fact]
    public void GetStatus_ClaudeNotOnPath_ReportsNotInstalled()
    {
        var installer = new ClaudeCliInstaller(claudeExecutable: "this-binary-does-not-exist-anywhere");

        var status = installer.GetStatus();

        Assert.False(status.Installed);
        Assert.Null(status.LoggedIn);
        Assert.Null(status.Email);
    }

    [Fact]
    public void InstallViaNpm_NpmNotOnPath_FailsHonestlyWithoutCrashing()
    {
        var installer = new ClaudeCliInstaller(npmExecutable: "this-binary-does-not-exist-anywhere");

        var result = installer.InstallViaNpm();

        Assert.False(result.Success);
        Assert.Contains("was not found on PATH", result.Message);
    }

    [Fact]
    public void Login_ClaudeNotOnPath_FailsHonestlyWithoutCrashing()
    {
        var installer = new ClaudeCliInstaller(claudeExecutable: "this-binary-does-not-exist-anywhere");

        var result = installer.Login();

        Assert.False(result.Success);
        Assert.Contains("Failed to start", result.Message);
    }
}

/// <summary>
/// Command-layer branching (already installed / not installed / already logged in), verified against
/// a fake <see cref="IClaudeCliInstaller"/> so it's deterministic and never shells out for real.
/// </summary>
public sealed class ClaudeCliCommandsTests
{
    private sealed class FakeInstaller : IClaudeCliInstaller
    {
        public ClaudeCliStatus Status { get; set; } = new(false, null, null);
        public ClaudeCliActionResult InstallResult { get; set; } = new(true, "installed");
        public ClaudeCliActionResult LoginResult { get; set; } = new(true, "logged in");
        public int InstallCallCount { get; private set; }
        public int LoginCallCount { get; private set; }

        public ClaudeCliStatus GetStatus() => Status;
        public ClaudeCliActionResult InstallViaNpm() { InstallCallCount++; return InstallResult; }
        public ClaudeCliActionResult Login() { LoginCallCount++; return LoginResult; }
    }

    private static CommandContext BuildContext()
    {
        var logger = Logger.CreateNullLogger();
        return new CommandContext
        {
            Root = Path.GetTempPath(),
            EnvironmentRepository = new EnvironmentRepository(Path.GetTempPath(), logger),
            Logger = logger,
        };
    }

    [Fact]
    public void InstallCommand_AlreadyInstalled_IsNoOp()
    {
        var fake = new FakeInstaller { Status = new ClaudeCliStatus(true, true, "amyx@example.com") };
        var command = new ClaudeCliInstallCommand(fake);

        var result = command.Execute(BuildContext(), new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(0, fake.InstallCallCount);
    }

    [Fact]
    public void InstallCommand_NotInstalled_InvokesInstall()
    {
        var fake = new FakeInstaller { Status = new ClaudeCliStatus(false, null, null) };
        var command = new ClaudeCliInstallCommand(fake);

        var result = command.Execute(BuildContext(), new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(1, fake.InstallCallCount);
    }

    [Fact]
    public void LoginCommand_NotInstalled_FailsWithoutAttemptingLogin()
    {
        var fake = new FakeInstaller { Status = new ClaudeCliStatus(false, null, null) };
        var command = new ClaudeCliLoginCommand(fake);

        var result = command.Execute(BuildContext(), new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Equal(0, fake.LoginCallCount);
    }

    [Fact]
    public void LoginCommand_AlreadyLoggedIn_IsNoOp()
    {
        var fake = new FakeInstaller { Status = new ClaudeCliStatus(true, true, "amyx@example.com") };
        var command = new ClaudeCliLoginCommand(fake);

        var result = command.Execute(BuildContext(), new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(0, fake.LoginCallCount);
    }

    [Fact]
    public void LoginCommand_InstalledNotLoggedIn_InvokesLogin()
    {
        var fake = new FakeInstaller { Status = new ClaudeCliStatus(true, false, null) };
        var command = new ClaudeCliLoginCommand(fake);

        var result = command.Execute(BuildContext(), new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(1, fake.LoginCallCount);
    }

    [Fact]
    public void StatusCommand_ReportsCurrentState()
    {
        var fake = new FakeInstaller { Status = new ClaudeCliStatus(true, true, "amyx@example.com") };
        var command = new ClaudeCliStatusCommand(fake);

        var result = command.Execute(BuildContext(), new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Contains("amyx@example.com", result.Output);
    }
}
