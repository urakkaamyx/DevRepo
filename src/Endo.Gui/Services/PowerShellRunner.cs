using System.Diagnostics;

namespace Endo.Gui.Services;

/// <summary>
/// Executes a command line via powershell.exe specifically (not cmd.exe) — the user drives this
/// machine through real PowerShell, so a "!&lt;command&gt;" shell escape in the chat should behave
/// the same way. Output is streamed as it arrives rather than captured and dumped at the end, so
/// a chat bubble can grow live the same way a terminal would. Deliberately WPF-agnostic (no
/// Dispatcher marshaling here) so it stays independently testable — the caller decides how to get
/// each line onto the UI thread.
/// </summary>
public sealed class PowerShellRunner : IShellRunner
{
    public async Task<int> RunAsync(string commandLine, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(commandLine);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            onOutputLine($"Failed to start powershell.exe: {ex.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
