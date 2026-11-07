namespace MyFlow.Core;

/// <summary>
/// Tiny file + console logger. Logs go to ~/Library/Application Support/MyFlow/logs/myflow.log.
/// Transcripts are only logged at Debug level so they don't end up in logs by default.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _file;

    public static bool DebugEnabled { get; set; } =
        Environment.GetEnvironmentVariable("MYFLOW_DEBUG") == "1";

    public static void Init(string? directory = null)
    {
        try
        {
            var dir = directory ?? AppPaths.LogsDirectory;
            Directory.CreateDirectory(dir);
            _file = Path.Combine(dir, "myflow.log");
            // Keep the log from growing forever.
            if (File.Exists(_file) && new FileInfo(_file).Length > 2_000_000)
            {
                File.Move(_file, _file + ".old", overwrite: true);
            }
        }
        catch
        {
            _file = null;
        }
    }

    public static void Debug(string message)
    {
        if (DebugEnabled) Write("DEBUG", message);
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            Console.Error.WriteLine(line);
            if (_file != null)
            {
                try { File.AppendAllText(_file, line + Environment.NewLine); } catch { /* logging must never crash */ }
            }
        }
    }
}
