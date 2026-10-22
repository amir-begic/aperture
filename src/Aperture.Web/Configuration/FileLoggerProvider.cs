namespace Aperture.Web.Configuration;

/// <summary>
/// Appends log lines to a file. Only for Windows, where Aperture runs without a console; launchd and systemd write the
/// console output to the log themselves. The usual Logging filters apply.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public FileLoggerProvider(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Shared, so the updater can read the end of it when a new version doesn't start.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => _writer.Dispose();

    private void Write(string line)
    {
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {logLevel,-11} {category}: {formatter(state, exception)}";
            provider.Write(exception is null ? line : line + Environment.NewLine + exception);
        }
    }
}
