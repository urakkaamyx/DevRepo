using System.Text.Json.Nodes;
using Endo.Core.Ai;
using Endo.Core.Diagnostics;
using Endo.Core.Environment;
using Endo.Core.Git;

namespace Endo.Core.Setup;

public sealed record SetupResult(bool Success, string Message, string Root, List<string> ChangedFiles, List<string> Diagnostics);

public sealed record SetupAnswers(
    string Root,
    string Workspace,
    bool InitDevRepo,
    string? AiProvider,
    bool AutoCheckUpdates,
    string? AiModel = null);

/// <summary>
/// Implements `endo setup` per 02-CLI-SPEC.md: establishes managed root, workspace location,
/// DevRepo, AI configuration, update preferences, and bootstrap requirements.
///
/// The deterministic core (<see cref="Apply"/>) takes already-resolved answers so it can be
/// registered as an ICommand and invoked by the AI layer without a TTY. First-run setup must be
/// interactive for humans (01-ARCHITECTURE.md), so <see cref="RunInteractive"/> gathers answers
/// via SetupPrompts and then calls the same deterministic core — one code path either way.
/// </summary>
public sealed class SetupService
{
    private readonly IClaudeCliInstaller _claudeCliInstaller;

    public SetupService(IClaudeCliInstaller? claudeCliInstaller = null)
    {
        _claudeCliInstaller = claudeCliInstaller ?? new ClaudeCliInstaller();
    }

    public SetupResult RunInteractive(SetupPrompts prompts)
    {
        var setupDiagnostics = new List<string>();
        var existingRoot = RootLocator.TryLocateRoot();
        if (existingRoot is not null && Directory.Exists(existingRoot) && new EnvironmentRepository(existingRoot, Logger.CreateNullLogger()).Exists())
        {
            var reinit = prompts.Confirm(
                $"Endo is already set up at '{existingRoot}'. Re-run setup and overwrite the existing configuration choices?",
                false);
            if (!reinit)
            {
                return new SetupResult(false, $"Setup cancelled; existing environment at '{existingRoot}' left untouched.", existingRoot, new List<string>(), new List<string>());
            }
        }

        // 1. Environment location (config, tools, runtimes, DevRepo).
        var suggestedRoot = existingRoot ?? RootLocator.SuggestDefaultRoot();
        var root = prompts.Prompt("Environment location (config, tools, runtimes, DevRepo)", suggestedRoot);

        // 2. Default Projects folder.
        var suggestedWorkspace = RootLocator.SuggestDefaultWorkspace(root);
        var workspace = prompts.Prompt("Default Projects folder", suggestedWorkspace);

        // 3. DevRepo.
        var devRepoPath = Path.Combine(root, "DevRepo");
        var initDevRepo = prompts.Confirm($"Initialize a private DevRepo Git repository at '{devRepoPath}'?", true);

        // 4. AI configuration.
        var aiProvider = prompts.Prompt("AI provider — anthropic, claude-cli, ollama, or leave blank to configure later", "");
        string? aiModel = null;
        if (aiProvider.Trim().Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            aiModel = prompts.Prompt("Ollama model to use", "llama3.2");
        }
        else if (aiProvider.Trim().Equals("claude-cli", StringComparison.OrdinalIgnoreCase))
        {
            aiModel = prompts.Prompt("Model override for the claude CLI (leave blank to use its own default)", "");
            BootstrapClaudeCli(prompts, setupDiagnostics);
        }

        // 5. Update preferences.
        var autoCheckUpdates = prompts.Confirm("Automatically check for Endo updates?", true);

        var result = Apply(new SetupAnswers(root, workspace, initDevRepo, aiProvider, autoCheckUpdates, aiModel));
        return setupDiagnostics.Count == 0
            ? result
            : result with { Diagnostics = [.. setupDiagnostics, .. result.Diagnostics] };
    }

    /// <summary>
    /// Offers to install (`npm install -g @anthropic-ai/claude-code`) and log into the claude CLI
    /// right when the user picks it as their AI provider, rather than leaving them to discover the
    /// gap the first time `endo ai ask` fails. Interactive-only — this never runs from <see cref="Apply"/>
    /// since installing global npm packages and launching a browser OAuth flow are not things an
    /// AI-orchestrated, non-interactive setup call should trigger as a side effect.
    /// </summary>
    private void BootstrapClaudeCli(SetupPrompts prompts, List<string> diagnostics)
    {
        var status = _claudeCliInstaller.GetStatus();

        if (!status.Installed)
        {
            var install = prompts.Confirm(
                "The claude CLI isn't installed yet. Install it now via 'npm install -g @anthropic-ai/claude-code'?", true);
            if (install)
            {
                var installResult = _claudeCliInstaller.InstallViaNpm();
                diagnostics.Add(installResult.Message);
                if (installResult.Success)
                {
                    status = _claudeCliInstaller.GetStatus();
                }
            }
            else
            {
                diagnostics.Add("claude CLI install skipped — run 'endo ai install claude-cli' later.");
            }
        }

        if (status.Installed && status.LoggedIn != true)
        {
            var login = prompts.Confirm(
                "Log into the claude CLI now? This opens 'claude auth login' (browser-based OAuth).", true);
            if (login)
            {
                var loginResult = _claudeCliInstaller.Login();
                diagnostics.Add(loginResult.Message);
            }
            else
            {
                diagnostics.Add("claude CLI login skipped — run 'endo ai login claude-cli' later.");
            }
        }
    }

    /// <summary>Deterministic setup core — the part an AI-orchestrated `setup` command actually runs.</summary>
    public SetupResult Apply(SetupAnswers answers)
    {
        var diagnostics = new List<string>();
        var changedFiles = new List<string>();
        var root = answers.Root;

        // 6. Bootstrap requirements.
        var gitCheck = GitProcess.Run(Directory.Exists(root) ? root : Path.GetTempPath(), "--version");
        if (!gitCheck.Success)
        {
            diagnostics.Add("git was not found on PATH. Project Git and DevRepo features require git to be installed.");
        }

        var devRepoPath = Path.Combine(root, "DevRepo");
        var paths = new PathsInfo
        {
            Root = root,
            Config = Path.Combine(root, "config"),
            Tools = Path.Combine(root, "Tools"),
            Runtimes = Path.Combine(root, "Runtimes"),
            Libraries = Path.Combine(root, "Libraries"),
            Cache = Path.Combine(root, "Cache"),
            Scratchpad = Path.Combine(root, "Cache", "Scratchpad"),
            Logs = Path.Combine(root, "Cache", "Logs"),
            DevRepo = devRepoPath,
            Workspace = answers.Workspace,
        };

        foreach (var dir in new[]
                 {
                     paths.Root, paths.Config,
                     Path.Combine(paths.Tools, "General"),
                     paths.Runtimes, paths.Libraries,
                     paths.Cache, paths.Scratchpad, paths.Logs,
                     paths.Workspace,
                 })
        {
            Directory.CreateDirectory(dir);
            changedFiles.Add(dir);
        }

        if (answers.InitDevRepo)
        {
            Directory.CreateDirectory(devRepoPath);
            if (!GitProcess.IsGitRepository(devRepoPath))
            {
                var init = GitProcess.Run(devRepoPath, "init");
                if (!init.Success)
                {
                    diagnostics.Add($"DevRepo git init failed: {init.StdErr.Trim()}");
                }
                else
                {
                    changedFiles.Add(devRepoPath);
                }
            }
        }

        var logger = new Logger(Path.Combine(paths.Logs, "endo.log"));
        var repository = new EnvironmentRepository(root, logger);

        EnvironmentState state;
        if (repository.Exists())
        {
            state = repository.Load();
            state.Paths = paths;
        }
        else
        {
            state = new EnvironmentState { Paths = paths };
        }

        if (!string.IsNullOrWhiteSpace(answers.AiProvider))
        {
            state.Ai["provider"] = JsonValue.Create(answers.AiProvider);
        }

        if (!string.IsNullOrWhiteSpace(answers.AiModel))
        {
            state.Ai["model"] = JsonValue.Create(answers.AiModel);
        }

        state.Updates.AutoCheck = answers.AutoCheckUpdates;

        repository.AppendHistory(state, "setup", "Ran endo setup.", changedFiles);
        repository.Save(state);
        changedFiles.Add(repository.EnvironmentFilePath);

        RootLocator.SaveRoot(root);

        return new SetupResult(true, $"Endo is set up at '{root}'.", root, changedFiles, diagnostics);
    }
}
