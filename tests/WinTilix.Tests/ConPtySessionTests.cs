using System.Diagnostics;
using Xunit;

namespace WinTilix.Tests;

public class ConPtySessionTests
{
    [Fact]
    public void Console_host_sees_cmd_echo_on_conpty()
    {
        var result = RunPtyCheck("echo");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("WIN_TILIX_PTY_OK", result.Output);
        Assert.Contains("PTY_CHECK_ECHO_OK", result.Output);
    }

    [Fact]
    public void Console_host_broadcasts_typed_input_to_second_session()
    {
        var result = RunPtyCheck("sync");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("PTY_CHECK_SYNC_OK", result.Output);
    }

    private static (int ExitCode, string Output) RunPtyCheck(string mode)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "WinTilix.PtyCheck.exe");
        Assert.True(File.Exists(exe), "WinTilix.PtyCheck.exe was not copied to the test output directory.");

        var start = new ProcessStartInfo(exe, mode)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start PtyCheck.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail("PtyCheck timed out.\n" + stdout + stderr);
        }

        return (process.ExitCode, stdout + stderr);
    }
}
