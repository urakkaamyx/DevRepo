using Endo.Core.Ai;
using Endo.Core.Git;
using Endo.Core.Projects;
using Endo.Core.Restore;
using Endo.Core.Runtimes;
using Endo.Core.Setup;
using Endo.Core.Tools;

namespace Endo.Core.Commands;

/// <summary>
/// The single place that wires every <see cref="ICommand"/> into a <see cref="CommandEngine"/>.
/// Both Endo.Cli and Endo.Gui call this rather than each keeping their own registration list —
/// two independently-maintained lists is exactly the kind of drift that left the old hand-written
/// CLI usage text missing the claudeCli.* commands (see <see cref="HelpCommand"/>); centralizing
/// the registration itself closes that gap one level deeper than just centralizing the help text.
/// </summary>
public static class EndoCommandEngineFactory
{
    public static CommandEngine Build(string root)
    {
        var engine = new CommandEngine();

        var claudeCliInstaller = new ClaudeCliInstaller();

        var projectService = new ProjectService();
        engine.Register(new ProjectNewCommand(projectService));
        engine.Register(new ProjectCheckCommand(projectService));
        engine.Register(new ProjectOpenCommand(projectService));
        engine.Register(new ProjectBootstrapCommand(claudeCliInstaller));

        var toolService = new ToolService(root);
        engine.Register(new ToolListCommand(toolService));
        engine.Register(new ToolInfoCommand(toolService));
        engine.Register(new ToolInstallCommand(toolService));
        engine.Register(new ToolRemoveCommand(toolService));

        var runtimeService = new RuntimeService();
        engine.Register(new RuntimeListCommand(runtimeService));
        engine.Register(new RuntimeInstallCommand(runtimeService));
        engine.Register(new RuntimeSetCommand(runtimeService));

        engine.Register(new DevRepoCheckpointCommand(ctx => new DevRepoService(
            Path.Combine(ctx.Root, "DevRepo"), ctx.Root, ctx.Logger, Path.Combine(ctx.Root, "config"))));

        var restoreService = new RestoreService();
        engine.Register(new RestoreCommand(restoreService));

        engine.Register(new SetupCommand(new SetupService()));

        engine.Register(new OllamaServeCommand());
        engine.Register(new OllamaPullCommand());

        engine.Register(new ClaudeCliStatusCommand(claudeCliInstaller));
        engine.Register(new ClaudeCliInstallCommand(claudeCliInstaller));
        engine.Register(new ClaudeCliLoginCommand(claudeCliInstaller));

        // Registered last so its own catalog listing includes every command above, itself included.
        engine.Register(new HelpCommand(engine));

        return engine;
    }
}
