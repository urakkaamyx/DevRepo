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

internal static class Program
{
    private static int Main(string[] args)
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

        var engine = BuildCommandEngine(root, environmentRepository);
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

    private static CommandEngine BuildCommandEngine(string root, EnvironmentRepository environmentRepository)
    {
        var engine = new CommandEngine();

        var projectService = new ProjectService();
        engine.Register(new ProjectNewCommand(projectService));
        engine.Register(new ProjectCheckCommand(projectService));
        engine.Register(new ProjectOpenCommand(projectService));

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

        var restoreService = new RestoreService(environmentRepository);
        engine.Register(new RestoreCommand(restoreService));

        engine.Register(new SetupCommand(new SetupService()));

        return engine;
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
        var engine = BuildCommandEngine(root, environmentRepository);
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
                if (args.Length >= 5)
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
                }

                var result = engine.Execute("project.new", context, new Dictionary<string, string>
                {
                    ["category"] = category,
                    ["subCategory"] = subCategory,
                    ["name"] = name,
                });
                PrintCommandResult(result);
                return result.Success ? 0 : 1;
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
                if (args.Length < 3) { Console.Error.WriteLine("Usage: endo tool install <name> --repo <url> [--ref <ref>] [--version <v>] [--scope Category/SubCategory] [--build <cmd>] [--validate <cmd>]"); return 1; }
                var cmdArgs = new Dictionary<string, string> { ["name"] = args[2] };
                ApplyScopeOption(args, cmdArgs);
                ApplyOption(args, "--repo", "repository", cmdArgs);
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
        if (args.Length < 3 || args[1] != "ask")
        {
            Console.Error.WriteLine("Usage: endo ai ask \"<request>\"");
            return 1;
        }

        var orchestrator = new AiOrchestrator(new AnthropicAiProvider(), engine);
        var result = await orchestrator.AskAsync(args[2], context);

        Console.WriteLine(result.Message);
        return result.Success ? 0 : 1;
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

        Usage:
          endo setup [--restore all|projects]
          endo project new [<Category> <SubCategory> <ProjectName>]
          endo project check <Category/SubCategory/Name>
          endo project open <Category/SubCategory/Name> [--ide <ide>]
          endo tool list
          endo tool info <name> [--scope Category/SubCategory]
          endo tool install <name> --repo <url> [--ref <ref>] [--version <v>] [--scope Category/SubCategory] [--build <cmd>] [--validate <cmd>]
          endo tool remove <name> [--scope Category/SubCategory] [--version v] [--force]
          endo runtime list
          endo runtime install <name> <version> --path <path>
          endo runtime set <runtime> --project <key> [--version v]
          endo devrepo checkpoint [--message <msg>]
          endo ai ask "<request>"
          endo update check
        """);
    }
}
