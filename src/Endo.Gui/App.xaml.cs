using System.IO;
using System.Linq;
using System.Windows;
using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Core.Diagnostics;
using Endo.Core.Environment;
using Endo.Gui.Services;
using Endo.Gui.ViewModels;
using Endo.Gui.Views;

namespace Endo.Gui;

/// <summary>
/// Composition root. Mirrors Endo.Cli/Program.cs's setup: locate/require a managed root (via
/// <see cref="SetupWindow"/> if one doesn't exist yet), build the same <see cref="CommandEngine"/>
/// every entry point uses (<see cref="EndoCommandEngineFactory"/>), then wire it into a
/// <see cref="ChatEngine"/> the chat window drives.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var root = RootLocator.TryLocateRoot();
        if (root is null || !HasEnvironment(root))
        {
            var setupWindow = new SetupWindow();
            if (setupWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            root = RootLocator.TryLocateRoot();
            if (root is null || !HasEnvironment(root))
            {
                MessageBox.Show("Setup did not complete.", "Endo", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
        }

        var logger = new Logger(Path.Combine(root, "Cache", "Logs", "endo.log"));
        var environmentRepository = new EnvironmentRepository(root, logger);
        var state = environmentRepository.Load();
        var commandEngine = EndoCommandEngineFactory.Build(root);
        var context = new CommandContext { Root = root, EnvironmentRepository = environmentRepository, Logger = logger };
        var provider = AiProviderFactory.Create(state);
        var orchestrator = new AiOrchestrator(provider, commandEngine);

        var chatEngine = new ChatEngine(orchestrator, commandEngine, context, new PowerShellRunner(), action => Dispatcher.Invoke(action));
        var statusText = $"Root: {root}    |    AI provider: {provider.Name}";
        var commands = commandEngine.ListCommands().OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var viewModel = new MainViewModel(chatEngine, commands, statusText);

        var mainWindow = new MainWindow(commandEngine, context, orchestrator) { DataContext = viewModel };
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static bool HasEnvironment(string root) => new EnvironmentRepository(root, Logger.CreateNullLogger()).Exists();
}
