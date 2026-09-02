using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

const int StdInputHandle = -10;
const int StdOutputHandle = -11;
const int StdErrorHandle = -12;

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

using var input = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.None);
input.Connect(10000);

var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
if (!CreatePipe(out var inRead, out var inWrite, ref sa, 0)) throw new Win32Exception();
if (!CreatePipe(out var outRead, out var outWrite, ref sa, 0)) throw new Win32Exception();
SetHandleInformation(inWrite.DangerousGetHandle(), 1, 0);
SetHandleInformation(outRead.DangerousGetHandle(), 1, 0);

var coord = new COORD { X = Math.Max(cols, (short)8), Y = Math.Max(rows, (short)8) };
var hr = CreatePseudoConsole(coord, inRead, outWrite, 0, out var hPC);
if (hr != 0) return 1;

// The app resizes the pseudoconsole over a side channel, because the stdin pipe
// carries raw terminal bytes that must not be parsed for commands.
var ptyLock = new object();
var ptyClosed = false;
var controlTask = Task.Run(() => ControlLoop(pipeName + ".ctl"));

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

// This host's stdout is a pipe back to the app. CreateProcess would hand those
// std handles to the shell, which then writes to the pipe instead of the
// pseudoconsole screen, leaving ConPTY with nothing to render and no input sink.
// Detaching them forces the shell onto the pseudoconsole. The handle values stay
// valid for our own use afterwards.
var appStdout = GetStdHandle(StdOutputHandle);
SetStdHandle(StdInputHandle, IntPtr.Zero);
SetStdHandle(StdOutputHandle, IntPtr.Zero);
SetStdHandle(StdErrorHandle, IntPtr.Zero);

var pSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
var tSec = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
var created = CreateProcess(null, commandLine, ref pSec, ref tSec, false, 0x00080000, IntPtr.Zero, null, ref si, out var pi);
if (!created)
{
    return 3;
}

inRead.Dispose();
outWrite.Dispose();

var ptyOut = new FileStream(outRead, FileAccess.Read, 4096, false);
var ptyIn = new FileStream(inWrite, FileAccess.Write, 4096, false);
var stdout = new FileStream(new SafeFileHandle(appStdout, ownsHandle: false), FileAccess.Write, 4096, false);

var outputTask = Task.Run(() => Copy(ptyOut, stdout));
var inputTask = Task.Run(() => Copy(input, ptyIn));
WaitForSingleObject(pi.hProcess, 0xFFFFFFFF);
try { ptyIn.Flush(); } catch (IOException) { }
lock (ptyLock)
{
    ptyClosed = true;
    ClosePseudoConsole(hPC);
}

try { outputTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
try { controlTask.Wait(TimeSpan.FromMilliseconds(200)); } catch { }
try { inputTask.Wait(TimeSpan.FromMilliseconds(200)); } catch { }
try { ptyOut.Dispose(); } catch { }
try { ptyIn.Dispose(); } catch { }
try { stdout.Dispose(); } catch { }
return 0;

void ControlLoop(string controlPipeName)
{
    try
    {
        using var control = new NamedPipeClientStream(".", controlPipeName, PipeDirection.In, PipeOptions.None);
        control.Connect(10000);
        using var reader = new StreamReader(control);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 ||
                parts[0] != "resize" ||
                !short.TryParse(parts[1], out var newCols) ||
                !short.TryParse(parts[2], out var newRows))
            {
                continue;
            }

            var size = new COORD
            {
                X = Math.Max(newCols, (short)8),
                Y = Math.Max(newRows, (short)8),
            };

            lock (ptyLock)
            {
                if (ptyClosed)
                {
                    return;
                }

                ResizePseudoConsole(hPC, size);
            }
        }
    }
    catch (Exception)
    {
        // Resize is best effort: the session keeps running at its current size.
    }
}

static void Copy(Stream from, Stream to)
{
    var buffer = new byte[4096];
    try
    {
        while (true)
        {
            var n = from.Read(buffer, 0, buffer.Length);
            if (n <= 0)
            {
                break;
            }

            to.Write(buffer, 0, n);
            to.Flush();
        }
    }
    catch (IOException)
    {
    }
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr GetStdHandle(int nStdHandle);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);
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
[DllImport("kernel32.dll", SetLastError = true)]
static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
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
