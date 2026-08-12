namespace Endo.Gui.Services;

/// <summary>Runs a command line in a real shell, streaming output line-by-line as it arrives.</summary>
public interface IShellRunner
{
    Task<int> RunAsync(string commandLine, Action<string> onOutputLine, CancellationToken cancellationToken = default);
}
