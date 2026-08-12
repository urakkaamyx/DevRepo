using System.Diagnostics;
using System.Text;

namespace Endo.Core.Git;

public sealed record GitResult(bool Success, int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Thin wrapper around the `git` executable. Endo shells out to the user's real git rather than
/// reimplementing it, so project Git and DevRepo behave exactly like git everywhere else.
/// </summary>
public static class GitProcess
{
    public static GitResult Run(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Without this, .NET reads git's stdout/stderr using the console codepage rather than
            // UTF-8 on Windows, mangling any non-ASCII byte (commit messages, file names, ...).
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new GitResult(process.ExitCode == 0, process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    public static bool IsGitRepository(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git"));
}
