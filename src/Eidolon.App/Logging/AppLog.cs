using System.IO;
using System.Text;

namespace Eidolon.App.Logging;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    None = 5
}

public static class AppLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static LogLevel _min = LogLevel.Info;
    private static string _logDir = "";
    private static string _logFile = "";
    private static bool _debugMode;
    private static bool _enforceSizeCap;

    /// <summary>Total retained log bytes under the default AppData log directory.</summary>
    public const long DefaultMaxTotalLogBytes = 1 * 1024 * 1024;

    public static LogLevel MinimumLevel => _min;
    public static string LogDirectory => _logDir;
    public static string LogFilePath => _logFile;
    public static bool IsInitialized => _writer != null;
    public static bool IsDebugMode => _debugMode;

    public static void Initialize(string[] args, string? baseDirectory = null)
    {
        lock (Gate)
        {
            _debugMode = HasFlag(args, "--debug");
            _min = ParseLevel(args);
            if (_debugMode && _min > LogLevel.Debug)
                _min = LogLevel.Debug;

            var root = baseDirectory ?? Directory.GetCurrentDirectory();
            if (_debugMode)
            {
                // Debug sessions write under cwd/Logs with no size cap.
                _logDir = Path.Combine(root, "Logs");
                _enforceSizeCap = false;
            }
            else
            {
                // Default: %APPDATA%/Eidolon/ with total log size kept under 1MB.
                _logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Eidolon");
                _enforceSizeCap = true;
            }

            Directory.CreateDirectory(_logDir);
            if (_enforceSizeCap)
                EnforceTotalSizeCap(_logDir, DefaultMaxTotalLogBytes);

            var name = $"eidolon_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            _logFile = Path.Combine(_logDir, name);
            _writer?.Dispose();
            _writer = new StreamWriter(new FileStream(_logFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
            {
                AutoFlush = true
            };
            Write(LogLevel.Info, "AppLog", $"Logging started. level={_min} debug={_debugMode} file={_logFile}");
            Write(LogLevel.Debug, "AppLog", $"cwd={Directory.GetCurrentDirectory()}");
            Write(LogLevel.Debug, "AppLog", $"args=[{string.Join(' ', args)}]");
        }
    }

    public static bool HasFlag(string[] args, string flag)
    {
        foreach (var a in args)
        {
            if (a.Equals(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static LogLevel ParseLevel(string[] args)
    {
        // --log-level=Debug | --log-level Debug | -log Debug | /log:Info
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--log-level=", StringComparison.OrdinalIgnoreCase))
                return ParseOne(a.Substring("--log-level=".Length));
            if (a.StartsWith("/log:", StringComparison.OrdinalIgnoreCase))
                return ParseOne(a.Substring(5));
            if (a.Equals("--log-level", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-log", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--log", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    return ParseOne(args[i + 1]);
            }
        }
        var env = Environment.GetEnvironmentVariable("EIDOLON_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(env))
            return ParseOne(env);
        return LogLevel.Info;
    }

    private static LogLevel ParseOne(string s)
    {
        s = s.Trim().Trim('"', '\'');
        if (Enum.TryParse<LogLevel>(s, true, out var lv))
            return lv;
        return s.ToLowerInvariant() switch
        {
            "verbose" or "trace" => LogLevel.Trace,
            "dbg" or "debug" => LogLevel.Debug,
            "information" or "info" => LogLevel.Info,
            "warning" or "warn" => LogLevel.Warn,
            "err" or "error" => LogLevel.Error,
            "off" or "none" or "silent" => LogLevel.None,
            _ => LogLevel.Info
        };
    }

    /// <summary>
    /// Keep total size of eidolon_*.log under <paramref name="maxTotalBytes"/> by deleting oldest files.
    /// Leaves room for a new session file.
    /// </summary>
    public static void EnforceTotalSizeCap(string logDir, long maxTotalBytes)
    {
        try
        {
            if (!Directory.Exists(logDir)) return;
            // Leave headroom so a new session can start writing.
            long budget = Math.Max(0, maxTotalBytes - 64 * 1024);
            var files = Directory.EnumerateFiles(logDir, "eidolon_*.log")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTimeUtc)
                .ThenBy(f => f.LastWriteTimeUtc)
                .ToList();
            long total = files.Sum(f => f.Length);
            foreach (var fi in files)
            {
                if (total <= budget) break;
                try
                {
                    long len = fi.Length;
                    fi.Delete();
                    total -= len;
                }
                catch
                {
                    // ignore locked/missing files
                }
            }
        }
        catch
        {
            // ignore retention errors
        }
    }

    public static void Trace(string message, string source = "App") => Write(LogLevel.Trace, source, message);
    public static void Debug(string message, string source = "App") => Write(LogLevel.Debug, source, message);
    public static void Info(string message, string source = "App") => Write(LogLevel.Info, source, message);
    public static void Warn(string message, string source = "App") => Write(LogLevel.Warn, source, message);
    public static void Error(string message, string source = "App") => Write(LogLevel.Error, source, message);

    public static void Error(Exception ex, string message = "", string source = "App")
    {
        var msg = string.IsNullOrEmpty(message) ? ex.ToString() : message + " | " + ex;
        Write(LogLevel.Error, source, msg);
    }

    public static void Write(LogLevel level, string source, string message)
    {
        if (level < _min || level == LogLevel.None || _min == LogLevel.None)
            return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] [{source}] {message}";
        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // ignore IO errors
            }
        }
        try
        {
            System.Diagnostics.Debug.WriteLine(line);
            if (level >= LogLevel.Warn)
                Console.Error.WriteLine(line);
            else if (level >= LogLevel.Info)
                Console.WriteLine(line);
        }
        catch { /* ignore */ }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            try
            {
                Write(LogLevel.Info, "AppLog", "Logging stopped.");
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { /* ignore */ }
            _writer = null;

            if (_enforceSizeCap && !string.IsNullOrEmpty(_logDir))
                EnforceTotalSizeCap(_logDir, DefaultMaxTotalLogBytes);
        }
    }
}
