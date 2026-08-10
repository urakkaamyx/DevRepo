using System.Text.Json;

namespace Endo.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Structured logger. Writes newline-delimited JSON to a log file under the
/// Endo managed root (cache/logs/endo.log) and mirrors human-readable lines to the console
/// at Info level and above.
/// </summary>
public sealed class Logger
{
    private readonly string? _logFilePath;
    private readonly object _writeLock = new();

    public Logger(string? logFilePath)
    {
        _logFilePath = logFilePath;
        if (_logFilePath is not null)
        {
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    public static Logger CreateNullLogger() => new Logger(null);

    public void Debug(string message, object? data = null) => Write(LogLevel.Debug, message, data);
    public void Info(string message, object? data = null) => Write(LogLevel.Info, message, data);
    public void Warn(string message, object? data = null) => Write(LogLevel.Warn, message, data);
    public void Error(string message, object? data = null) => Write(LogLevel.Error, message, data);

    private void Write(LogLevel level, string message, object? data)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, level.ToString(), message, data);

        if (_logFilePath is not null)
        {
            lock (_writeLock)
            {
                var line = JsonSerializer.Serialize(entry);
                File.AppendAllText(_logFilePath, line + System.Environment.NewLine);
            }
        }

        if (level >= LogLevel.Info)
        {
            var prefix = level switch
            {
                LogLevel.Warn => "warn",
                LogLevel.Error => "error",
                _ => "info"
            };
            var writer = level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine($"[{prefix}] {message}");
        }
    }
}

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message, object? Data);
