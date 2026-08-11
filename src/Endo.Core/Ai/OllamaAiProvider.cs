using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Endo.Core.Ai;

/// <summary>
/// Local-model implementation of the provider-neutral interface, per 06-AI-SPEC.md's
/// local-first architecture goal ("The architecture should remain local-first so a local
/// provider can eventually become the primary option"). Talks to a local Ollama server over its
/// native HTTP API — no Anthropic SDK involved, since a local model doesn't speak that wire
/// format. The server itself is expected to already be running (see OllamaServerManager /
/// `endo ai serve`); this class only ever makes requests, it never launches anything.
/// </summary>
public sealed class OllamaAiProvider : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaAiProvider(string baseUrl, string model)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _model = model;
    }

    public string Name => "ollama";

    public async Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        // EnableWebSearch is a hint other providers may act on; a local model has no built-in
        // search tool to attach, so it's silently ignored here rather than failing the request.
        // ForceJsonOutput maps directly to Ollama's constrained-decoding "format": "json" — small
        // local models otherwise answer a JSON-only instruction in plain prose fairly often.
        var payload = new OllamaChatRequest(
            _model,
            [
                new OllamaChatMessage("system", request.SystemPrompt),
                new OllamaChatMessage("user", request.UserPrompt),
            ],
            Stream: false,
            Format: request.ForceJsonOutput ? "json" : null);

        HttpResponseMessage response;
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            response = await _http.PostAsync("/api/chat", content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new AiCompletionResponse(
                false, null,
                $"Could not reach the local Ollama server at '{_http.BaseAddress}': {ex.Message}. Run 'endo ai serve' first.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiCompletionResponse(false, null, $"Timed out waiting for the local Ollama server at '{_http.BaseAddress}'.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AiCompletionResponse(false, null, $"Ollama returned HTTP {(int)response.StatusCode}: {body}");
        }

        OllamaChatResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new AiCompletionResponse(false, null, $"Ollama response was not valid JSON: {ex.Message}");
        }

        var text = parsed?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AiCompletionResponse(false, null, "Ollama returned no message content.");
        }

        return new AiCompletionResponse(true, text, null);
    }

    private sealed record OllamaChatMessage(string Role, string Content);
    private sealed record OllamaChatRequest(string Model, List<OllamaChatMessage> Messages, bool Stream, string? Format = null);
    private sealed record OllamaChatResponseMessage(string? Role, string? Content);
    private sealed record OllamaChatResponse(OllamaChatResponseMessage? Message);
}
