using System.Text.Json;
using System.Text.Json.Serialization;
using Endo.Core.Ai;

namespace Endo.Core.Projects;

public sealed record ArchitectureDocsResult(bool Success, string Message, List<string> WrittenFiles);

/// <summary>
/// Turns a project's BOOTSTRAP.md spec into a set of architecture documents under
/// docs/Architecture/, using the Builder AI role — a free-form <see cref="IAiProvider.CompleteAsync"/>
/// call, never routed through AiOrchestrator's command-only constraint (that constraint exists for
/// operating Endo itself; writing prose documents isn't a command-dispatch task). Mirrors the same
/// spec-to-numbered-docs shape this very project was built from (01-ARCHITECTURE.md, 02-CLI-SPEC.md, ...).
/// </summary>
public static class ProjectBootstrapDocs
{
    public const string RelativeDirectory = "docs/Architecture";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ArchitectureDocsResult> GenerateAsync(
        IAiProvider builder, string projectRoot, string projectName, string bootstrapSpec, CancellationToken cancellationToken = default)
    {
        const string systemPrompt = """
            You are a software architect. Given a raw project specification, break it into a small
            set of focused architecture documents, the way a real engineering spec is organized:
            an overview, key components/data model, and workflows/acceptance criteria as
            appropriate. Use your judgment on how many documents the spec's scope actually
            justifies (typically 3-8) -- do not pad with filler documents for a small spec.
            Each document is Markdown, filenames numbered for reading order
            (e.g. "01-ARCHITECTURE.md", "02-COMPONENTS.md", ...).
            Respond with ONLY a JSON array, no prose, no markdown code fences:
            [{"filename": "01-ARCHITECTURE.md", "content": "# ..."}, ...]
            """;

        var userPrompt = $"Project: {projectName}\n\nSpec:\n{bootstrapSpec}";

        var response = await builder.CompleteAsync(
            new AiCompletionRequest(systemPrompt, userPrompt, MaxTokens: 8192, ForceJsonOutput: true),
            cancellationToken);

        if (!response.Available)
        {
            return new ArchitectureDocsResult(false, response.UnavailableReason ?? "Builder AI provider unavailable. Configure it via 'endo setup'.", []);
        }

        List<ArchitectureDoc>? docs;
        try
        {
            docs = JsonSerializer.Deserialize<List<ArchitectureDoc>>(ExtractJsonArray(response.Text ?? ""), JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ArchitectureDocsResult(false, $"Builder AI response could not be parsed as JSON: {ex.Message}", []);
        }

        if (docs is null || docs.Count == 0)
        {
            return new ArchitectureDocsResult(false, "Builder AI returned no architecture documents.", []);
        }

        var dir = Path.Combine(projectRoot, "docs", "Architecture");
        Directory.CreateDirectory(dir);

        var written = new List<string>();
        foreach (var doc in docs)
        {
            if (string.IsNullOrWhiteSpace(doc.Filename) || string.IsNullOrWhiteSpace(doc.Content))
            {
                continue;
            }

            // Guard against a model-generated filename containing path separators/traversal.
            var safeName = Path.GetFileName(doc.Filename);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                continue;
            }

            var path = Path.Combine(dir, safeName);
            File.WriteAllText(path, doc.Content);
            written.Add(path);
        }

        return written.Count > 0
            ? new ArchitectureDocsResult(true, $"Generated {written.Count} architecture document(s) in {RelativeDirectory}.", written)
            : new ArchitectureDocsResult(false, "Builder AI response parsed but contained no usable filename/content pairs.", []);
    }

    private sealed record ArchitectureDoc([property: JsonPropertyName("filename")] string? Filename, [property: JsonPropertyName("content")] string? Content);

    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
