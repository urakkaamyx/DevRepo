namespace Endo.Core.Tools;

public sealed class ToolInstallRequest
{
    public required string Name { get; init; }
    public string? ScopeCategory { get; init; }
    public string? ScopeSubCategory { get; init; }
    public required string Repository { get; init; }
    public string Ref { get; init; } = "main";
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
