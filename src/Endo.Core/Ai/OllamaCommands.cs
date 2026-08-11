using Endo.Core.Commands;
using Endo.Core.Tools;

namespace Endo.Core.Ai;

/// <summary>Shared lookup: resolves the workspace-installed Ollama tool into an OllamaServerManager, or a clear reason it can't yet.</summary>
internal static class OllamaLookup
{
    public static (OllamaServerManager? Manager, string? Error) Resolve(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var state = context.Environment ??= context.EnvironmentRepository.Load();

        if (!state.Tools.General.TryGetValue("Ollama", out var manifest))
        {
            return (null, "Ollama is not installed in this workspace. Run 'endo tool install Ollama --release <url> --version <v> --validate \"ollama.exe --version\"' first.");
        }

        var version = manifest.Channels.GetValueOrDefault("latest") ?? manifest.Versions.Keys.FirstOrDefault();
        if (version is null || !manifest.Versions.TryGetValue(version, out var versionInfo))
        {
            return (null, "Ollama is registered but has no installed version on record.");
        }

        var exePath = Path.Combine(versionInfo.Path, "ollama.exe");
        var modelsDirectory = Path.Combine(state.Paths.Tools, "General", "Ollama", "models");

        args.TryGetValue("baseUrl", out var baseUrl);
        baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl;

        var logFilePath = Path.Combine(state.Paths.Logs, "ollama-serve.log");

        return (new OllamaServerManager(exePath, modelsDirectory, baseUrl, logFilePath), null);
    }
}

/// <summary>endo ai serve — starts the workspace-managed Ollama server if it isn't already responding.</summary>
public sealed class OllamaServeCommand : ICommand
{
    public string Name => "ollama.serve";
    public string Description => "Start the workspace-installed Ollama server (no-op if one is already responding at the configured address).";
    public IReadOnlyList<string> Parameters => ["baseUrl"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        var (manager, error) = OllamaLookup.Resolve(context, args);
        if (manager is null)
        {
            return CommandResult.Fail(error!);
        }

        var result = manager.EnsureServerRunningAsync().GetAwaiter().GetResult();
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}

/// <summary>endo ai serve --model &lt;name&gt; — pulls a model into the workspace-managed Ollama server.</summary>
public sealed class OllamaPullCommand : ICommand
{
    public string Name => "ollama.pull";
    public string Description => "Pull a model into the workspace-installed Ollama server. The server must already be running.";
    public IReadOnlyList<string> Parameters => ["model", "baseUrl"];

    public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("model", out var model) || string.IsNullOrWhiteSpace(model))
        {
            return CommandResult.Fail("ollama.pull requires a 'model' argument.");
        }

        var (manager, error) = OllamaLookup.Resolve(context, args);
        if (manager is null)
        {
            return CommandResult.Fail(error!);
        }

        var result = manager.PullModel(model);
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
