using System.Diagnostics;
using System.Text;

namespace Endo.Core.Projects;

public sealed record TemplateScaffoldResult(bool Success, string Message, List<string> ChangedFiles);

/// <summary>
/// Optional scaffolding applied at project creation time, chosen explicitly via the 'template'
/// argument/prompt. Not part of 04-PROJECT-SPEC.md -- that spec covers IDE *preference* only, not
/// generating starter files -- so this stays a deliberate, named opt-in rather than something
/// project.new guesses at from Category/IDE. Shells out to the real 'dotnet' CLI rather than
/// hand-rolling .sln/.csproj text, the same way Endo shells out to the real 'git' for project Git.
/// </summary>
public static class ProjectTemplates
{
    public const string None = "none";
    public const string DotNetClassLib = "dotnet-classlib";

    public static readonly IReadOnlyList<string> Known = [None, DotNetClassLib];

    public static TemplateScaffoldResult Scaffold(string? template, string projectRoot, string name)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Equals(None, StringComparison.OrdinalIgnoreCase))
        {
            return new TemplateScaffoldResult(true, "No template selected.", new List<string>());
        }

        if (template.Equals(DotNetClassLib, StringComparison.OrdinalIgnoreCase))
        {
            return ScaffoldDotNetClassLib(projectRoot, name);
        }

        return new TemplateScaffoldResult(false, $"Unknown template '{template}'. Known templates: {string.Join(", ", Known)}.", new List<string>());
    }

    private static TemplateScaffoldResult ScaffoldDotNetClassLib(string projectRoot, string name)
    {
        var changedFiles = new List<string>();

        var classLibDir = Path.Combine(projectRoot, name);
        var classLib = RunDotNet(projectRoot, "new", "classlib", "-n", name, "-o", classLibDir);
        if (!classLib.Success)
        {
            return new TemplateScaffoldResult(false, $"'dotnet new classlib' failed: {FirstNonEmptyLine(classLib.StdErr, classLib.StdOut)}", changedFiles);
        }
        changedFiles.Add(classLibDir);

        var sln = RunDotNet(projectRoot, "new", "sln", "-n", name);
        if (!sln.Success)
        {
            return new TemplateScaffoldResult(false, $"'dotnet new sln' failed: {FirstNonEmptyLine(sln.StdErr, sln.StdOut)}", changedFiles);
        }

        // .NET's own SDK tooling (as of .NET 10, same as Endo's own Endo.slnx) defaults 'dotnet new
        // sln' to the modern .slnx format rather than the classic .sln — match whichever it actually
        // produced instead of assuming.
        var slnPath = Path.Combine(projectRoot, $"{name}.slnx");
        if (!File.Exists(slnPath))
        {
            slnPath = Path.Combine(projectRoot, $"{name}.sln");
        }
        changedFiles.Add(slnPath);

        var add = RunDotNet(projectRoot, "sln", slnPath, "add", Path.Combine(classLibDir, $"{name}.csproj"));
        if (!add.Success)
        {
            return new TemplateScaffoldResult(false, $"'dotnet sln add' failed: {FirstNonEmptyLine(add.StdErr, add.StdOut)}", changedFiles);
        }

        return new TemplateScaffoldResult(true, $"Scaffolded a C#/.NET class library: {Path.GetFileName(slnPath)} + {name}/{name}.csproj.", changedFiles);
    }

    private static string FirstNonEmptyLine(string stdErr, string stdOut)
    {
        var text = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "(no output)";
    }

    private static (bool Success, string StdOut, string StdErr) RunDotNet(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return (process.ExitCode == 0, stdOut.ToString(), stdErr.ToString());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (false, "", $"Could not start 'dotnet': {ex.Message}. Is the .NET SDK installed and on PATH?");
        }
    }
}
