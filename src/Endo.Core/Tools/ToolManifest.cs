namespace Endo.Core.Tools;

public sealed class ToolSchemaInfo
{
    public int Version { get; set; } = 1;
}

public sealed class ToolIdentity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>General tools have no scope. Scoped tools declare Category/SubCategory (e.g. GameModding/Skyrim).</summary>
public sealed class ToolScope
{
    public string? Category { get; set; }
    public string? SubCategory { get; set; }

    public bool IsGeneral => string.IsNullOrEmpty(Category);

    public string Key => IsGeneral ? "General" : $"{Category}/{SubCategory}";
}

public sealed class ToolSource
{
    /// <summary>"git" or "release".</summary>
    public string Type { get; set; } = "git";
    public string Repository { get; set; } = string.Empty;
    public string Ref { get; set; } = "main";
    public string? Commit { get; set; }
}

public sealed class ToolAcquisition
{
    /// <summary>"source-build" (clone + build) or "release" (download archive).</summary>
    public string Method { get; set; } = "source-build";
}

public sealed class ToolValidation
{
    public string Status { get; set; } = "unvalidated"; // unvalidated | passed | failed
    public List<string> Tests { get; set; } = new();
}

public sealed class ToolVersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Commit { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public string ValidationStatus { get; set; } = "unvalidated";
    public string? BuildCommand { get; set; }
    public string? ValidateCommand { get; set; }
    public List<string> Provenance { get; set; } = new();
}

public sealed class ToolUpdatePreference
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Full manifest for one tool. Matches 11-JSON-SCHEMAS-DRAFT.md's Tool Manifest draft.
/// Old versions are intentionally retained under Versions; Channels point at specific versions
/// rather than duplicating install state.
/// </summary>
public sealed class ToolManifest
{
    public ToolSchemaInfo Schema { get; set; } = new();
    public ToolIdentity Identity { get; set; } = new();
    public ToolScope Scope { get; set; } = new();
    public ToolSource Source { get; set; } = new();
    public ToolAcquisition Acquisition { get; set; } = new();

    /// <summary>Channel name (stable/latest/develop/custom) -> installed version string.</summary>
    public Dictionary<string, string> Channels { get; set; } = new();

    /// <summary>Version string -> version detail. Never pruned automatically.</summary>
    public Dictionary<string, ToolVersionInfo> Versions { get; set; } = new();

    public ToolValidation Validation { get; set; } = new();
    public ToolUpdatePreference Update { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
