using Endo.Core.Json;

namespace Endo.Core.Tests;

public sealed class AtomicJsonWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicJsonWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "endo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed record Sample(string Name, int Count);

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var target = Path.Combine(_tempDir, "sample.json");
        AtomicJsonWriter.Write(target, new Sample("endo", 3));

        var read = AtomicJsonWriter.Read<Sample>(target);

        Assert.Equal("endo", read.Name);
        Assert.Equal(3, read.Count);
    }

    [Fact]
    public void Write_NoTempFileLeftBehindOnSuccess()
    {
        var target = Path.Combine(_tempDir, "sample.json");
        AtomicJsonWriter.Write(target, new Sample("endo", 1));

        var leftoverTempFiles = Directory.GetFiles(_tempDir, ".*.tmp");
        Assert.Empty(leftoverTempFiles);
    }

    [Fact]
    public void Write_OverwritingExistingFile_NeverLeavesTargetMissing()
    {
        var target = Path.Combine(_tempDir, "sample.json");
        AtomicJsonWriter.Write(target, new Sample("first", 1));
        AtomicJsonWriter.Write(target, new Sample("second", 2));

        Assert.True(File.Exists(target));
        var read = AtomicJsonWriter.Read<Sample>(target);
        Assert.Equal("second", read.Name);
    }

    [Fact]
    public void WriteRaw_InvalidJson_ThrowsAndDoesNotTouchExistingTarget()
    {
        var target = Path.Combine(_tempDir, "sample.json");
        AtomicJsonWriter.Write(target, new Sample("original", 1));

        Assert.ThrowsAny<Exception>(() => AtomicJsonWriter.WriteRaw(target, "{ not valid json"));

        // The crash-safety guarantee: an invalid write must never corrupt or remove the existing target.
        var stillThere = AtomicJsonWriter.Read<Sample>(target);
        Assert.Equal("original", stillThere.Name);
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.json");
        var success = AtomicJsonWriter.TryRead<Sample>(missing, out var value);

        Assert.False(success);
        Assert.Null(value);
    }
}
