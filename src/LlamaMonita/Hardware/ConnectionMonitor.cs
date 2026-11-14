using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LlamaMonita.Hardware;

/// <summary>
/// Finds out who is connected to a local llama-server, from the Windows TCP connection table.
/// llama-server does not report client addresses over HTTP, so this is the only way to tell where a
/// request came from without changing how the server is started. Only works when the server runs on
/// this machine. No administrator rights required.
/// </summary>
public static class ConnectionMonitor
{
    /// <summary>True when <paramref name="host"/> names this machine (loopback or one of its addresses).</summary>
    public static bool IsLocalHost(string host)
    {
        try
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            var addresses = IPAddress.TryParse(host, out var ip) ? new[] { ip } : Dns.GetHostAddresses(host);
            var own = Dns.GetHostAddresses(Dns.GetHostName());
            return addresses.Any(a => IPAddress.IsLoopback(a) || own.Contains(a));
        }
        catch { return false; }
    }

    /// <summary>
    /// Clients with an established IPv4 connection to <paramref name="port"/> on this machine:
    /// a remote address for LAN clients, or "local (process)" for programs on this machine.
    /// Connections opened by <paramref name="selfPid"/> (this monitor's own polling) are left out.
    /// </summary>
    public static List<string> Clients(int port, int selfPid)
    {
        var result = new List<string>();
        try
        {
            var rows = ReadTable();
            foreach (var r in rows)
            {
                if (r.LocalPort != port || r.State != MIB_TCP_STATE_ESTAB) continue;
                var remote = new IPAddress(r.RemoteAddr);
                bool sameMachine = IPAddress.IsLoopback(remote) || r.RemoteAddr == r.LocalAddr;
                string name;
                if (sameMachine)
                {
                    // The other end of a same-machine connection is a row whose local port is our remote port.
                    var peer = rows.FirstOrDefault(p => p.LocalPort == r.RemotePort && p.RemotePort == port);
                    if (peer.OwningPid == (uint)selfPid) continue;
                    string process = ProcessName((int)peer.OwningPid);
                    if (process == SelfName) continue;   // another LlamaMonita window polling the same server
                    name = "local (" + process + ")";
                }
                else
                {
                    name = remote.ToString();
                }
                if (!result.Contains(name)) result.Add(name);
            }
        }
        catch { }
        return result;
    }

    private static readonly string SelfName = Process.GetCurrentProcess().ProcessName;

    private static string ProcessName(int pid)
    {
        if (pid <= 0) return "unknown";
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch { return "pid " + pid; }
    }

    private readonly record struct Row(uint State, uint LocalAddr, int LocalPort, uint RemoteAddr, int RemotePort, uint OwningPid);

    private static List<Row> ReadTable()
    {
        var list = new List<Row>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        IntPtr buffer = Marshal.AllocHGlobal(size + 4096);   // head-room: the table can grow between the two calls
        try
        {
            size += 4096;
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return list;
            int count = Marshal.ReadInt32(buffer);
            IntPtr p = buffer + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++, p += rowSize)
            {
                var r = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(p);
                list.Add(new Row(r.dwState, r.dwLocalAddr, Port(r.dwLocalPort), r.dwRemoteAddr, Port(r.dwRemotePort), r.dwOwningPid));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return list;
    }

    // Ports sit in the low 16 bits, in network byte order.
    private static int Port(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    private const int AF_INET = (int)AddressFamily.InterNetwork;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint MIB_TCP_STATE_ESTAB = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState, dwLocalAddr, dwLocalPort, dwRemoteAddr, dwRemotePort, dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);
}
