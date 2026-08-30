using WinTilix.Diagnostics;
using Xunit;

namespace WinTilix.Tests;

public class DebugLogTests
{
    private static readonly object Gate = new();

    [Fact]
    public void WriteReport_IncludesVersionExceptionAndLogLines()
    {
        lock (Gate)
        {
            var directory = Path.Combine(Path.GetTempPath(), "WinTilix.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            DebugLog.ConfigureDirectory(directory);
            try
            {
                DebugLog.Info("pane started");
                DebugLog.Error("session failed", new InvalidOperationException("pty host missing"));
                var path = DebugLog.WriteReport(new Version(0, 1, 6), new Exception("boom"), "manual");

                var text = File.ReadAllText(path);
                Assert.Contains("Version: 0.1.6", text);
                Assert.Contains("boom", text);
                Assert.Contains("pane started", text);
                Assert.Contains("pty host missing", text);
                Assert.StartsWith(DebugLog.ReportDirectory, path);
            }
            finally
            {
                DebugLog.ConfigureDirectory(null);
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateGitHubIssueUri_PointsAtWinTilixIssues()
    {
        var uri = DebugLog.CreateGitHubIssueUri(@"C:\Users\david\AppData\Local\WinTilix\debug-reports\report.txt");
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.Contains("/buffalo-analytics/WinTilix/issues/new", uri.AbsolutePath);
        Assert.Contains("report.txt", uri.Query);
        Assert.DoesNotContain("id_ed25519", uri.Query);
    }
}
