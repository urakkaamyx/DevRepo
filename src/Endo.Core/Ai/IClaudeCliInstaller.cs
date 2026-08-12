namespace Endo.Core.Ai;

public sealed record ClaudeCliStatus(bool Installed, bool? LoggedIn, string? Email);

public sealed record ClaudeCliActionResult(bool Success, string Message);

/// <summary>
/// Bootstraps the `claude` CLI (Claude Code) itself as a dependency of <see cref="ClaudeCliAiProvider"/> —
/// requested explicitly: rather than assuming the CLI is pre-installed, Endo should be able to
/// install it via npm and hand off to its own interactive OAuth login.
/// </summary>
public interface IClaudeCliInstaller
{
    /// <summary>Whether the CLI is on PATH, and if so, whether it currently has a logged-in session.</summary>
    ClaudeCliStatus GetStatus();

    /// <summary>Installs the CLI via `npm install -g @anthropic-ai/claude-code`. Requires npm on PATH.</summary>
    ClaudeCliActionResult InstallViaNpm();

    /// <summary>
    /// Launches `claude auth login` interactively. This is an OAuth flow that opens a browser and/or
    /// prints a code the user must act on themselves — it cannot be scripted or run headlessly.
    /// </summary>
    ClaudeCliActionResult Login();
}
