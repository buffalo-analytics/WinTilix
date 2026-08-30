namespace WinTilix.Terminal;

public static class ShellResolver
{
    public static string DefaultShellCommand()
    {
        var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh))
        {
            return Quote(pwsh) + " -NoLogo";
        }

        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (File.Exists(powershell))
        {
            return Quote(powershell) + " -NoLogo";
        }

        return Quote(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
    }

    public static string? OpenSshPath()
    {
        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"OpenSSH\ssh.exe");
        return File.Exists(ssh) ? ssh : null;
    }

    public static string SshCommand(string target)
    {
        var ssh = OpenSshPath() ?? "ssh";
        return $"{Quote(ssh)} {target}";
    }

    public static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
