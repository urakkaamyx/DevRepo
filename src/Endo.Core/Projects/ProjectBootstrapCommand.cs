using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Core.Environment;

namespace Endo.Core.Projects;

/// <summary>endo project bootstrap &lt;Category/SubCategory/Name&gt; --agent &lt;claude|codex|...&gt;</summary>
public sealed class ProjectBootstrapCommand : ICommand
{
    private readonly IClaudeCliInstaller _claudeCliInstaller;

    public ProjectBootstrapCommand(IClaudeCliInstaller claudeCliInstaller) => _claudeCliInstaller = claudeCliInstaller;

    public string Name => "project.bootstrap";
    public string Description => $"Turn a project's docs/Bootstrap/BOOTSTRAP.md spec into architecture docs (via the Builder AI role), then launch an independent coding agent (known: {string.Join(", ", ProjectBootstrap.KnownAgents)}; any other name on PATH also works) in its own window to build it. Optional 'skipDocs'=true launches the agent directly on BOOTSTRAP.md, skipping docs generation.";
    public IReadOnlyList<string> Parameters => ["key", "agent", "skipDocs"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("key", out var key))
        {
            return CommandResult.Fail("project.bootstrap requires a 'key' argument (Category/SubCategory/Name).");
        }

        if (!args.TryGetValue("agent", out var agent) || string.IsNullOrWhiteSpace(agent))
        {
            return CommandResult.Fail($"project.bootstrap requires an 'agent' argument (known: {string.Join(", ", ProjectBootstrap.KnownAgents)}; any other name on PATH also works).");
        }

        var state = context.Environment ??= context.EnvironmentRepository.Load();
        if (!state.Projects.TryGetValue(key, out var projectRef))
        {
            return CommandResult.Fail($"'{key}' is not a registered project.");
        }

        var projectPath = projectRef.ResolvePath(state.Paths);

        var (specOk, specMessage, specContent) = ProjectBootstrap.ReadSpec(projectPath);
        if (!specOk)
        {
            return CommandResult.Fail(specMessage);
        }

        var diagnostics = new List<string>();
        var skipDocs = args.TryGetValue("skipDocs", out var skipDocsRaw) && bool.TryParse(skipDocsRaw, out var parsedSkip) && parsedSkip;

        if (!skipDocs)
        {
            var builder = AiProviderFactory.CreateBuilder(state);
            var docsResult = ProjectBootstrapDocs.GenerateAsync(builder, projectPath, projectRef.Name, specContent!).GetAwaiter().GetResult();
            diagnostics.Add(docsResult.Message);
            if (!docsResult.Success)
            {
                diagnostics.Add("Continuing to agent launch without generated architecture docs — it will read BOOTSTRAP.md directly instead.");
            }
        }

        var launch = ProjectBootstrap.Launch(projectPath, agent, _claudeCliInstaller);
        return launch.Success
            ? CommandResult.Ok(launch.Message, diagnostics: diagnostics)
            : CommandResult.Fail(launch.Message, diagnostics: diagnostics);
    }
}
