using Endo.Core.Commands;

namespace Endo.Core.Tests;

public sealed class CommandResultTests
{
    [Fact]
    public void Ok_ProducesSuccessWithZeroExitCode()
    {
        var result = CommandResult.Ok("did the thing", affectedState: new[] { "projects.x" }, changedFiles: new[] { "a.txt" });

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("did the thing", result.Output);
        Assert.Contains("projects.x", result.AffectedState);
        Assert.Contains("a.txt", result.ChangedFiles);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_ProducesFailureWithNonZeroExitCodeAndError()
    {
        var result = CommandResult.Fail("something broke", diagnostics: new[] { "detail 1" });

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("something broke", result.Error);
        Assert.Contains("detail 1", result.Diagnostics);
    }
}

public sealed class CommandEngineTests
{
    private sealed class EchoCommand : ICommand
    {
        public string Name => "echo";
        public string Description => "Echoes an arg back.";
        public IReadOnlyList<string> Parameters => ["text"];
        public CommandResult Execute(CommandContext context, IReadOnlyDictionary<string, string> args) =>
            CommandResult.Ok(args.GetValueOrDefault("text", ""));
    }

    [Fact]
    public void Execute_UnknownCommand_FailsRatherThanInventingBehavior()
    {
        var engine = new CommandEngine();
        var logger = Diagnostics.Logger.CreateNullLogger();
        var context = new CommandContext
        {
            Root = Path.GetTempPath(),
            EnvironmentRepository = new Environment.EnvironmentRepository(Path.GetTempPath(), logger),
            Logger = logger,
        };

        var result = engine.Execute("does.not.exist", context, new Dictionary<string, string>());

        Assert.False(result.Success);
    }

    [Fact]
    public void ListCommands_ReflectsOnlyRegisteredCommands()
    {
        var engine = new CommandEngine();
        engine.Register(new EchoCommand());

        var commands = engine.ListCommands();

        Assert.Single(commands);
        Assert.Equal("echo", commands[0].Name);
    }

    [Fact]
    public void Execute_RegisteredCommand_Dispatches()
    {
        var engine = new CommandEngine();
        engine.Register(new EchoCommand());
        var logger = Diagnostics.Logger.CreateNullLogger();
        var context = new CommandContext
        {
            Root = Path.GetTempPath(),
            EnvironmentRepository = new Environment.EnvironmentRepository(Path.GetTempPath(), logger),
            Logger = logger,
        };

        var result = engine.Execute("echo", context, new Dictionary<string, string> { ["text"] = "hi" });

        Assert.True(result.Success);
        Assert.Equal("hi", result.Output);
    }
}
