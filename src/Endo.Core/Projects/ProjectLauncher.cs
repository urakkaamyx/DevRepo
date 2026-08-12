using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Endo.Core.Projects;

public sealed record LaunchResult(bool Success, string Message);

/// <summary>
/// Opens a project directory, optionally in a specific IDE. Default behavior opens the
/// directory; an IDE is only used when configured or explicitly requested (04-PROJECT-SPEC.md
/// "IDE": "Require an IDE to open a project" is a listed negative constraint).
/// </summary>
public static class ProjectLauncher
{
    /// <summary>A handful of common IDE name aliases to their launch executable. Not an extensible plugin system by design (05-TOOL-SYSTEM-SPEC.md "Capability System").</summary>
    private static readonly Dictionary<string, string> IdeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["visual-studio"] = "devenv.exe",
        ["vscode"] = "code",
        ["code"] = "code",
        ["rider"] = "rider64.exe",
    };

    /// <summary>The recognized alias spellings, for UI pickers (GUI dialog, CLI prompts) to offer as a fixed list — a single source of truth so the choices shown always match what actually launches.</summary>
    public static readonly IReadOnlyList<string> KnownIdeAliases = ["visual-studio", "vscode", "rider"];

    public static LaunchResult OpenDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return new LaunchResult(false, $"Directory does not exist: '{path}'.");
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
            else
            {
                Process.Start("xdg-open", path);
            }

            return new LaunchResult(true, $"Opened directory '{path}'.");
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, $"Failed to open directory '{path}': {ex.Message}");
        }
    }

    public static LaunchResult OpenWithIde(string path, string ide)
    {
        if (!Directory.Exists(path))
        {
            return new LaunchResult(false, $"Directory does not exist: '{path}'.");
        }

        var executable = IdeAliases.GetValueOrDefault(ide, ide);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
            return new LaunchResult(true, $"Opened '{path}' in '{ide}' ({executable}).");
        }
        catch (Exception ex)
        {
            return new LaunchResult(
                false,
                $"Could not launch IDE '{ide}' (tried executable '{executable}'): {ex.Message}. Falling back is not automatic — verify '{executable}' is on PATH.");
        }
    }
}
