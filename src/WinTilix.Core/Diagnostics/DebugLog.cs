using System.Collections.Concurrent;

namespace WinTilix.Diagnostics;

public static class DebugLog
{
    private const int MemoryCap = 300;
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<string> Recent = new();
    private static string? _directoryOverride;

    public static string DirectoryPath
    {
        get
        {
            if (_directoryOverride is not null)
            {
                return _directoryOverride;
            }

            var root = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "WinTilix");
        }
    }

    public static string LogPath => Path.Combine(DirectoryPath, "debug.log");

    public static string ReportDirectory => Path.Combine(DirectoryPath, "debug-reports");

    public static void ConfigureDirectory(string? directory)
    {
        _directoryOverride = directory;
        while (Recent.TryDequeue(out _))
        {
        }
    }

    public static void Info(string message) => Write("info", message);

    public static void Warn(string message) => Write("warn", message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("error", exception is null ? message : $"{message}: {exception}");
    }

    public static IReadOnlyList<string> Snapshot() => Recent.ToArray();

    public static string WriteReport(Version version, Exception? exception = null, string? notes = null)
    {
        Directory.CreateDirectory(ReportDirectory);
        var path = Path.Combine(
            ReportDirectory,
            $"wintilix-debug-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, FormatReport(version, exception, notes));
        return path;
    }

    public static string FormatReport(Version version, Exception? exception = null, string? notes = null)
    {
        var lines = new List<string>
        {
            "WinTilix debug report",
            $"GeneratedUtc: {DateTime.UtcNow:O}",
            $"Version: {version}",
            $"OS: {Environment.OSVersion}",
            $"64BitProcess: {Environment.Is64BitProcess}",
            $"Runtime: {Environment.Version}",
            $"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"BaseDirectory: {AppContext.BaseDirectory}",
        };

        if (!string.IsNullOrWhiteSpace(notes))
        {
            lines.Add("Notes: " + notes.Trim());
        }

        if (exception is not null)
        {
            lines.Add("Exception:");
            lines.Add(exception.ToString());
        }

        lines.Add("Recent log:");
        var snapshot = Snapshot();
        if (snapshot.Count == 0)
        {
            lines.Add("(empty)");
        }
        else
        {
            lines.AddRange(snapshot);
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static Uri CreateGitHubIssueUri(string reportPath)
    {
        const string owner = "buffalo-analytics";
        const string repo = "WinTilix";
        var title = "WinTilix debug report";
        var body =
            """
            ## What happened


            ## Steps to reproduce
            1.
            2.

            ## Debug report
            Please attach the generated report file (it is already on this PC):

            """ + reportPath + """


            Do not paste SSH private keys or passwords.
            """;

        return new Uri(
            $"https://github.com/{owner}/{repo}/issues/new?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}");
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {message}";
        Recent.Enqueue(line);
        while (Recent.Count > MemoryCap && Recent.TryDequeue(out _))
        {
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
