using WinTilix.Terminal;
using Xunit;

namespace WinTilix.Tests;

public class SshConfigParserTests
{
    [Fact]
    public void RoundTrip_PreservesCommentsAndUnknownDirectives()
    {
        var original =
            """
            Include config.d/*.conf

            # production web
            Host buffalo-web1
                HostName 10.0.0.10
                User deploy
                ForwardAgent yes
                # keep this

            Match host *.internal
                User ops
            """;

        var document = SshConfigParser.Parse("config", original);
        var serialized = document.Serialize();
        var again = SshConfigParser.Parse("config", serialized);

        Assert.Single(again.HostSections);
        Assert.Equal("buffalo-web1", again.HostSections.First().Aliases.Single());
        Assert.Contains(again.Sections, section => section is SshMatchSection);
        Assert.Contains("ForwardAgent yes", serialized);
        Assert.Contains("# production web", serialized);
    }

    [Fact]
    public void ApplyHost_UpdatesKnownFieldsWithoutTouchingPreamble()
    {
        var document = SshConfigParser.Parse(
            "config",
            """
            Include other.conf

            Host old
                HostName old.example
                User root
                Ciphers aes256-ctr
            """);
        var section = document.HostSections.Single();
        var edit = SshConfigParser.ReadHost(section);
        edit.Alias = "new-host";
        edit.HostName = "new.example";
        edit.User = "deploy";
        edit.Port = 2222;
        edit.AdvancedText = "Ciphers aes256-ctr";

        var validation = SshConfigParser.Validate(edit, document, section);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        SshConfigParser.ApplyHost(document, section, edit);

        var text = document.Serialize();
        Assert.Contains("Include other.conf", text);
        Assert.Contains("Host new-host", text);
        Assert.Contains("HostName new.example", text);
        Assert.Contains("Port 2222", text);
        Assert.Contains("Ciphers aes256-ctr", text);
        Assert.DoesNotContain("Host old", text);
    }

    [Fact]
    public void Validate_RejectsWildcardAliasAndBadPort()
    {
        var document = SshConfigParser.Parse("config", "Host existing\n");
        var edit = new SshHostEdit
        {
            Alias = "*.web",
            Port = 70000,
        };

        var result = SshConfigParser.Validate(edit, document, null);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("wildcards", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Port", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RoundTrip_IsNoOpForCommentsInlineCommentsAndMatch()
    {
        var original =
            """
            # top
            Host web app
                HostName example.com # inline
                User deploy

            Match host *.internal
                User ops
            """.ReplaceLineEndings();

        var serialized = SshConfigParser.Parse("config", original).Serialize();
        Assert.Equal(original.TrimEnd() + Environment.NewLine, serialized);
        Assert.Contains("# inline", serialized);
        Assert.Contains("Host web app", serialized);
    }

    [Fact]
    public void Validate_RejectsDuplicateAliases()
    {
        var document = SshConfigParser.Parse("config", "Host existing\nHost other\n");
        var edit = new SshHostEdit { Alias = "existing" };
        var result = SshConfigParser.Validate(edit, document, document.HostSections.Last());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("existing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyHost_AddsAndRemovesHostSections()
    {
        var document = SshConfigParser.Parse("config", "Host keep\n    User root\n");
        SshConfigParser.ApplyHost(document, null, new SshHostEdit
        {
            Alias = "added",
            HostName = "added.example",
            IdentityFiles = ["~/.ssh/id_ed25519"],
        });
        Assert.Equal(2, document.HostSections.Count());

        var added = document.HostSections.Last();
        SshConfigParser.RemoveHost(document, added);
        Assert.Single(document.HostSections);
        Assert.Equal("keep", document.HostSections.Single().Aliases.Single());
    }

    [Fact]
    public void Store_SaveCreatesBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinTilix.Tests", Guid.NewGuid().ToString("N"));
        var config = Path.Combine(directory, "config");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(config, "Host old\n");
            var document = SshConfigParser.Parse(config, File.ReadAllText(config));
            var section = document.HostSections.Single();
            var edit = SshConfigParser.ReadHost(section);
            edit.Alias = "fresh";
            SshConfigParser.ApplyHost(document, section, edit);
            SshConfigStore.Save(config, document.Serialize());

            Assert.Contains("Host fresh", File.ReadAllText(config));
            Assert.Contains("Host old", File.ReadAllText(config + ".bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
