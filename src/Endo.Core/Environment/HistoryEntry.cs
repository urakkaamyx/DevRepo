namespace Endo.Core.Environment;

/// <summary>
/// Append-only record of a meaningful environment change. Backs drift diagnosis and
/// gives DevRepo checkpoints something concrete to describe.
/// </summary>
public sealed class HistoryEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> ChangedFiles { get; set; } = new();
}
