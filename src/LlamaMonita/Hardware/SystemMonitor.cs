using System.Runtime.InteropServices;

namespace LlamaMonita.Hardware;

public readonly record struct SystemReading(double CpuPercent, ulong RamTotal, ulong RamAvailable);

public sealed class SystemMonitor
{
    private ulong _prevIdle, _prevTotal;

    public SystemReading Sample()
    {
        double cpu = 0;
        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            ulong total = kernel + user;   // kernel time includes idle time
            if (_prevTotal != 0 && total > _prevTotal)
                cpu = 100.0 * (1.0 - (double)(idle - _prevIdle) / (total - _prevTotal));
            _prevIdle = idle;
            _prevTotal = total;
        }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        return new SystemReading(Math.Clamp(cpu, 0, 100), mem.ullTotalPhys, mem.ullAvailPhys);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
