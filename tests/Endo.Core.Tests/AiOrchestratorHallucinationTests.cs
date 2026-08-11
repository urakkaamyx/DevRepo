using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Core.Diagnostics;
using Endo.Core.Environment;

namespace Endo.Core.Tests;

/// <summary>
/// Phase 10 hardening: 06-AI-SPEC.md "No Invented State" and 01-ARCHITECTURE.md's rule that AI
/// must never become a second, hidden implementation of Endo. These tests use a stub provider so
/// the "AI invents a command" scenario is fully deterministic and doesn't depend on live model
/// behavior — proving the refusal is enforced by AiOrchestrator itself, not by the model behaving.
/// </summary>
public sealed class AiOrchestratorHallucinationTests
{
    private sealed class StubProvider : IAiProvider
    {
        private readonly string _jsonResponse;
        public StubProvider(string jsonResponse) => _jsonResponse = jsonResponse;

        public string Name => "stub";

        public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiCompletionResponse(true, _jsonResponse, null));
    }

    private sealed class EchoCommand : ICommand
    {
        public string Name => "echo";
        public string Description => "Echoes an arg back.";
        public IReadOnlyList<string> Parameters => ["text"];
        public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args) =>
            CommandResult.Ok(args.GetValueOrDefault("text", ""));
    }

    private static CommandContext BuildContext()
    {
        var logger = Logger.CreateNullLogger();
        return new CommandContext
        {
            Root = Path.GetTempPath(),
            EnvironmentRepository = new EnvironmentRepository(Path.GetTempPath(), logger),
            Logger = logger,
        };
    }

    [Fact]
    public async Task AskAsync_ProviderProposesUnregisteredCommand_IsRefusedNotExecuted()
    {
        var engine = new CommandEngine();
        engine.Register(new EchoCommand());
        var provider = new StubProvider("""{"command": "project.deleteEverything", "args": {}, "clarification": null}""");
        var orchestrator = new AiOrchestrator(provider, engine);

        var result = await orchestrator.AskAsync("do something dangerous", BuildContext());

        Assert.False(result.Success);
        Assert.Null(result.CommandResult); // nothing was ever executed
        Assert.Equal("project.deleteEverything", result.ChosenCommand);
        Assert.Contains("unknown command", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskAsync_ProviderProposesRealCommand_ExecutesThroughTheRealCommandEngine()
    {
        var engine = new CommandEngine();
        engine.Register(new EchoCommand());
        var provider = new StubProvider("""{"command": "echo", "args": {"text": "hello"}, "clarification": null}""");
        var orchestrator = new AiOrchestrator(provider, engine);

        var result = await orchestrator.AskAsync("say hello", BuildContext());

        Assert.True(result.Success);
        Assert.NotNull(result.CommandResult);
        Assert.Equal("hello", result.CommandResult!.Output);
    }

    [Fact]
    public async Task AskAsync_ProviderReturnsGarbage_FailsCleanlyRatherThanThrowing()
    {
        var engine = new CommandEngine();
        var provider = new StubProvider("this is not json at all");
        var orchestrator = new AiOrchestrator(provider, engine);

        var result = await orchestrator.AskAsync("anything", BuildContext());

        Assert.False(result.Success);
        Assert.Null(result.CommandResult);
    }
}
