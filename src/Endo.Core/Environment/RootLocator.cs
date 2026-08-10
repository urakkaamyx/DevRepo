using System.Runtime.InteropServices;

namespace Endo.Core.Environment;

/// <summary>
/// Finds the Endo managed root regardless of what directory `endo` is invoked from.
///
/// Resolution order:
///   1. ENDO_ROOT environment variable (explicit override, always wins).
///   2. A small pointer file in the OS-standard per-user config location, written by
///      `endo setup` once the user has chosen a managed root.
///
/// The pointer file itself is intentionally tiny (a single path) — it is not environment.json.
/// It only exists so Endo can find environment.json in the first place.
/// </summary>
public static class RootLocator
{
    private const string PointerFileName = "root.txt";

    public static string? TryLocateRoot()
    {
        var envOverride = System.Environment.GetEnvironmentVariable("ENDO_ROOT");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        var pointerPath = PointerFilePath();
        if (File.Exists(pointerPath))
        {
            var text = File.ReadAllText(pointerPath).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    public static void SaveRoot(string rootPath)
    {
        var pointerPath = PointerFilePath();
        var dir = Path.GetDirectoryName(pointerPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(pointerPath, rootPath);
    }

    public static string PointerFilePath()
    {
        return Path.Combine(PerUserConfigDirectory(), "endo", PointerFileName);
    }

    /// <summary>OS-standard per-user config directory, independent from the Endo-managed root itself.</summary>
    public static string PerUserConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            return appData;
        }

        var xdgConfig = System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfig))
        {
            return xdgConfig;
        }

        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config");
    }

    /// <summary>Suggested default managed root, offered (not forced) during interactive setup.</summary>
    public static string SuggestDefaultRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Endo");
        }

        var xdgData = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgData))
        {
            return Path.Combine(xdgData, "endo");
        }

        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "endo");
    }

    /// <summary>Suggested default workspace (Projects/) location, as a sibling of the managed root.</summary>
    public static string SuggestDefaultWorkspace(string root)
    {
        var parent = Directory.GetParent(root)?.FullName ?? root;
        return Path.Combine(parent, "Projects");
    }
}
