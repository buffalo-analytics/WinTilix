using System.Runtime.InteropServices;
using System.Text.Json;
using WinTilix.Native;

namespace WinTilix.Agents;

public sealed record AgentInfo(string Name, string? Role, AgentRoleKind Kind, string? Status);

/// <summary>
/// Finds the Claude Code agent running inside a pane by walking the pane's process tree and
/// matching PIDs against the session registry Claude Code keeps in ~/.claude/sessions.
/// </summary>
public static class AgentLocator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);
    private static readonly object Gate = new();
    private static DateTime _loadedAt = DateTime.MinValue;
    private static Dictionary<int, (string Name, string? Status)> _sessions = new();
    private static Dictionary<int, List<int>> _children = new();

    public static string SessionsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    public static AgentInfo? Find(int rootPid)
    {
        Dictionary<int, (string Name, string? Status)> sessions;
        Dictionary<int, List<int>> children;
        lock (Gate)
        {
            if (DateTime.UtcNow - _loadedAt > CacheTtl)
            {
                _sessions = ReadSessions(SessionsDirectory);
                _children = ReadProcessTree();
                _loadedAt = DateTime.UtcNow;
            }

            sessions = _sessions;
            children = _children;
        }

        var pid = FindSessionPid(rootPid, sessions.ContainsKey, children);
        if (pid is null)
        {
            return null;
        }

        var (name, status) = sessions[pid.Value];
        AgentRoleCatalog.Load().TryGetValue(name, out var role);
        return new AgentInfo(name, role, AgentRoleCatalog.Classify(role), status);
    }

    /// <summary>Breadth-first search from the root, so the agent nearest the pane's shell wins.</summary>
    public static int? FindSessionPid(
        int rootPid, Func<int, bool> isSession, IReadOnlyDictionary<int, List<int>> children)
    {
        var queue = new Queue<int>([rootPid]);
        var seen = new HashSet<int>();
        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();
            if (!seen.Add(pid))
            {
                continue;
            }

            if (isSession(pid))
            {
                return pid;
            }

            if (children.TryGetValue(pid, out var kids))
            {
                foreach (var kid in kids)
                {
                    queue.Enqueue(kid);
                }
            }
        }

        return null;
    }

    public static Dictionary<int, (string Name, string? Status)> ReadSessions(string directory)
    {
        var result = new Dictionary<int, (string Name, string? Status)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var id) &&
                        root.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n)
                    {
                        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
                        result[id] = (n, status);
                    }
                }
                catch
                {
                    // A session file can be mid-write or stale; skip it.
                }
            }
        }
        catch
        {
            // No registry directory means no agents.
        }

        return result;
    }

    private static Dictionary<int, List<int>> ReadProcessTree()
    {
        var children = new Dictionary<int, List<int>>();
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return children;
        }

        try
        {
            var entry = new NativeMethods.ProcessEntry32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>(),
            };
            for (var ok = NativeMethods.Process32FirstW(snapshot, ref entry);
                 ok;
                 ok = NativeMethods.Process32NextW(snapshot, ref entry))
            {
                var parent = (int)entry.th32ParentProcessID;
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add((int)entry.th32ProcessID);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        return children;
    }
}
