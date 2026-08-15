using System.IO;

namespace ImageStacker.App.Services;

internal static class FileLogger
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static void Initialize()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = new StreamWriter(AppPaths.LogFile, append: true) { AutoFlush = true };
            _writer.WriteLine($"--- session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        string text = ex is null ? message : $"{message}: {ex}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            _writer?.WriteLine(line);
        }
    }
}
