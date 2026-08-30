using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using WinTilix.Diagnostics;

namespace WinTilix.Terminal;

public sealed class ConPtySession : IDisposable
{
    private readonly Process _host;
    private readonly NamedPipeServerStream _stdin;
    private readonly Stream _stdout;
    private readonly object _writeLock = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public event Action<byte[]>? OutputReceived;
    public event Action? Exited;
    public int LastReadError { get; private set; }
    public int LastWriteError { get; private set; }
    public int ProcessId { get; }

    public static ConPtySession Start(string commandLine, string? workingDirectory, int cols = 80, int rows = 24, Action<byte[]>? onOutput = null)
    {
        var hostPath = LocatePtyHost();
        var pipeName = "WinTilix." + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.None);
        try
        {
            var start = new ProcessStartInfo(hostPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            start.ArgumentList.Add(Math.Max(cols, 8).ToString());
            start.ArgumentList.Add(Math.Max(rows, 8).ToString());
            start.ArgumentList.Add(pipeName);
            foreach (var part in SplitCommandLine(commandLine))
            {
                start.ArgumentList.Add(part);
            }

            var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start WinTilix.PtyHost.");
            pipe.WaitForConnection();
            DebugLog.Info($"PtyHost started pid={process.Id}");

            var session = new ConPtySession(process, pipe, onOutput);
            _ = Task.Run(session.ReadLoop);
            _ = Task.Run(session.WaitForExit);
            return session;
        }
        catch (Exception ex)
        {
            DebugLog.Error("Failed to start PtyHost", ex);
            pipe.Dispose();
            throw;
        }
    }

    private ConPtySession(Process host, NamedPipeServerStream stdin, Action<byte[]>? onOutput)
    {
        _host = host;
        ProcessId = host.Id;
        _stdin = stdin;
        _stdout = host.StandardOutput.BaseStream;
        if (onOutput is not null)
        {
            OutputReceived += onOutput;
        }
    }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text) || _disposed)
        {
            return;
        }

        WriteBytes(Encoding.UTF8.GetBytes(text));
    }

    public void WriteBytes(byte[] bytes)
    {
        if (bytes.Length == 0 || _disposed)
        {
            return;
        }

        try
        {
            lock (_writeLock)
            {
                _stdin.Write(bytes, 0, bytes.Length);
                _stdin.Flush();
            }
        }
        catch (IOException)
        {
            LastWriteError = MarshalGetLastError();
        }
    }

    public void Resize(int cols, int rows)
    {
        _ = cols;
        _ = rows;
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var n = _stdout.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                {
                    break;
                }

                var copy = new byte[n];
                Buffer.BlockCopy(buffer, 0, copy, 0, n);
                OutputReceived?.Invoke(copy);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
            LastReadError = MarshalGetLastError();
        }
    }

    private void WaitForExit()
    {
        try
        {
            _host.WaitForExit();
        }
        catch
        {
        }

        if (LastReadError != 0 || LastWriteError != 0)
        {
            DebugLog.Warn($"PtyHost exited pid={ProcessId} readError={LastReadError} writeError={LastWriteError}");
        }

        Exited?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try { _host.Kill(entireProcessTree: true); } catch { }
        try { _stdin.Dispose(); } catch { }
        try { _stdout.Dispose(); } catch { }
        try { _host.Dispose(); } catch { }
        _cts.Dispose();
    }

    private static string LocatePtyHost()
    {
        var name = "WinTilix.PtyHost.exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, name),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "WinTilix.PtyHost", "bin", "Release", "net8.0-windows", name)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "WinTilix.PtyHost", "bin", "Debug", "net8.0-windows", name))
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("WinTilix.PtyHost.exe was not found next to the application.");
    }

    private static IEnumerable<string> SplitCommandLine(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in commandLine.Trim())
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static int MarshalGetLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}
