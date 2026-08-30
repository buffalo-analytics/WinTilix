using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

var cols = args.Length > 0 && short.TryParse(args[0], out var c) ? c : (short)80;
var rows = args.Length > 1 && short.TryParse(args[1], out var r) ? r : (short)24;
var pipeName = args.Length > 2 ? args[2] : "";
var commandLine = args.Length > 3
    ? string.Join(" ", args.Skip(3).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
    : @"C:\Windows\System32\cmd.exe";

if (string.IsNullOrWhiteSpace(pipeName))
{
    return 4;
}

// Opt-in tracing: the host has no console, so diagnostics must go to a file.
var logPath = Environment.GetEnvironmentVariable("WINTILIX_PTYHOST_LOG");
var logLock = new object();
void Trace(string message)
{
    if (logPath is null)
    {
        return;
    }

    try
    {
        lock (logLock)
        {
            File.AppendAllText(logPath, $"[{Environment.ProcessId} {DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
    }
    catch (IOException)
    {
    }
}

Trace($"start commandLine={commandLine} cols={cols} rows={rows}");

using var input = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.None);
input.Connect(10000);
Trace("named pipe connected");

var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
if (!CreatePipe(out var inRead, out var inWrite, ref sa, 0)) throw new Win32Exception();
if (!CreatePipe(out var outRead, out var outWrite, ref sa, 0)) throw new Win32Exception();
SetHandleInformation(inWrite.DangerousGetHandle(), 1, 0);
SetHandleInformation(outRead.DangerousGetHandle(), 1, 0);

var coord = new COORD { X = Math.Max(cols, (short)8), Y = Math.Max(rows, (short)8) };
var hr = CreatePseudoConsole(coord, inRead, outWrite, 0, out var hPC);
Trace($"CreatePseudoConsole hr=0x{hr:X8}");
if (hr != 0) return 1;

var size = IntPtr.Zero;
InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
var si = new STARTUPINFOEX();
si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
si.lpAttributeList = Marshal.AllocHGlobal(size);
InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size);
if (!UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)0x00020016, hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
{
    return 2;
}

var pSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
var tSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
var created = CreateProcess(null, commandLine, ref pSec, ref tSec, false, 0x00080000, IntPtr.Zero, null, ref si, out var pi);
Trace($"CreateProcess={created} pid={pi.dwProcessId} err={Marshal.GetLastWin32Error()} consoleWindow=0x{GetConsoleWindow():X}");
if (!created)
{
    return 3;
}

inRead.Dispose();
outWrite.Dispose();

var ptyOut = new FileStream(outRead, FileAccess.Read, 4096, false);
var ptyIn = new FileStream(inWrite, FileAccess.Write, 4096, false);
var stdout = new FileStream(new SafeFileHandle(GetStdHandle(-11), ownsHandle: false), FileAccess.Write, 4096, false);

var outputTask = Task.Run(() => Copy(ptyOut, stdout, "pty->app"));
var inputTask = Task.Run(() => Copy(input, ptyIn, "app->pty"));
WaitForSingleObject(pi.hProcess, 0xFFFFFFFF);
Trace("child exited");
try { ptyIn.Flush(); } catch (IOException) { }
ClosePseudoConsole(hPC);
try { outputTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
try { inputTask.Wait(TimeSpan.FromMilliseconds(200)); } catch { }
try { ptyOut.Dispose(); } catch { }
try { ptyIn.Dispose(); } catch { }
try { stdout.Dispose(); } catch { }
return 0;

void Copy(Stream from, Stream to, string direction)
{
    var buffer = new byte[4096];
    var total = 0L;
    try
    {
        while (true)
        {
            var n = from.Read(buffer, 0, buffer.Length);
            if (n <= 0)
            {
                Trace($"{direction} ended after {total} bytes (read returned {n})");
                break;
            }

            total += n;
            Trace($"{direction} {n} bytes (total {total}): {Escape(buffer, n)}");
            to.Write(buffer, 0, n);
            to.Flush();
        }
    }
    catch (IOException ex)
    {
        Trace($"{direction} IOException after {total} bytes: {ex.Message}");
    }
}

static string Escape(byte[] buffer, int length)
{
    var text = Encoding.UTF8.GetString(buffer, 0, Math.Min(length, 400));
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

[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();
[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr GetStdHandle(int nStdHandle);
[DllImport("kernel32.dll", SetLastError = true)]
static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
[DllImport("kernel32.dll", SetLastError = true)]
static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);
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
