using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Core.Diagnostics;
using Endo.Core.Environment;
using Endo.Core.Git;
using Endo.Core.Projects;
using Endo.Core.Restore;
using Endo.Core.Runtimes;
using Endo.Core.Setup;
using Endo.Core.Tools;

namespace Endo.Cli;

/// <summary>
/// The actual CLI dispatch logic, factored out of <see cref="Program"/> so the unified
/// Endo executable can drive it directly (console-mode invocation) without spawning a
/// separate process. Endo.Cli remains independently runnable for debugging via Program.Main.
/// </summary>
public static class CliHost
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return Dispatch(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] {ex.Message}");
            return 1;
        }
    }

    private static int Dispatch(string[] args)
    {
        var verb = args[0];

        if (verb == "setup")
        {
            return RunSetup(args);
        }

        if (verb is "help" or "--help" or "-h")
        {
            return RunHelp();
        }

        // Every other verb needs an existing managed root.
        var root = RootLocator.TryLocateRoot();
        if (root is null || !Directory.Exists(root))
        {
            Console.Error.WriteLine("Endo has not been set up. Run 'endo setup' first.");
            return 1;
        }

        var logger = new Logger(Path.Combine(root, "Cache", "Logs", "endo.log"));
        var environmentRepository = new EnvironmentRepository(root, logger);
        if (!environmentRepository.Exists())
        {
            Console.Error.WriteLine($"environment.json not found at '{environmentRepository.EnvironmentFilePath}'. Run 'endo setup' first.");
            return 1;
        }

        var engine = EndoCommandEngineFactory.Build(root);
        var context = new CommandContext { Root = root, EnvironmentRepository = environmentRepository, Logger = logger };

        return verb switch
        {
            "project" => RunProject(engine, context, args),
            "tool" => RunTool(engine, context, args),
            "runtime" => RunRuntime(engine, context, args),
            "devrepo" => RunDevRepo(engine, context, args),
            "ai" => RunAi(engine, context, args).GetAwaiter().GetResult(),
            "update" => RunUpdate(engine, context, args),
            _ => Unknown(verb),
        };
    }

    // ---- setup ----

    private static int RunSetup(string[] args)
    {
        var restoreIndex = Array.IndexOf(args, "--restore");
        if (restoreIndex >= 0)
        {
            var scope = restoreIndex + 1 < args.Length ? args[restoreIndex + 1] : "all";
            return RunRestoreViaSetup(scope);
        }

        var setupService = new SetupService();
        var result = setupService.RunInteractive(SetupPrompts.Console());

        PrintResultLine(result.Success, result.Message);
        foreach (var d in result.Diagnostics)
        {
            Console.WriteLine($"  note: {d}");
        }

        return result.Success ? 0 : 1;
    }

    // ---- help ----

    /// <summary>
    /// Builds the same catalog AiOrchestrator sends the AI provider and prints it for humans —
    /// reading each command's real <c>Description</c>/<c>Parameters</c> rather than a hand-written
    /// usage string that inevitably drifts (this is exactly how the old static list ended up
    /// missing the claudeCli.* commands). Works even before 'endo setup' has run: constructing the
    /// command objects only stores their dependencies, it never touches the filesystem.
    /// </summary>
    private static int RunHelp()
    {
        var root = RootLocator.TryLocateRoot() ?? Path.Combine(Path.GetTempPath(), "endo-help");
        var engine = EndoCommandEngineFactory.Build(root);

        Console.WriteLine("Endo — managed development environment and orchestration system.\n");
        foreach (var c in engine.ListCommands().OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {c.Name}({string.Join(", ", c.Parameters)})");
            Console.WriteLine($"      {c.Description}");
        }

        if (RootLocator.TryLocateRoot() is null)
        {
            Console.WriteLine("\nRun 'endo setup' first to get started.");
        }

        return 0;
    }

    private static int RunRestoreViaSetup(string scope)
    {
        var root = RootLocator.TryLocateRoot();
        if (root is null)
        {
            Console.Error.WriteLine("Endo has not been set up. Run 'endo setup' first (restore reconciles an existing environment, it does not create one from nothing).");
            return 1;
        }

        var logger = new Logger(Path.Combine(root, "Cache", "Logs", "endo.log"));
        var environmentRepository = new EnvironmentRepository(root, logger);
        var engine = EndoCommandEngineFactory.Build(root);
        var context = new CommandContext { Root = root, EnvironmentRepository = environmentRepository, Logger = logger };

        var result = engine.Execute("restore", context, new Dictionary<string, string> { ["scope"] = scope });
        PrintCommandResult(result);
        return result.Success ? 0 : 1;
    }

    // ---- project ----

    private static int RunProject(CommandEngine engine, CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: endo project <new|check|open> ...");
            return 1;
        }

        switch (args[1])
        {
            case "new":
            {
                string category, subCategory, name;
                string? ide = null;
                string? runtimeChoice = null;

                // Positional args = scriptable/automation usage: no prompts, matching 02-CLI-SPEC.md
                // (ordinary CLI operation shouldn't gain unnecessary approval gates). Bare
                // 'endo project new' is the guided path — mirrors the GUI's New Project dialog:
                // IDE, runtime, and (for a new GameModding game) a tool-discovery checklist.
                var interactive = args.Length < 5;
                if (!interactive)
                {
                    category = args[2];
                    subCategory = args[3];
                    name = args[4];
                }
                else
                {
                    var prompts = SetupPrompts.Console();
                    category = prompts.Prompt("Category (e.g. GameModding)", "");
                    subCategory = prompts.Prompt("SubCategory (e.g. Skyrim)", "");
                    name = prompts.Prompt("Project name", "");

                    var ideAnswer = prompts.Prompt($"IDE (blank for none; known: {string.Join(", ", ProjectLauncher.KnownIdeAliases)})", "");
                    ide = string.IsNullOrWhiteSpace(ideAnswer) ? null : ideAnswer;

                    var state = context.Environment ??= context.EnvironmentRepository.Load();
                    if (state.Runtimes.Count > 0)
                    {
                        var runtimeAnswer = prompts.Prompt($"Runtime (blank for none; known: {string.Join(", ", state.Runtimes.Keys)})", "");
                        runtimeChoice = string.IsNullOrWhiteSpace(runtimeAnswer) ? null : runtimeAnswer;
                    }
                }

                var projectArgs = new Dictionary<string, string> { ["category"] = category, ["subCategory"] = subCategory, ["name"] = name };
                if (!string.IsNullOrWhiteSpace(ide))
                {
                    projectArgs["ide"] = ide;
                }

                var result = engine.Execute("project.new", context, projectArgs);
                PrintCommandResult(result);
                if (!result.Success)
                {
                    return 1;
                }

                if (!string.IsNullOrWhiteSpace(runtimeChoice))
                {
                    var runtimeResult = engine.Execute("runtime.set", context, new Dictionary<string, string>
                    {
                        ["project"] = ProjectService.ProjectKey(category, subCategory, name),
                        ["runtime"] = runtimeChoice,
                    });
                    PrintCommandResult(runtimeResult);
                }

                if (interactive && category.Equals("GameModding", StringComparison.OrdinalIgnoreCase))
                {
                    RunGameModdingDiscoveryPrompt(engine, context, category, subCategory);
                }

                return 0;
            }
            case "check":
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: endo project check <Category/SubCategory/Name>");
                    return 1;
                }
                var result = engine.Execute("project.check", context, new Dictionary<string, string> { ["key"] = args[2] });
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            case "open":
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: endo project open <Category/SubCategory/Name> [--ide <ide>]");
                    return 1;
                }
                var cmdArgs = new Dictionary<string, string> { ["key"] = args[2] };
                var ideIndex = Array.IndexOf(args, "--ide");
                if (ideIndex >= 0 && ideIndex + 1 < args.Length)
                {
                    cmdArgs["ide"] = args[ideIndex + 1];
                }
                var result = engine.Execute("project.open", context, cmdArgs);
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            default:
                Console.Error.WriteLine($"Unknown project subcommand '{args[1]}'.");
                return 1;
        }
    }

    /// <summary>
    /// 04-PROJECT-SPEC.md "GameModding Discovery": only for the first project under a new game.
    /// Mirrors the GUI's New Project dialog — search, show numbered candidates, let the user pick
    /// which to install, rather than installing everything found automatically.
    /// </summary>
    private static void RunGameModdingDiscoveryPrompt(CommandEngine engine, CommandContext context, string category, string subCategory)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();
        var prefix = $"{category}/{subCategory}/";
        var projectsForThisGame = state.Projects.Keys.Count(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (projectsForThisGame != 1)
        {
            return;
        }

        Console.WriteLine("New game for this environment — searching for modding tools...");
        var orchestrator = new AiOrchestrator(AiProviderFactory.Create(state), engine);
        var search = orchestrator.FindCandidatesAsync(category, subCategory).GetAwaiter().GetResult();
        var candidates = search.Candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Repository))
            .ToList();

        if (!search.Success || candidates.Count == 0)
        {
            Console.WriteLine($"  {search.Message}");
            return;
        }

        Console.WriteLine($"Found {candidates.Count} candidate(s):");
        for (var i = 0; i < candidates.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {candidates[i].Name} — {candidates[i].Repository}");
        }

        var answer = SetupPrompts.Console().Prompt("Install which? (comma-separated numbers, 'all', or blank for none)", "");
        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        List<DiscoveredToolCandidate> selected;
        if (answer.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selected = candidates;
        }
        else
        {
            selected = new List<DiscoveredToolCandidate>();
            foreach (var token in answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(token, out var index) && index >= 1 && index <= candidates.Count)
                {
                    selected.Add(candidates[index - 1]);
                }
            }
        }

        if (selected.Count == 0)
        {
            Console.WriteLine("  Nothing selected.");
            return;
        }

        var report = orchestrator.InstallCandidates(category, subCategory, selected, context);
        Console.WriteLine($"  {report.Message}");
        foreach (var r in report.Results)
        {
            Console.WriteLine($"    {(r.Success ? "[ok]" : "[fail]")} {r.Name}: {r.Message}");
        }
    }

    // ---- tool ----

    private static int RunTool(CommandEngine engine, CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: endo tool <list|info|install|remove> ...");
            return 1;
        }

        switch (args[1])
        {
            case "list":
                PrintCommandResult(engine.Execute("tool.list", context, new Dictionary<string, string>()));
                return 0;
            case "info":
            {
                if (args.Length < 3) { Console.Error.WriteLine("Usage: endo tool info <name> [--scope Category/SubCategory]"); return 1; }
                var cmdArgs = new Dictionary<string, string> { ["name"] = args[2] };
                ApplyScopeOption(args, cmdArgs);
                var result = engine.Execute("tool.info", context, cmdArgs);
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            case "install":
            {
                if (args.Length < 3) { Console.Error.WriteLine("Usage: endo tool install <name> (--repo <url> | --release <url>) [--ref <ref>] [--version <v>] [--scope Category/SubCategory] [--build <cmd>] [--validate <cmd>]"); return 1; }
                var cmdArgs = new Dictionary<string, string> { ["name"] = args[2] };
                ApplyScopeOption(args, cmdArgs);
                ApplyOption(args, "--repo", "repository", cmdArgs);
                ApplyOption(args, "--release", "releaseUrl", cmdArgs);
                ApplyOption(args, "--ref", "ref", cmdArgs);
                ApplyOption(args, "--version", "version", cmdArgs);
                ApplyOption(args, "--build", "buildCommand", cmdArgs);
                ApplyOption(args, "--validate", "validateCommand", cmdArgs);
                var result = engine.Execute("tool.install", context, cmdArgs);
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            case "remove":
            {
                if (args.Length < 3) { Console.Error.WriteLine("Usage: endo tool remove <name> [--scope Category/SubCategory] [--version v] [--force]"); return 1; }
                var cmdArgs = new Dictionary<string, string> { ["name"] = args[2] };
                ApplyScopeOption(args, cmdArgs);
                ApplyOption(args, "--version", "version", cmdArgs);
                if (args.Contains("--force")) cmdArgs["force"] = "true";
                var result = engine.Execute("tool.remove", context, cmdArgs);
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            default:
                Console.Error.WriteLine($"Unknown tool subcommand '{args[1]}'.");
                return 1;
        }
    }

    // ---- runtime ----

    private static int RunRuntime(CommandEngine engine, CommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: endo runtime <list|install|set> ...");
            return 1;
        }

        switch (args[1])
        {
            case "list":
                PrintCommandResult(engine.Execute("runtime.list", context, new Dictionary<string, string>()));
                return 0;
            case "install":
            {
                if (args.Length < 4) { Console.Error.WriteLine("Usage: endo runtime install <name> <version> --path <path>"); return 1; }
                var cmdArgs = new Dictionary<string, string> { ["name"] = args[2], ["version"] = args[3] };
                ApplyOption(args, "--path", "path", cmdArgs);
                var result = engine.Execute("runtime.install", context, cmdArgs);
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            case "set":
            {
                if (args.Length < 3) { Console.Error.WriteLine("Usage: endo runtime set <runtime> --project <key> [--version v]"); return 1; }
                var cmdArgs = new Dictionary<string, string> { ["runtime"] = args[2] };
                ApplyOption(args, "--project", "project", cmdArgs);
                ApplyOption(args, "--version", "version", cmdArgs);
                var result = engine.Execute("runtime.set", context, cmdArgs);
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
            }
            default:
                Console.Error.WriteLine($"Unknown runtime subcommand '{args[1]}'.");
                return 1;
        }
    }

    // ---- devrepo ----

    private static int RunDevRepo(CommandEngine engine, CommandContext context, string[] args)
    {
        if (args.Length < 2 || args[1] != "checkpoint")
        {
            Console.Error.WriteLine("Usage: endo devrepo checkpoint [--message <msg>]");
            return 1;
        }

        var cmdArgs = new Dictionary<string, string>();
        ApplyOption(args, "--message", "message", cmdArgs);
        var result = engine.Execute("devrepo.checkpoint", context, cmdArgs);
        PrintCommandResult(result);
        return result.Success ? 0 : 1;
    }

    // ---- ai ----

    private static async Task<int> RunAi(CommandEngine engine, CommandContext context, string[] args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (args.Length >= 3 && args[1] == "ask")
        {
            var orchestrator = new AiOrchestrator(AiProviderFactory.Create(state), engine);
            var result = await orchestrator.AskAsync(args[2], context);

            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }

        if (args.Length >= 4 && args[1] == "discover")
        {
            var orchestrator = new AiOrchestrator(AiProviderFactory.Create(state), engine);
            var report = await orchestrator.DiscoverToolsAsync(args[2], args[3], context);

            PrintResultLine(report.Success, report.Message);
            foreach (var r in report.Results)
            {
                Console.WriteLine($"  {(r.Success ? "[ok]" : "[fail]")} {r.Name}: {r.Message}");
            }
            return report.Success ? 0 : 1;
        }

        if (args.Length >= 2 && args[1] == "serve")
        {
            var cmdArgs = new Dictionary<string, string>();
            ApplyOption(args, "--base-url", "baseUrl", cmdArgs);

            var serveResult = engine.Execute("ollama.serve", context, cmdArgs);
            PrintCommandResult(serveResult);
            if (!serveResult.Success)
            {
                return 1;
            }

            var modelIndex = Array.IndexOf(args, "--model");
            if (modelIndex >= 0 && modelIndex + 1 < args.Length)
            {
                var pullArgs = new Dictionary<string, string>(cmdArgs) { ["model"] = args[modelIndex + 1] };
                var pullResult = engine.Execute("ollama.pull", context, pullArgs);
                PrintCommandResult(pullResult);
                return pullResult.Success ? 0 : 1;
            }

            return 0;
        }

        if (args.Length >= 3 && args[1] == "install" && args[2] == "claude-cli")
        {
            var prompts = SetupPrompts.Console();
            var proceed = prompts.Confirm(
                "This may run 'npm install -g @anthropic-ai/claude-code' (a global npm install) if the claude CLI isn't already on PATH. Continue?",
                true);
            if (!proceed)
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }

            var installResult = engine.Execute("claudeCli.install", context, new Dictionary<string, string>());
            PrintCommandResult(installResult);
            if (!installResult.Success)
            {
                return 1;
            }

            var loginNow = prompts.Confirm(
                "Log into the claude CLI now? This opens 'claude auth login' (browser-based OAuth).", true);
            if (!loginNow)
            {
                return 0;
            }

            var loginResult = engine.Execute("claudeCli.login", context, new Dictionary<string, string>());
            PrintCommandResult(loginResult);
            return loginResult.Success ? 0 : 1;
        }

        if (args.Length >= 3 && args[1] == "login" && args[2] == "claude-cli")
        {
            var loginResult = engine.Execute("claudeCli.login", context, new Dictionary<string, string>());
            PrintCommandResult(loginResult);
            return loginResult.Success ? 0 : 1;
        }

        if (args.Length >= 3 && args[1] == "status" && args[2] == "claude-cli")
        {
            var statusResult = engine.Execute("claudeCli.status", context, new Dictionary<string, string>());
            PrintCommandResult(statusResult);
            return statusResult.Success ? 0 : 1;
        }

        Console.Error.WriteLine("Usage: endo ai ask \"<request>\"");
        Console.Error.WriteLine("       endo ai discover <Category> <SubCategory>");
        Console.Error.WriteLine("       endo ai serve [--model <name>] [--base-url <url>]");
        Console.Error.WriteLine("       endo ai install claude-cli");
        Console.Error.WriteLine("       endo ai login claude-cli");
        Console.Error.WriteLine("       endo ai status claude-cli");
        return 1;
    }

    // ---- update ----

    private static int RunUpdate(CommandEngine engine, CommandContext context, string[] args)
    {
        if (args.Length < 2 || args[1] != "check")
        {
            Console.Error.WriteLine("Usage: endo update check");
            return 1;
        }

        Console.WriteLine("Update checking is not implemented in this build; no update endpoint is configured.");
        return 0;
    }

    private static void ApplyOption(string[] args, string flag, string key, Dictionary<string, string> target)
    {
        var index = Array.IndexOf(args, flag);
        if (index >= 0 && index + 1 < args.Length)
        {
            target[key] = args[index + 1];
        }
    }

    private static void ApplyScopeOption(string[] args, Dictionary<string, string> target)
    {
        var index = Array.IndexOf(args, "--scope");
        if (index >= 0 && index + 1 < args.Length)
        {
            var parts = args[index + 1].Split('/', 2);
            target["scopeCategory"] = parts[0];
            if (parts.Length > 1) target["scopeSubCategory"] = parts[1];
        }
    }

    private static void PrintCommandResult(CommandResult result)
    {
        PrintResultLine(result.Success, string.IsNullOrEmpty(result.Output) ? (result.Error ?? "") : result.Output);
        if (!result.Success && !string.IsNullOrEmpty(result.Error) && !string.IsNullOrEmpty(result.Output))
        {
            Console.Error.WriteLine(result.Error);
        }
        foreach (var d in result.Diagnostics)
        {
            Console.WriteLine($"  {d}");
        }
        if (result.RecoveryInformation is not null)
        {
            Console.WriteLine($"  recovery: {result.RecoveryInformation}");
        }
    }

    private static void PrintResultLine(bool success, string message)
    {
        Console.WriteLine(success ? $"[ok] {message}" : $"[fail] {message}");
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"Unknown command '{verb}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Endo — managed development environment and orchestration system.

        Run 'endo help' for the full list of commands, or 'endo setup' to get started.
        """);
    }
}
