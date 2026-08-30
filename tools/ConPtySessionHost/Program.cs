using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
if (!StartConPty($"\"{cmd}\" /c echo WIN_TILIX_PTY_OK", null, 80, 24, out _, out var ourRead, out _, out _, out var hPC, out _, out _, out _, out _))
{
    return 1;
}

var buf = new byte[4096];
using var fs = new FileStream(ourRead, FileAccess.Read);
var collected = new StringBuilder();
_ = Task.Delay(3000).ContinueWith(_ => ClosePseudoConsole(hPC));
var sw = Stopwatch.StartNew();
while (sw.Elapsed < TimeSpan.FromSeconds(3))
{
    if (fs.Read(buf, 0, buf.Length) is > 0 and var n)
    {
        collected.Append(Encoding.UTF8.GetString(buf, 0, n));
        if (collected.ToString().Contains("WIN_TILIX_PTY_OK")) break;
    }
    else break;
}

Console.WriteLine(collected.ToString().Contains("WIN_TILIX_PTY_OK") ? "DIRECT_STARTCONPTY_OK" : "DIRECT_STARTCONPTY_FAIL");
return collected.ToString().Contains("WIN_TILIX_PTY_OK") ? 0 : 2;

static bool StartConPty(
    string commandLine,
    string? workingDirectory,
    int cols,
    int rows,
    out SafeFileHandle ourWrite,
    out SafeFileHandle ourRead,
    out SafeFileHandle ptyIn,
    out SafeFileHandle ptyOut,
    out IntPtr pseudoConsole,
    out IntPtr attributeList,
    out IntPtr process,
    out IntPtr thread,
    out int processId)
{
    ourWrite = ourRead = ptyIn = ptyOut = null!;
    pseudoConsole = attributeList = process = thread = IntPtr.Zero;
    processId = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine, ref SECURITY_ATTRIBUTES lpProcessAttributes, ref SECURITY_ATTRIBUTES lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    if (!CreatePipe(out ptyIn, out ourWrite, IntPtr.Zero, 0)) throw new Win32Exception();
    if (!CreatePipe(out ourRead, out ptyOut, IntPtr.Zero, 0)) throw new Win32Exception();
    var coord = new COORD { X = (short)Math.Max(cols, 8), Y = (short)Math.Max(rows, 8) };
    var hr = CreatePseudoConsole(coord, ptyIn, ptyOut, 0, out pseudoConsole);
    if (hr != 0) return false;

    var size = IntPtr.Zero;
    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
    attributeList = Marshal.AllocHGlobal(size);
    InitializeProcThreadAttributeList(attributeList, 1, 0, ref size);
    if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)0x00020016, pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
    {
        return false;
    }

    var si = new STARTUPINFOEX();
    si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
    si.lpAttributeList = attributeList;
    var pSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
    var tSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
    if (!CreateProcess(null, commandLine, ref pSec, ref tSec, false, 0x00080000, IntPtr.Zero, workingDirectory, ref si, out var pi))
    {
        return false;
    }

    process = pi.hProcess;
    thread = pi.hThread;
    processId = pi.dwProcessId;
    return true;
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern void ClosePseudoConsole(IntPtr hPC);

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
