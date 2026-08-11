using Endo.Core.Ai;

namespace Endo.Core.Tests;

/// <summary>
/// ClaudeCliAiProvider shells out to the real `claude` binary with no injectable seam, so its
/// success/failure paths aren't unit-testable here without either the CLI installed and logged in
/// or a mocking layer this codebase doesn't otherwise need. It was instead verified manually,
/// live, end-to-end — both `endo ai ask` and `endo ai discover` (including real web search) — see
/// STATUS.md.
/// </summary>
public sealed class ClaudeCliAiProviderTests
{
    [Fact]
    public void Name_IsClaudeCli()
    {
        var provider = new ClaudeCliAiProvider();

        Assert.Equal("claude-cli", provider.Name);
    }

    [Fact]
    public async Task CompleteAsync_ExecutableNotOnPath_ReportsUnavailableRatherThanThrowing()
    {
        var provider = new ClaudeCliAiProvider(executable: "this-binary-does-not-exist-anywhere");

        var response = await provider.CompleteAsync(new AiCompletionRequest("system", "user"));

        Assert.False(response.Available);
        Assert.Null(response.Text);
        Assert.NotNull(response.UnavailableReason);
    }
}
