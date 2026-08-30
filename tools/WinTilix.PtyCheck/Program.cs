using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinTilix.Session;
using WinTilix.Terminal;

return args.FirstOrDefault() switch
{
    "sync" => Checker.RunSync(),
    _ => Checker.RunEcho()
};

static class Checker
{
    public static int RunEcho()
    {
        var output = new StringBuilder();
        using var session = ConPtySession.Start(CmdSlashC("WIN_TILIX_PTY_OK"), workingDirectory: null, 80, 24, Append(output));
        var text = WaitUntil(output, static t => t.Replace("\0", "").Contains("WIN_TILIX_PTY_OK"), TimeSpan.FromSeconds(8));
        var visible = text.Replace("\0", "");
        if (visible.Contains("WIN_TILIX_PTY_OK"))
        {
            Console.WriteLine("PTY_CHECK_ECHO_OK");
            return 0;
        }

        Console.WriteLine("ESC=" + string.Concat(visible.Select(ch => ch is >= ' ' and <= '~' ? ch.ToString() : $"\\x{(int)ch:X2}")));
        Console.WriteLine($"PTY_CHECK_ECHO_FAIL pid={session.ProcessId}");
        return 2;
    }

    public static int RunSync()
    {
        const string marker = "WIN_TILIX_SYNC_OK";
        var firstOut = new StringBuilder();
        var secondOut = new StringBuilder();
        using var first = ConPtySession.Start(CmdInteractive(), workingDirectory: null, 80, 24, Append(firstOut));
        using var second = ConPtySession.Start(CmdInteractive(), workingDirectory: null, 80, 24, Append(secondOut));

        WaitUntil(firstOut, static t => StripAnsi(t).Length > 0, TimeSpan.FromSeconds(8));
        WaitUntil(secondOut, static t => StripAnsi(t).Length > 0, TimeSpan.FromSeconds(8));
        Thread.Sleep(200);

        var hub = new SessionHub { SessionSyncEnabled = true };
        hub.Register(new LivePane("a", first));
        hub.Register(new LivePane("b", second));

        var command = $"echo {marker}\r\n";
        first.Write(command);
        hub.BroadcastInput("a", command);

        var firstText = WaitUntil(firstOut, t => t.Contains(marker), TimeSpan.FromSeconds(8));
        var secondText = WaitUntil(secondOut, t => t.Contains(marker), TimeSpan.FromSeconds(8));
        if (firstText.Contains(marker) && secondText.Contains(marker))
        {
            Console.WriteLine("PTY_CHECK_SYNC_OK");
            return 0;
        }

        Console.WriteLine("PTY_CHECK_SYNC_FAIL");
        Console.WriteLine("--- first ---\n" + StripAnsi(firstText));
        Console.WriteLine("--- second ---\n" + StripAnsi(secondText));
        return 3;
    }

    static string CmdSlashC(string text)
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return $"\"{cmd}\" /c echo {text}";
    }

    static string CmdInteractive()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return $"\"{cmd}\"";
    }

    static Action<byte[]> Append(StringBuilder output) => bytes =>
    {
        lock (output)
        {
            output.Append(Encoding.UTF8.GetString(bytes));
        }
    };

    static string Snapshot(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }

    static string WaitUntil(StringBuilder output, Func<string, bool> ok, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var text = Snapshot(output);
            if (ok(text))
            {
                return text;
            }

            Thread.Sleep(40);
        }

        return Snapshot(output);
    }

    static string StripAnsi(string text) => Regex.Replace(text, @"\x1B\[[0-9;?]*[A-Za-z]", "");

    sealed class LivePane(string id, ConPtySession session) : ISyncTarget
    {
        public string PaneId { get; } = id;
        public bool ParticipateInSync { get; } = true;
        public void ReceiveSyncedInput(string data) => session.Write(data);
    }
}
