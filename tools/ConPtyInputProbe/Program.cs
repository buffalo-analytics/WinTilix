using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Diagnoses ConPTY attach + input delivery. Writes a report file so it can run
// with its own console detached, which removes the parent console as a variable.

var reportPath = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "probe-report.txt");
var report = new StringBuilder();

var consoleBefore = GetConsoleWindow();
var freed = FreeConsole();
var consoleAfter = GetConsoleWindow();
Log($"console window before=0x{consoleBefore:X} FreeConsole={freed} after=0x{consoleAfter:X}");

var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

// The PTY is deliberately an odd size. If the child reports 77x21 it really is
// attached to our pseudoconsole; anything else means it attached elsewhere.
var modePath = Path.Combine(Path.GetDirectoryName(reportPath)!, "child-mode.txt");
File.Delete(modePath);
RunScenario("attach identity", $"\"{cmd}\" /c mode con > \"{modePath}\"", oneShot: false, settleOnly: true);
Log($"child-reported console:{Environment.NewLine}{(File.Exists(modePath) ? File.ReadAllText(modePath).Trim() : "<no file written>")}");

RunScenario("cmd /c echo", $"\"{cmd}\" /c echo PROBE_ONESHOT", oneShot: true);
RunScenario("interactive cmd", $"\"{cmd}\"", oneShot: false);

File.WriteAllText(reportPath, report.ToString());
return 0;

void RunScenario(string name, string commandLine, bool oneShot, bool settleOnly = false)
{
    Log("");
    Log($"===== {name} =====");

    if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0)) throw new Win32Exception();
    if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new Win32Exception();

    var coord = new COORD { X = 77, Y = 21 };
    var hr = CreatePseudoConsole(coord, inRead, outWrite, 0, out var hPC);
    Log($"CreatePseudoConsole hr=0x{hr:X8}");
    if (hr != 0) return;

    var size = IntPtr.Zero;
    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
    Log($"attribute list size={size}");

    var si = new STARTUPINFOEX();
    si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
    si.lpAttributeList = Marshal.AllocHGlobal(size);
    var init = InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size);
    var update = UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)0x00020016, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero);
    Log($"STARTUPINFOEX cb={si.StartupInfo.cb} init={init} update={update} err={Marshal.GetLastWin32Error()}");

    // A redirected stdout on this process is otherwise handed to the child, which
    // then bypasses the console screen entirely and leaves ConPTY nothing to send.
    Log($"std handles before clear: in=0x{GetStdHandle(-10):X} out=0x{GetStdHandle(-11):X} err=0x{GetStdHandle(-12):X}");
    SetStdHandle(-10, IntPtr.Zero);
    SetStdHandle(-11, IntPtr.Zero);
    SetStdHandle(-12, IntPtr.Zero);

    var pSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
    var tSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
    var created = CreateProcess(null, commandLine, ref pSec, ref tSec, false, 0x00080000, IntPtr.Zero, null, ref si, out var pi);
    Log($"CreateProcess={created} pid={pi.dwProcessId} err={Marshal.GetLastWin32Error()}");
    if (!created) return;

    inRead.Dispose();
    outWrite.Dispose();

    var transcript = new StringBuilder();
    var reader = new FileStream(outRead, FileAccess.Read, 4096, false);
    var writer = new FileStream(inWrite, FileAccess.Write, 4096, false);

    var readerThread = new Thread(() =>
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var n = reader.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;
                lock (transcript) transcript.Append(Encoding.UTF8.GetString(buffer, 0, n));
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    })
    { IsBackground = true };
    readerThread.Start();

    if (settleOnly)
    {
        WaitFor(() => false, TimeSpan.FromSeconds(3));
    }
    else if (oneShot)
    {
        WaitFor(() => Snapshot().Contains("PROBE_ONESHOT"), TimeSpan.FromSeconds(5));
        Log($"child output reached PTY pipe: {Snapshot().Contains("PROBE_ONESHOT")}");
    }
    else
    {
        WaitFor(() => Snapshot().Contains('>'), TimeSpan.FromSeconds(5));
        Log($"prompt reached PTY pipe: {Snapshot().Contains('>')}");

        Attempt("raw CR", "PROBE_RAW_CR", Encoding.UTF8.GetBytes("echo PROBE_RAW_CR\r"));
        Attempt("win32 input mode", "PROBE_WIN32", EncodeWin32("echo PROBE_WIN32\r"));
    }

    Log("transcript: " + Escape(Snapshot()));

    ClosePseudoConsole(hPC);
    try { writer.Dispose(); } catch { }
    readerThread.Join(TimeSpan.FromSeconds(1));

    void Attempt(string label, string marker, byte[] payload)
    {
        try
        {
            writer.Write(payload, 0, payload.Length);
            writer.Flush();
        }
        catch (IOException ex)
        {
            Log($"{label}: write threw {ex.Message}");
            return;
        }

        // The marker echoes once from the line editor and once from `echo`
        // running, so two occurrences means the command actually executed.
        var executed = WaitFor(() => Occurrences(Snapshot(), marker) >= 2, TimeSpan.FromSeconds(3));
        Log($"{label}: wrote {payload.Length} bytes, executed={executed}");
    }

    string Snapshot()
    {
        lock (transcript) return transcript.ToString();
    }
}

void Log(string line) => report.AppendLine(line);

static bool WaitFor(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (condition()) return true;
        Thread.Sleep(40);
    }

    return false;
}

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

static string Escape(string text)
{
    var builder = new StringBuilder(text.Length * 2);
    foreach (var ch in text)
    {
        builder.Append(ch switch
        {
            '\u001b' => "<ESC>",
            '\r' => "<CR>",
            '\n' => "<LF>",
            '\0' => "<NUL>",
            _ => ch >= ' ' && ch <= '~' ? ch.ToString() : $"<{(int)ch:X2}>"
        });
    }

    return builder.ToString();
}

static byte[] EncodeWin32(string text)
{
    var builder = new StringBuilder(text.Length * 48);
    foreach (var ch in text)
    {
        int vk;
        int scan;
        var control = 0;

        if (ch == '\r')
        {
            vk = 0x0D;
            scan = (int)MapVirtualKeyW(0x0D, 0);
        }
        else
        {
            var mapped = VkKeyScanW(ch);
            vk = mapped & 0xFF;
            scan = (int)MapVirtualKeyW((uint)vk, 0);
            var mods = (mapped >> 8) & 0xFF;
            if ((mods & 1) != 0) control |= 0x0010;
            if ((mods & 2) != 0) control |= 0x0008;
            if ((mods & 4) != 0) control |= 0x0002;
        }

        builder.Append($"\u001b[{vk};{scan};{(int)ch};1;{control};1_");
        builder.Append($"\u001b[{vk};{scan};{(int)ch};0;{control};1_");
    }

    return Encoding.UTF8.GetBytes(builder.ToString());
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool FreeConsole();
[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();
[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr GetStdHandle(int nStdHandle);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern short VkKeyScanW(char ch);
[DllImport("user32.dll")]
static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
[DllImport("kernel32.dll", SetLastError = true)]
static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
[DllImport("kernel32.dll", SetLastError = true)]
static extern void ClosePseudoConsole(IntPtr hPC);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine, ref SECURITY_ATTRIBUTES lpProcessAttributes, ref SECURITY_ATTRIBUTES lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

[StructLayout(LayoutKind.Sequential)] struct COORD { public short X; public short Y; }
[StructLayout(LayoutKind.Sequential)] struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct STARTUPINFO
{
    public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
    public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
    public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
}
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
[StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
