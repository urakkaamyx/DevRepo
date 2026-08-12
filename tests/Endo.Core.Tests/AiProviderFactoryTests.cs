using System.Text.Json.Nodes;
using Endo.Core.Ai;
using Endo.Core.Environment;

namespace Endo.Core.Tests;

/// <summary>
/// Two independent AI roles: Orchestrator (Endo AI proper) and Builder (free-form generation,
/// e.g. BOOTSTRAP.md -> architecture docs). Orchestrator must keep reading the pre-role-split flat
/// ai.provider/ai.model so environments set up before this change don't lose their configuration.
/// </summary>
public sealed class AiProviderFactoryTests
{
    [Fact]
    public void Create_NestedOrchestratorSection_ResolvesCorrectProvider()
    {
        var state = new EnvironmentState();
        state.Ai["orchestrator"] = new JsonObject { ["provider"] = "claude-cli", ["model"] = "opus" };

        var provider = AiProviderFactory.Create(state);

        Assert.Equal("claude-cli", provider.Name);
    }

    [Fact]
    public void Create_LegacyFlatFields_StillResolveAsOrchestrator()
    {
        var state = new EnvironmentState();
        state.Ai["provider"] = "anthropic";

        var provider = AiProviderFactory.Create(state);

        Assert.Equal("anthropic", provider.Name);
    }

    [Fact]
    public void Create_NestedTakesPrecedenceOverLegacyFlat()
    {
        var state = new EnvironmentState();
        state.Ai["provider"] = "anthropic";
        state.Ai["orchestrator"] = new JsonObject { ["provider"] = "ollama", ["model"] = "llama3.2" };

        var provider = AiProviderFactory.Create(state);

        Assert.Equal("ollama", provider.Name);
    }

    [Fact]
    public async Task CreateBuilder_Unconfigured_ResolvesToNullProvider()
    {
        var state = new EnvironmentState();
        state.Ai["orchestrator"] = new JsonObject { ["provider"] = "claude-cli" };

        var builder = AiProviderFactory.CreateBuilder(state);

        Assert.False((await builder.CompleteAsync(new AiCompletionRequest("s", "u"))).Available);
    }

    [Fact]
    public void CreateBuilder_ConfiguredIndependentlyOfOrchestrator()
    {
        var state = new EnvironmentState();
        state.Ai["orchestrator"] = new JsonObject { ["provider"] = "claude-cli" };
        state.Ai["builder"] = new JsonObject { ["provider"] = "ollama", ["model"] = "codellama" };

        var orchestrator = AiProviderFactory.Create(state);
        var builder = AiProviderFactory.CreateBuilder(state);

        Assert.Equal("claude-cli", orchestrator.Name);
        Assert.Equal("ollama", builder.Name);
    }
}
