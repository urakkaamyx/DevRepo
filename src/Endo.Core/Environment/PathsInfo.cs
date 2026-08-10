namespace Endo.Core.Environment;

/// <summary>
/// Physical filesystem locations that make up an Endo managed root.
/// The physical project root is configurable at setup time.
/// </summary>
public sealed class PathsInfo
{
    public string Root { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Tools { get; set; } = string.Empty;
    public string Runtimes { get; set; } = string.Empty;
    public string Libraries { get; set; } = string.Empty;
    public string Cache { get; set; } = string.Empty;
    public string Scratchpad { get; set; } = string.Empty;
    public string Logs { get; set; } = string.Empty;
    public string DevRepo { get; set; } = string.Empty;
    public string Workspace { get; set; } = string.Empty;
}
