namespace Endo.Core.Runtimes;

public sealed class RuntimeVersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Availability of a runtime (e.g. "python", "node") across Endo. A project separately
/// records which installed version it has *selected* in its own project.json — availability
/// and selection are deliberately different concepts.
/// </summary>
public sealed class RuntimeManifest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Version string -> install detail. Multiple versions intentionally coexist.</summary>
    public Dictionary<string, RuntimeVersionInfo> Versions { get; set; } = new();

    public string? LatestInstalled =>
        Versions.Keys
            .Select(v => (raw: v, parsed: System.Version.TryParse(NormalizeForParse(v), out var pv) ? pv : null))
            .Where(x => x.parsed is not null)
            .OrderByDescending(x => x.parsed)
            .Select(x => x.raw)
            .FirstOrDefault();

    private static string NormalizeForParse(string version)
    {
        // Version.TryParse requires at least a Major.Minor; pad bare major versions like "3".
        var parts = version.Split('.');
        return parts.Length == 1 ? version + ".0" : version;
    }
}
