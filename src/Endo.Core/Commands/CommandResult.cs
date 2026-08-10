namespace Endo.Core.Commands;

/// <summary>
/// Structured result every command produces, per 02-CLI-SPEC.md "Command Results":
/// Success/failure, Exit status, Output, Error information, Affected state, Changed files,
/// Diagnostics, Recovery information where applicable. The AI consumes these results rather
/// than guessing, so nothing here may be invented — only what the command actually observed.
/// </summary>
public sealed class CommandResult
{
    public required bool Success { get; init; }
    public required int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string? Error { get; init; }
    public List<string> AffectedState { get; init; } = new();
    public List<string> ChangedFiles { get; init; } = new();
    public List<string> Diagnostics { get; init; } = new();
    public string? RecoveryInformation { get; init; }

    public static CommandResult Ok(
        string output = "",
        IEnumerable<string>? affectedState = null,
        IEnumerable<string>? changedFiles = null,
        IEnumerable<string>? diagnostics = null) =>
        new()
        {
            Success = true,
            ExitCode = 0,
            Output = output,
            AffectedState = affectedState?.ToList() ?? new List<string>(),
            ChangedFiles = changedFiles?.ToList() ?? new List<string>(),
            Diagnostics = diagnostics?.ToList() ?? new List<string>(),
        };

    public static CommandResult Fail(
        string error,
        int exitCode = 1,
        string output = "",
        IEnumerable<string>? diagnostics = null,
        string? recoveryInformation = null) =>
        new()
        {
            Success = false,
            ExitCode = exitCode,
            Output = output,
            Error = error,
            Diagnostics = diagnostics?.ToList() ?? new List<string>(),
            RecoveryInformation = recoveryInformation,
        };
}
