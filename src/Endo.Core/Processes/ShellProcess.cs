using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Endo.Core.Processes;

public sealed record ShellResult(bool Success, int ExitCode, string StdOut, string StdErr);

/// <summary>Runs an arbitrary shell command line (a tool's build/validate command) in a working directory.</summary>
public static class ShellProcess
{
    public static ShellResult Run(string workingDirectory, string commandLine, TimeSpan? timeout = null)
    {
        var psi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo { FileName = "cmd.exe", ArgumentList = { "/c", commandLine } }
            : new ProcessStartInfo { FileName = "/bin/sh", ArgumentList = { "-c", commandLine } };

        psi.WorkingDirectory = workingDirectory;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process = new Process { StartInfo = psi };

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var finished = process.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(10)).TotalMilliseconds);
        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            return new ShellResult(false, -1, stdOut.ToString(), stdErr.ToString() + $"{System.Environment.NewLine}[timed out after {timeout}]");
        }

        process.WaitForExit();
        return new ShellResult(process.ExitCode == 0, process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}
