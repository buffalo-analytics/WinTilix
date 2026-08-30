using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0)) throw new Win32Exception();
if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new Win32Exception();
var coord = new COORD { X = 80, Y = 24 };
var hr = CreatePseudoConsole(coord, inRead, outWrite, 0, out var hPC);
Console.WriteLine($"CreatePseudoConsole hr=0x{hr:X8} hPC={hPC}");
if (hr != 0) return 1;

var size = IntPtr.Zero;
InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
var si = new STARTUPINFOEX();
si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
si.lpAttributeList = Marshal.AllocHGlobal(size);
InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size);
var ok = UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)0x00020016, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero);
Console.WriteLine($"UpdateProcThreadAttribute={ok} err={Marshal.GetLastWin32Error()}");
var pSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
var tSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
ok = CreateProcess(null, $"\"{cmd}\" /c echo WIN_TILIX_PTY_OK", ref pSec, ref tSec, false, 0x00080000, IntPtr.Zero, null, ref si, out var pi);
Console.WriteLine($"CreateProcess={ok} pid={pi.dwProcessId} err={Marshal.GetLastWin32Error()}");

var buf = new byte[4096];
using var fs = new FileStream(outRead, FileAccess.Read);
_ = Task.Delay(3000).ContinueWith(_ => ClosePseudoConsole(hPC));
var sw = Stopwatch.StartNew();
while (sw.Elapsed < TimeSpan.FromSeconds(3))
{
    if (fs.Read(buf, 0, buf.Length) is > 0 and var n)
    {
        Console.Write(Encoding.UTF8.GetString(buf, 0, n));
    }
    else break;
}

_ = inWrite;
return 0;

[DllImport("kernel32.dll", SetLastError = true)]
static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
[DllImport("kernel32.dll", SetLastError = true)]
static extern void ClosePseudoConsole(IntPtr hPC);
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
