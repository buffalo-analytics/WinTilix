using WinTilix.Terminal;
using Xunit;

namespace WinTilix.Tests;

public class ShellResolverTests
{
    [Theory]
    [InlineData("ssh example.com")]
    [InlineData("\"C:\\Windows\\System32\\OpenSSH\\ssh.exe\" user@example.com")]
    [InlineData("C:\\Windows\\System32\\OpenSSH\\SSH.EXE -p 2222 example.com")]
    public void Identifies_ssh_commands(string commandLine)
    {
        Assert.True(ShellResolver.IsSshCommand(commandLine));
    }

    [Theory]
    [InlineData("pwsh.exe -NoLogo")]
    [InlineData("\"C:\\Windows\\System32\\cmd.exe\"")]
    [InlineData("ssh-agent.exe")]
    public void Does_not_identify_other_shells_as_ssh(string commandLine)
    {
        Assert.False(ShellResolver.IsSshCommand(commandLine));
    }

    [Fact]
    public void Reads_selectable_hosts_from_config_and_includes()
    {
        var home = Path.Combine(Path.GetTempPath(), "WinTilix.Tests", Guid.NewGuid().ToString("N"));
        var ssh = Path.Combine(home, ".ssh");
        var includes = Path.Combine(ssh, "config.d");
        Directory.CreateDirectory(includes);

        try
        {
            File.WriteAllText(
                Path.Combine(ssh, "config"),
                """
                Include config.d/*.conf
                Host buffalo-web-1 buffalo-web-2
                    HostName 10.0.0.10
                Host * !blocked
                    ServerAliveInterval 30
                """);
            File.WriteAllText(
                Path.Combine(includes, "work.conf"),
                """
                Host production-db
                    HostName db.internal
                Host *.internal
                    User ops
                """);

            var hosts = SshConfigReader.ReadHosts(Path.Combine(ssh, "config"), home);

            Assert.Equal(
                ["buffalo-web-1", "buffalo-web-2", "production-db"],
                hosts);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
