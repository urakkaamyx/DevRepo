using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Endo.Core.Processes;

namespace Endo.Core.Ai;

/// <summary>
/// Default <see cref="IClaudeCliInstaller"/>: shells out to the real `claude`/`npm` executables.
/// Executable names are injectable so tests can point at a name that can never resolve, without
/// touching the machine's real global npm/claude state.
/// </summary>
public sealed class ClaudeCliInstaller : IClaudeCliInstaller
{
    private readonly string _claudeExecutable;
    private readonly string _npmExecutable;

    public ClaudeCliInstaller(string claudeExecutable = "claude", string npmExecutable = "npm")
    {
        _claudeExecutable = claudeExecutable;
        _npmExecutable = npmExecutable;
    }

    public ClaudeCliStatus GetStatus()
    {
        var versionCheck = ShellProcess.Run(System.Environment.CurrentDirectory, $"{_claudeExecutable} --version", TimeSpan.FromSeconds(15));
        if (!versionCheck.Success)
        {
            return new ClaudeCliStatus(false, null, null);
        }

        var authStatus = ShellProcess.Run(System.Environment.CurrentDirectory, $"{_claudeExecutable} auth status --json", TimeSpan.FromSeconds(15));
        if (!authStatus.Success)
        {
            return new ClaudeCliStatus(true, null, null);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AuthStatusJson>(authStatus.StdOut);
            return new ClaudeCliStatus(true, parsed?.LoggedIn, parsed?.Email);
        }
        catch (JsonException)
        {
            return new ClaudeCliStatus(true, null, null);
        }
    }

    public ClaudeCliActionResult InstallViaNpm()
    {
        var npmCheck = ShellProcess.Run(System.Environment.CurrentDirectory, $"{_npmExecutable} --version", TimeSpan.FromSeconds(15));
        if (!npmCheck.Success)
        {
            return new ClaudeCliActionResult(false, $"'{_npmExecutable}' was not found on PATH. Install Node.js (which includes npm) first, then retry: https://nodejs.org");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _npmExecutable,
            UseShellExecute = false,
            // Deliberately not redirected — npm's own install progress is worth seeing live,
            // same reasoning as OllamaServerManager.PullModel.
        };
        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add("@anthropic-ai/claude-code");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new ClaudeCliActionResult(false, $"Failed to start '{_npmExecutable} install -g @anthropic-ai/claude-code': {ex.Message}");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return new ClaudeCliActionResult(false, $"'npm install -g @anthropic-ai/claude-code' exited with code {process.ExitCode}.");
        }

        var recheck = ShellProcess.Run(System.Environment.CurrentDirectory, $"{_claudeExecutable} --version", TimeSpan.FromSeconds(15));
        return recheck.Success
            ? new ClaudeCliActionResult(true, "Installed the claude CLI via npm.")
            : new ClaudeCliActionResult(false, "npm install succeeded but 'claude' still isn't runnable — check that npm's global bin directory is on PATH.");
    }

    public ClaudeCliActionResult Login()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _claudeExecutable,
            UseShellExecute = false,
            // Not redirected: OAuth login opens a browser and/or prints a URL/code the user must
            // act on directly in their own terminal — this cannot be scripted or captured.
        };
        psi.ArgumentList.Add("auth");
        psi.ArgumentList.Add("login");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new ClaudeCliActionResult(false, $"Failed to start '{_claudeExecutable} auth login': {ex.Message}");
        }

        process.WaitForExit();

        var status = GetStatus();
        return status.LoggedIn == true
            ? new ClaudeCliActionResult(true, $"Logged in as {status.Email}.")
            : new ClaudeCliActionResult(false, "Login did not complete successfully — 'claude auth status' still reports not logged in.");
    }

    private sealed class AuthStatusJson
    {
        [JsonPropertyName("loggedIn")]
        public bool LoggedIn { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
