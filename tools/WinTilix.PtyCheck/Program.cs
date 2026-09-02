using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinTilix.Session;
using WinTilix.Terminal;

return args.FirstOrDefault() switch
{
    "sync" => Checker.RunSync(),
    "resize" => Checker.RunResize(),
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
            Console.WriteLine("pty-output: " + MarkerLine(StripAnsi(visible), "WIN_TILIX_PTY_OK"));
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

        // The marker echoes once from the line editor and once from `echo` running,
        // so two occurrences per pane means the command actually executed there.
        var firstText = WaitUntil(firstOut, t => Occurrences(t, marker) >= 2, TimeSpan.FromSeconds(8));
        var secondText = WaitUntil(secondOut, t => Occurrences(t, marker) >= 2, TimeSpan.FromSeconds(8));
        var firstHits = Occurrences(firstText, marker);
        var secondHits = Occurrences(secondText, marker);
        Console.WriteLine($"typed-pane occurrences={firstHits} broadcast-pane occurrences={secondHits}");
        if (firstHits >= 2 && secondHits >= 2)
        {
            Console.WriteLine("PTY_CHECK_SYNC_OK");
            Console.WriteLine("typed-pane: " + MarkerLine(StripAnsi(firstText), marker));
            Console.WriteLine("broadcast-pane: " + MarkerLine(StripAnsi(secondText), marker));
            return 0;
        }

        Console.WriteLine("PTY_CHECK_SYNC_FAIL");
        Console.WriteLine("--- first ---\n" + StripAnsi(firstText));
        Console.WriteLine("--- second ---\n" + StripAnsi(secondText));
        return 3;
    }

    public static int RunResize()
    {
        const int cols = 132;
        const int rows = 44;
        var output = new StringBuilder();
        using var session = ConPtySession.Start(CmdInteractive(), workingDirectory: null, 80, 24, Append(output));
        WaitUntil(output, static t => StripAnsi(t).Length > 0, TimeSpan.FromSeconds(8));

        // Round-trip a command first: cmd drops anything typed before it reads stdin.
        session.Write("echo PTY_READY\r\n");
        WaitUntil(output, static t => Occurrences(t, "PTY_READY") >= 2, TimeSpan.FromSeconds(8));

        session.Resize(cols, rows);
        Thread.Sleep(500);

        // `mode con` reports the pseudoconsole geometry the child actually sees.
        session.Write("mode con\r\n");
        var text = WaitUntil(output, MatchesSize, TimeSpan.FromSeconds(8));
        if (MatchesSize(text))
        {
            Console.WriteLine("PTY_CHECK_RESIZE_OK");
            Console.WriteLine($"pty-size: cols={cols} rows={rows}");
            return 0;
        }

        Console.WriteLine("PTY_CHECK_RESIZE_FAIL");
        Console.WriteLine(StripAnsi(text));
        return 4;

        static bool MatchesSize(string raw)
        {
            var visible = StripAnsi(raw);
            var reported = visible.LastIndexOf("mode con", StringComparison.Ordinal);
            if (reported < 0)
            {
                return false;
            }

            var tail = visible[reported..];
            return Regex.IsMatch(tail, @"\b132\b") && Regex.IsMatch(tail, @"\b44\b");
        }
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

    static int Occurrences(string haystack, string needle)
    {
        var total = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            total++;
            index += needle.Length;
        }

        return total;
    }

    static string MarkerLine(string text, string marker)
    {
        var line = text.Split('\n', '\r')
            .LastOrDefault(candidate => candidate.Contains(marker, StringComparison.Ordinal));
        if (line is null)
        {
            return "<not found>";
        }

        // ConPTY paints the screen, so a line can still carry cursor-positioning
        // remnants around the marker itself.
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        return line[start..].Trim();
    }

    sealed class LivePane(string id, ConPtySession session) : ISyncTarget
    {
        public string PaneId { get; } = id;
        public bool ParticipateInSync { get; } = true;
        public void ReceiveSyncedInput(string data) => session.Write(data);
    }
}
