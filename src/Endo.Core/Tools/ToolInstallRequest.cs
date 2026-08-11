namespace Endo.Core.Tools;

/// <summary>
/// Exactly one of <see cref="Repository"/> (source-first, git clone) or <see cref="ReleaseUrl"/>
/// (archive fallback) must be set — 05-TOOL-SYSTEM-SPEC.md "Source-First Acquisition": "Release/
/// archive fallback is allowed when source is unavailable or unsuitable." <see cref="Version"/>
/// is required for a release install since there is no commit hash to derive one from.
/// </summary>
public sealed class ToolInstallRequest
{
    public required string Name { get; init; }
    public string? ScopeCategory { get; init; }
    public string? ScopeSubCategory { get; init; }
    public string? Repository { get; init; }
    public string Ref { get; init; } = "main";
    public string? ReleaseUrl { get; init; }
    public string? Version { get; init; }
    public string? BuildCommand { get; init; }
    public string? ValidateCommand { get; init; }
}

public sealed class ToolInstallReport
{
    public bool Success { get; set; }
    public string ScratchpadPath { get; set; } = string.Empty;
    public string? ReadmePath { get; set; }
    public List<string> DocumentationReviewed { get; } = new();
    public List<string> StepsSucceeded { get; } = new();
    public List<string> StepsFailed { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> RecoveryAttempts { get; } = new();
    public string? FinalReason { get; set; }
    public ToolVersionInfo? InstalledVersion { get; set; }
}
