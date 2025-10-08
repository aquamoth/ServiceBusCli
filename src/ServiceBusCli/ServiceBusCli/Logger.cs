using System;
using System.IO;
using System.Text;
using System.Threading;
using Serilog;

namespace ServiceBusCli;

internal static class Logger
{
    private static readonly object _gate = new();
    private static string? _path;
    private static bool _initialized;

    public static string? Initialize(string? explicitPath = null)
    {
        if (_initialized) return _path;
        lock (_gate)
        {
            if (_initialized) return _path;
            try
            {
                _path = !string.IsNullOrWhiteSpace(explicitPath)
                    ? explicitPath
                    : Environment.GetEnvironmentVariable("SERVICEBUSCLI_LOG_FILE");
                if (string.IsNullOrWhiteSpace(_path))
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var baseDir = Path.Combine(string.IsNullOrWhiteSpace(home) ? "." : home, ".servicebuscli", "logs");
                    Directory.CreateDirectory(baseDir);
                    _path = Path.Combine(baseDir, $"sbcli_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                }
                // Touch file
                File.AppendAllText(_path!, $"# ServiceBusCli log started {DateTime.UtcNow:u}{Environment.NewLine}");
            }
            catch { _path = null; }
            _initialized = true;
            return _path;
        }
    }

    public static void Info(string msg)
    {
        try { Log.Information("{Msg}", msg); } catch { Write("INF", msg); }
    }

    public static void Error(string msg)
    {
        try { Log.Error("{Msg}", msg); } catch { Write("ERR", msg); }
    }

    private static void Write(string level, string msg)
    {
        if (!_initialized) Initialize();
        if (string.IsNullOrWhiteSpace(_path)) return;
        var line = new StringBuilder()
            .Append(DateTime.UtcNow.ToString("u"))
            .Append(' ')
            .Append(level)
            .Append(' ')
            .Append(msg)
            .Append(Environment.NewLine)
            .ToString();
        try
        {
            lock (_gate) File.AppendAllText(_path!, line);
        }
        catch { }
    }
}

