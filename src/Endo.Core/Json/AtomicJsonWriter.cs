using System.Text.Json;

namespace Endo.Core.Json;

/// <summary>
/// Crash-safe JSON writes: write to a temp file in the same directory, validate it parses,
/// flush to disk, then atomically replace the target file. A process interruption at any point
/// must never leave a partially written target file.
/// </summary>
public static class AtomicJsonWriter
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static void Write<T>(string targetPath, T value, JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;
        var json = JsonSerializer.Serialize(value, options);
        WriteRaw(targetPath, json);
    }

    /// <summary>
    /// Writes raw pre-serialized JSON text atomically. Validates that it parses before committing.
    /// </summary>
    public static void WriteRaw(string targetPath, string json)
    {
        // Validate before ever touching disk state.
        using (JsonDocument.Parse(json))
        {
            // Parse-only validation; discard.
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException($"Cannot determine directory for target path '{targetPath}'.");
        }

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // Re-validate the bytes actually landed on disk correctly.
            using (JsonDocument.Parse(File.ReadAllText(tempPath)))
            {
            }

            // Atomic replace of the existing file, per 03-ENVIRONMENT-SPEC.md "Safe Writes" steps 1-4.
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    public static T Read<T>(string path, JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, options)
            ?? throw new InvalidDataException($"File '{path}' deserialized to null.");
    }

    public static bool TryRead<T>(string path, out T? value, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(path))
        {
            value = default;
            return false;
        }

        try
        {
            value = Read<T>(path, options);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
