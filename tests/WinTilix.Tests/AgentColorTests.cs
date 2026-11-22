using WinTilix.Agents;
using Xunit;

namespace WinTilix.Tests;

[Collection("LocalAppData")]
public class AgentColorTests : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinTilix-tests-" + Guid.NewGuid().ToString("N"));

    public AgentColorTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _previous);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ColorHex_UsesDefaultWhenNothingSaved() =>
        Assert.Equal("#E3B341", AgentRoleCatalog.ColorHex(AgentRoleKind.Coordinator));

    [Fact]
    public void SaveColors_PersistsOverridesAndNormalisesHex()
    {
        AgentRoleCatalog.SaveColors(new Dictionary<AgentRoleKind, string>
        {
            [AgentRoleKind.Coordinator] = "ff0000",
            [AgentRoleKind.Backend] = AgentRoleCatalog.DefaultColorHex(AgentRoleKind.Backend),
        });

        Assert.Equal("#FF0000", AgentRoleCatalog.ColorHex(AgentRoleKind.Coordinator));
        Assert.Equal("#58A6FF", AgentRoleCatalog.ColorHex(AgentRoleKind.Backend));
        Assert.Single(AgentRoleCatalog.LoadColorOverrides());
    }

    [Fact]
    public void SaveColors_WithOnlyDefaultsRemovesTheFile()
    {
        AgentRoleCatalog.SaveColors(new Dictionary<AgentRoleKind, string> { [AgentRoleKind.Ui] = "#000000" });
        Assert.True(File.Exists(AgentRoleCatalog.ColorsPath));

        AgentRoleCatalog.SaveColors(new Dictionary<AgentRoleKind, string>
        {
            [AgentRoleKind.Ui] = AgentRoleCatalog.DefaultColorHex(AgentRoleKind.Ui),
        });

        Assert.False(File.Exists(AgentRoleCatalog.ColorsPath));
    }

    [Fact]
    public void LoadColorOverrides_IgnoresInvalidEntriesAndCorruptFiles()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgentRoleCatalog.ColorsPath)!);
        File.WriteAllText(AgentRoleCatalog.ColorsPath, """{"qa":"nope","bogus":"#112233","ui":"#112233"}""");
        Assert.Equal("#112233", Assert.Single(AgentRoleCatalog.LoadColorOverrides()).Value);

        File.WriteAllText(AgentRoleCatalog.ColorsPath, "{ broken");
        Assert.Empty(AgentRoleCatalog.LoadColorOverrides());
    }
}
