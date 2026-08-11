using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Endo.Core.Ai;

/// <summary>
/// Third implementation of the provider-neutral interface. Unlike <see cref="AnthropicAiProvider"/>
/// (raw Anthropic SDK, needs an API key or `ant auth login`), this one shells out to the `claude`
/// CLI itself in headless print mode — reusing whatever session the user already has logged in
/// (Claude Pro/Max OAuth, keychain, etc.) with nothing extra to configure. Requested explicitly:
/// the user already runs `claude` interactively from PowerShell and wanted that same login reused
/// as an Endo AI provider, without going through `ant`.
///
/// Cost/latency note: `claude -p` is the full Claude Code harness, not a bare completion — every
/// call incurs its own baseline system-prompt overhead (~16k cache-creation tokens observed,
/// separate from and in addition to whatever Endo's own system prompt costs) before it ever sees
/// Endo's request. `--bare` mode would avoid a large chunk of that, but per `claude --help` it
/// only accepts `ANTHROPIC_API_KEY`/`apiKeyHelper` auth and never reads OAuth or the keychain —
/// which defeats the entire point of this provider (reusing the logged-in session) — so it is
/// deliberately not used here.
/// </summary>
public sealed class ClaudeCliAiProvider : IAiProvider
{
    private readonly string _executable;
    private readonly string? _model;

    public ClaudeCliAiProvider(string executable = "claude", string? model = null)
    {
        _executable = executable;
        _model = model;
    }

    public string Name => "claude-cli";

    public async Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(request.UserPrompt);
        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(request.SystemPrompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--no-session-persistence");
        // "" disables all built-in tools (a pure headless completion); when web search is wanted,
        // allow only the CLI's own WebSearch/WebFetch rather than the full tool set (no reason for
        // a command-routing request to have Bash/Edit/etc. available). Headless mode has no TTY to
        // approve a permission prompt, so an allowed tool still gets silently blocked
        // (permission_denials in the JSON output, confirmed by direct testing) unless permissions
        // are also bypassed — safe here specifically because --tools already restricts the
        // bypass to these two read-only tools, never Bash/Write/Edit.
        psi.ArgumentList.Add("--tools");
        psi.ArgumentList.Add(request.EnableWebSearch ? "WebSearch,WebFetch" : "");
        if (request.EnableWebSearch)
        {
            psi.ArgumentList.Add("--permission-mode");
            psi.ArgumentList.Add("bypassPermissions");
        }

        if (!string.IsNullOrWhiteSpace(_model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_model);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new AiCompletionResponse(
                false, null,
                $"Could not launch the 'claude' CLI ('{_executable}'): {ex.Message}. Is Claude Code installed and on PATH?");
        }

        // Close stdin immediately — otherwise the CLI waits ~3s deciding whether piped input is
        // coming before proceeding (observed directly: "no stdin data received in 3s").
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new AiCompletionResponse(false, null, $"'claude' CLI produced no output (exit {process.ExitCode}). {stderr.Trim()}");
        }

        ClaudeCliResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ClaudeCliResult>(stdout);
        }
        catch (JsonException ex)
        {
            return new AiCompletionResponse(false, null, $"'claude' CLI output was not valid JSON: {ex.Message}");
        }

        if (parsed is null)
        {
            return new AiCompletionResponse(false, null, "'claude' CLI returned an empty result.");
        }

        if (parsed.IsError)
        {
            return new AiCompletionResponse(false, null, parsed.Result ?? $"'claude' CLI reported an error (subtype: {parsed.Subtype}).");
        }

        if (string.IsNullOrWhiteSpace(parsed.Result))
        {
            return new AiCompletionResponse(false, null, "'claude' CLI returned no result text.");
        }

        return new AiCompletionResponse(true, parsed.Result, null);
    }

    private sealed class ClaudeCliResult
    {
        [JsonPropertyName("is_error")]
        public bool IsError { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("subtype")]
        public string? Subtype { get; set; }
    }
}
