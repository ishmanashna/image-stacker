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
            try
            {
                _writer?.Dispose();
                _writer = null;
                _writer = OpenWriter(AppPaths.LogFile);
                _writer.WriteLine($"--- session {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} ---");
            }
            catch (Exception ex)
            {
                // Never let logging take down the app. Fall back to a pid-scoped file, then to null.
                try
                {
                    string fallback = Path.Combine(
                        AppPaths.LogDir,
                        $"app-{Environment.ProcessId}.log");
                    _writer = OpenWriter(fallback);
                    _writer.WriteLine($"--- session {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} (fallback) ---");
                    _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] Primary log unavailable: {ex.Message}");
                }
                catch
                {
                    _writer = null;
                }
            }
        }
    }

    private static StreamWriter OpenWriter(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        return new StreamWriter(stream) { AutoFlush = true };
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
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // Swallow write failures; logging must never crash the UI.
            }
        }
    }
}
