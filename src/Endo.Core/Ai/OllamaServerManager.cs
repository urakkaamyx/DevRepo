using System.Diagnostics;

namespace Endo.Core.Ai;

public sealed record OllamaServeResult(bool Success, string Message, bool AlreadyRunning);
public sealed record OllamaPullResult(bool Success, string Message);

/// <summary>
/// Manages the lifecycle of the workspace-managed Ollama server process: starts it if nothing is
/// already listening at the configured address, and pulls models into it. Talks only to the
/// Ollama instance Endo itself installed via <c>tool.install --release</c> — never a machine-wide
/// install — so everything (binary, models) lives inside the Endo-managed workspace as requested,
/// not in whatever global location a system-wide Ollama install would use.
/// </summary>
public sealed class OllamaServerManager
{
    private readonly string _executablePath;
    private readonly string _modelsDirectory;
    private readonly string _baseUrl;
    private readonly string _logFilePath;

    public OllamaServerManager(string executablePath, string modelsDirectory, string baseUrl, string logFilePath)
    {
        _executablePath = executablePath;
        _modelsDirectory = modelsDirectory;
        _baseUrl = baseUrl;
        _logFilePath = logFilePath;
    }

    public async Task<OllamaServeResult> EnsureServerRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await IsServerRespondingAsync(cancellationToken))
        {
            return new OllamaServeResult(true, $"Ollama server already responding at {_baseUrl}.", AlreadyRunning: true);
        }

        if (!File.Exists(_executablePath))
        {
            return new OllamaServeResult(
                false,
                $"Ollama executable not found at '{_executablePath}'. Install it first with 'endo tool install Ollama --release <url> --version <v>'.",
                false);
        }

        Directory.CreateDirectory(_modelsDirectory);
        var logDirectory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var uri = new Uri(_baseUrl);
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add("serve");
        psi.Environment["OLLAMA_MODELS"] = _modelsDirectory;
        psi.Environment["OLLAMA_HOST"] = $"{uri.Host}:{uri.Port}";

        Process process;
        try
        {
            process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => AppendLog(e.Data);
            process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return new OllamaServeResult(false, $"Failed to start 'ollama serve': {ex.Message}", false);
        }

        // Poll for readiness rather than assuming a fixed startup delay.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (await IsServerRespondingAsync(cancellationToken))
            {
                return new OllamaServeResult(true, $"Started Ollama server (pid {process.Id}); models directory '{_modelsDirectory}'.", AlreadyRunning: false);
            }

            if (process.HasExited)
            {
                return new OllamaServeResult(false, $"'ollama serve' exited immediately (code {process.ExitCode}). See '{_logFilePath}'.", false);
            }

            await Task.Delay(500, cancellationToken);
        }

        return new OllamaServeResult(false, $"Ollama server did not become ready within 15s. See '{_logFilePath}'.", false);
    }

    public OllamaPullResult PullModel(string model)
    {
        if (!File.Exists(_executablePath))
        {
            return new OllamaPullResult(false, $"Ollama executable not found at '{_executablePath}'.");
        }

        var uri = new Uri(_baseUrl);
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
            UseShellExecute = false,
            // Deliberately not redirected: `ollama pull` renders a live progress bar via carriage
            // returns that only makes sense streamed straight to the real console, and this
            // command already runs synchronously to completion either way.
        };
        psi.ArgumentList.Add("pull");
        psi.ArgumentList.Add(model);
        psi.Environment["OLLAMA_HOST"] = $"{uri.Host}:{uri.Port}";

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new OllamaPullResult(false, $"Failed to start 'ollama pull {model}'.");
        }

        process.WaitForExit();
        return process.ExitCode == 0
            ? new OllamaPullResult(true, $"Pulled model '{model}'.")
            : new OllamaPullResult(false, $"'ollama pull {model}' exited with code {process.ExitCode}.");
    }

    private async Task<bool> IsServerRespondingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync(_baseUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void AppendLog(string? line)
    {
        if (line is null)
        {
            return;
        }

        try { File.AppendAllText(_logFilePath, line + System.Environment.NewLine); }
        catch { /* best-effort; a lost log line must never take down the server */ }
    }
}
