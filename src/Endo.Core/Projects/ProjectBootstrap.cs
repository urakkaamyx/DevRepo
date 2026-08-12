using System.Diagnostics;
using Endo.Core.Ai;

namespace Endo.Core.Projects;

/// <summary>
/// docs/Bootstrap/BOOTSTRAP.md + "endo project bootstrap": a place to paste a project spec, and a
/// way to hand it to an independent coding agent (claude, codex, ...) — run interactively in its
/// own window — to build the project out, after Endo's Builder AI role turns the spec into
/// architecture docs. Deliberately separate from Endo AI (06-AI-SPEC.md draws that line for
/// project .agents/ too): the launched agent never goes through AiOrchestrator/CommandContext at
/// all; it's a real, standalone CLI session with the project directory as its working directory,
/// working autonomously, the same as a user cd-ing in and running it themselves.
/// </summary>
public static class ProjectBootstrap
{
    public const string RelativeDirectory = "docs/Bootstrap";
    public const string FileName = "BOOTSTRAP.md";

    /// <summary>Known build-agent names offered as a fixed list (UI pickers can still accept a custom one).</summary>
    public static readonly IReadOnlyList<string> KnownAgents = ["claude", "codex"];

    private static string TemplateContent(string name) => $"""
        # Bootstrap spec — {name}

        Paste the project spec/requirements here, then run:

            endo project bootstrap <Category>/<SubCategory>/{name}

        Endo will turn this into architecture docs under docs/Architecture/ (via the Builder AI
        role), then launch an independent coding agent — not Endo's own orchestrated AI — in its
        own window, working directly in this project's directory, to start building.

        ---

        (Replace this section with the actual spec.)
        """;

    /// <summary>Creates docs/Bootstrap/BOOTSTRAP.md with a starter template. Idempotent — never overwrites an existing file.</summary>
    public static void Scaffold(string projectRoot, string name)
    {
        var dir = Path.Combine(projectRoot, "docs", "Bootstrap");
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, FileName);
        if (!File.Exists(filePath))
        {
            File.WriteAllText(filePath, TemplateContent(name));
        }
    }

    /// <summary>Reads and validates BOOTSTRAP.md — exists, and has actually been filled in past the placeholder template.</summary>
    public static (bool Ok, string Message, string? Content) ReadSpec(string projectRoot)
    {
        var filePath = Path.Combine(projectRoot, "docs", "Bootstrap", FileName);
        if (!File.Exists(filePath))
        {
            return (false, $"'{filePath}' does not exist. It should have been created when the project was made — run 'endo project check' to see what else is missing.", null);
        }

        var content = File.ReadAllText(filePath);
        var name = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (content.Trim() == TemplateContent(name).Trim())
        {
            return (false, $"'{filePath}' still has the placeholder template — paste the actual project spec into it first.", null);
        }

        return (true, "", content);
    }

    /// <summary>
    /// Launches <paramref name="agent"/> (e.g. "claude", "codex", or any other name found on PATH)
    /// interactively in its own PowerShell window, cwd = the project root, seeded to read the
    /// generated architecture docs (falling back to the raw BOOTSTRAP.md if docs generation hasn't
    /// run yet) and start building.
    /// </summary>
    public static LaunchResult Launch(string projectRoot, string agent, IClaudeCliInstaller claudeCliInstaller)
    {
        var (specOk, specMessage, _) = ReadSpec(projectRoot);
        if (!specOk)
        {
            return new LaunchResult(false, specMessage);
        }

        if (agent.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            var status = claudeCliInstaller.GetStatus();
            if (!status.Installed)
            {
                return new LaunchResult(false, "The claude CLI isn't installed. Run 'endo ai install claude-cli' first.");
            }
        }

        var architectureDir = Path.Combine(projectRoot, "docs", "Architecture");
        var hasArchitectureDocs = Directory.Exists(architectureDir) && Directory.EnumerateFiles(architectureDir, "*.md").Any();

        var seedPrompt = hasArchitectureDocs
            ? $"Read every file in {ProjectBootstrapDocs.RelativeDirectory}/ (and {RelativeDirectory}/{FileName} for original context) and start building this project accordingly."
            : $"Read {RelativeDirectory}/{FileName} in this directory and start building this project according to that spec.";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = projectRoot,
                UseShellExecute = true, // spawns its own visible window, independent of whatever launched Endo
            };
            psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add($"{agent} \"{seedPrompt.Replace("\"", "`\"")}\"");

            Process.Start(psi);
            return new LaunchResult(true, $"Launched an independent '{agent}' session in '{projectRoot}'.");
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, $"Failed to launch the bootstrap session: {ex.Message}");
        }
    }
}
