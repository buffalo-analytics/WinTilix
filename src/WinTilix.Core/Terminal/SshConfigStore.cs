using System.Text;

namespace WinTilix.Terminal;

public static class SshConfigStore
{
    public static string Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    public static void Save(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The SSH config path has no parent directory.");

        Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".wintilix.tmp";
        var backupPath = fullPath + ".bak";

        try
        {
            File.WriteAllText(tempPath, NormalizeLineEndings(content), new UTF8Encoding(false));

            if (File.Exists(fullPath))
            {
                File.Copy(fullPath, backupPath, overwrite: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
}
