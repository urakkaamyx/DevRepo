using Endo.Core.Ai;

namespace Endo.Core.Tests;

/// <summary>
/// AnthropicAiProvider wraps a live AnthropicClient with no injectable seam, so its
/// success/failure paths against the real API aren't unit-testable here without either a live
/// credential or a mocking layer this codebase doesn't otherwise need. Its honest-failure
/// contract (report Available=false rather than throwing or inventing a response) is exercised
/// manually via `endo ai ask` — see STATUS.md.
/// </summary>
public sealed class AnthropicAiProviderTests
{
    [Fact]
    public void Name_IsAnthropic()
    {
        var provider = new AnthropicAiProvider();

        Assert.Equal("anthropic", provider.Name);
    }
}
