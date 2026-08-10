using Endo.Core.Environment;

namespace Endo.Core.Tests;

/// <summary>
/// Only tests the pure, side-effect-free parts of RootLocator. SaveRoot() writes to the real
/// per-user config directory by design (it is not a temp/test concern), so it is exercised via
/// manual smoke testing rather than here, to avoid an automated test suite mutating a developer's
/// real machine state.
/// </summary>
public sealed class RootLocatorTests
{
    [Fact]
    public void TryLocateRoot_HonorsEndoRootEnvironmentVariable()
    {
        var previous = System.Environment.GetEnvironmentVariable("ENDO_ROOT");
        try
        {
            System.Environment.SetEnvironmentVariable("ENDO_ROOT", "G:/SomeCustom/EndoRoot");

            var located = RootLocator.TryLocateRoot();

            Assert.Equal("G:/SomeCustom/EndoRoot", located);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("ENDO_ROOT", previous);
        }
    }

    [Fact]
    public void SuggestDefaultWorkspace_IsSiblingOfRoot()
    {
        var root = Path.Combine("G:", "Endo", "EndoRoot");
        var workspace = RootLocator.SuggestDefaultWorkspace(root);

        Assert.Equal(Path.Combine("G:", "Endo", "Projects"), workspace);
    }

    [Fact]
    public void SuggestDefaultRoot_IsNotNullOrEmpty()
    {
        var suggested = RootLocator.SuggestDefaultRoot();

        Assert.False(string.IsNullOrWhiteSpace(suggested));
    }
}
