using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LlamaMonita.Hardware;

public sealed class GpuAdapter
{
    public string Name { get; init; } = "";
    public string LuidKey { get; init; } = "";   // "luid_0x00000000_0x0001018c" (as used by the GPU perf counters)
    public uint VendorId { get; init; }
    public ulong DedicatedTotal { get; init; }
    public ulong SharedTotal { get; init; }
    public bool? Integrated { get; init; }       // true = UMA (iGPU), false = discrete, null = unknown
    internal uint KmtHandle;
}

public sealed class GpuAdapterReading
{
    public GpuAdapter Adapter { get; init; } = null!;
    public double UtilPercent { get; set; }
    public string TopEngine { get; set; } = "";
    public long DedicatedUsed { get; set; }
    public long SharedUsed { get; set; }
    public double? TemperatureC { get; set; }
    public int? FanRpm { get; set; }
    public double? PowerPercent { get; set; }
}

public sealed class GpuReading
{
    public List<GpuAdapterReading> Adapters { get; } = new();
    // Aggregated over the watched processes (local llama-server instances)
    public double WatchUtilPercent { get; set; }
    public long WatchDedicated { get; set; }
    public long WatchShared { get; set; }
}

/// <summary>
/// Vendor-neutral GPU telemetry for Windows: DXGI for adapter identity, the WDDM "GPU *" performance
/// counters for utilization and memory, and D3DKMT adapter perf data for temperature / fan / power
/// (the same sources Task Manager uses). No administrator rights required.
/// </summary>
public sealed unsafe class GpuMonitor : IDisposable
{
    public IReadOnlyList<GpuAdapter> Adapters { get; }

    private readonly PerformanceCounterCategory? _engineCat;
    private readonly PerformanceCounterCategory? _adapterMemCat;
    private readonly PerformanceCounterCategory? _processMemCat;
    private Dictionary<string, CounterSample> _prevEngine = new(StringComparer.OrdinalIgnoreCase);

    public GpuMonitor()
    {
        Adapters = EnumerateAdapters();
        foreach (var a in Adapters) a.KmtHandle = OpenKmt(a.LuidKey);
        _engineCat = TryCategory("GPU Engine");
        _adapterMemCat = TryCategory("GPU Adapter Memory");
        _processMemCat = TryCategory("GPU Process Memory");
    }

    public GpuReading Sample(IReadOnlyCollection<int> watchPids)
    {
        var reading = new GpuReading();
        var byLuid = new Dictionary<string, GpuAdapterReading>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in Adapters)
        {
            var r = new GpuAdapterReading { Adapter = a };
            byLuid[a.LuidKey] = r;
            reading.Adapters.Add(r);
            ReadPerfData(a, r);
        }

        SampleEngines(reading, byLuid, watchPids);
        SampleAdapterMemory(byLuid);
        SampleProcessMemory(reading, watchPids);
        return reading;
    }

    // ---- utilization -------------------------------------------------------------------------

    private void SampleEngines(GpuReading reading, Dictionary<string, GpuAdapterReading> byLuid, IReadOnlyCollection<int> watchPids)
    {
        if (_engineCat is null) return;
        var current = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase);
        // (luid, engine) -> utilization summed over processes; the adapter figure is the busiest engine.
        var perEngine = new Dictionary<(string luid, string eng), double>();
        var watchEngine = new Dictionary<(string luid, string eng), double>();
        try
        {
            var data = _engineCat.ReadCategory();
            if (!data.Contains("Utilization Percentage")) return;
            foreach (InstanceData inst in data["Utilization Percentage"]!.Values)
            {
                string name = inst.InstanceName;
                current[name] = inst.Sample;
                if (!_prevEngine.TryGetValue(name, out var prev)) continue;
                double v = CounterSample.Calculate(prev, inst.Sample);
                if (double.IsNaN(v) || v <= 0) continue;
                if (!ParseEngineInstance(name, out int pid, out string luid, out string eng)) continue;
                var key = (luid, eng);
                perEngine[key] = perEngine.GetValueOrDefault(key) + v;
                if (watchPids.Contains(pid)) watchEngine[key] = watchEngine.GetValueOrDefault(key) + v;
            }
        }
        catch { /* counters can disappear between enumeration and read */ }
        _prevEngine = current;

        foreach (var ((luid, eng), v) in perEngine)
        {
            if (!byLuid.TryGetValue(luid, out var r) || v <= r.UtilPercent) continue;
            r.UtilPercent = Math.Min(100, v);
            int i = eng.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            r.TopEngine = i >= 0 ? eng[(i + 8)..] : eng;
        }
        reading.WatchUtilPercent = watchEngine.Count == 0 ? 0 : Math.Min(100, watchEngine.Values.Max());
    }

    // "pid_42724_luid_0x00000000_0x0001018c_phys_0_eng_0_engtype_3d"
    private static bool ParseEngineInstance(string name, out int pid, out string luid, out string eng)
    {
        pid = 0; luid = eng = "";
        int l = name.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
        int p = name.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
        int e = name.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase);
        if (!name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase) || l < 0 || p < l || e < p) return false;
        if (!int.TryParse(name.AsSpan(4, l - 4), out pid)) return false;
        luid = name[(l + 1)..p];
        eng = name[(e + 1)..];
        return true;
    }

    // ---- memory ------------------------------------------------------------------------------

    private void SampleAdapterMemory(Dictionary<string, GpuAdapterReading> byLuid)
    {
        if (_adapterMemCat is null) return;
        try
        {
            var data = _adapterMemCat.ReadCategory();
            foreach (var (counter, apply) in new (string, Action<GpuAdapterReading, long>)[]
                     {
                         ("Dedicated Usage", (r, v) => r.DedicatedUsed += v),
                         ("Shared Usage", (r, v) => r.SharedUsed += v),
                     })
            {
                if (!data.Contains(counter)) continue;
                foreach (InstanceData inst in data[counter]!.Values)
                {
                    int p = inst.InstanceName.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
                    string luid = p > 0 ? inst.InstanceName[..p] : inst.InstanceName;
                    if (byLuid.TryGetValue(luid, out var r)) apply(r, inst.RawValue);
                }
            }
        }
        catch { }
    }

    private void SampleProcessMemory(GpuReading reading, IReadOnlyCollection<int> watchPids)
    {
        if (_processMemCat is null || watchPids.Count == 0) return;
        try
        {
            var data = _processMemCat.ReadCategory();
            reading.WatchDedicated = SumFor(data, "Dedicated Usage", watchPids);
            reading.WatchShared = SumFor(data, "Shared Usage", watchPids);
        }
        catch { }

        static long SumFor(InstanceDataCollectionCollection data, string counter, IReadOnlyCollection<int> pids)
        {
            long sum = 0;
            if (!data.Contains(counter)) return 0;
            foreach (InstanceData inst in data[counter]!.Values)
            {
                string n = inst.InstanceName;   // "pid_1234_luid_0x..._phys_0"
                int l = n.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
                if (l > 4 && int.TryParse(n.AsSpan(4, l - 4), out int pid) && pids.Contains(pid)) sum += inst.RawValue;
            }
            return sum;
        }
    }

    private static PerformanceCounterCategory? TryCategory(string name)
    {
        try { return PerformanceCounterCategory.Exists(name) ? new PerformanceCounterCategory(name) : null; }
        catch { return null; }
    }

    // ---- temperature / fan / power (D3DKMT) ----------------------------------------------------

    private static void ReadPerfData(GpuAdapter a, GpuAdapterReading r)
    {
        if (a.KmtHandle == 0) return;
        var perf = new D3DKMT_ADAPTER_PERFDATA();
        var q = new D3DKMT_QUERYADAPTERINFO
        {
            hAdapter = a.KmtHandle,
            Type = KMTQAITYPE_ADAPTERPERFDATA,
            pPrivateDriverData = (IntPtr)(&perf),
            PrivateDriverDataSize = (uint)sizeof(D3DKMT_ADAPTER_PERFDATA),
        };
        if (D3DKMTQueryAdapterInfo(ref q) != 0) return;
        // Drivers that do not implement a sensor report 0.
        if (perf.Temperature > 0) r.TemperatureC = perf.Temperature / 10.0;   // deci-Celsius
        if (perf.FanRPM > 0) r.FanRpm = (int)perf.FanRPM;
        if (perf.Power > 0) r.PowerPercent = perf.Power / 10.0;               // tenths of a percent of max
    }

    private static uint OpenKmt(string luidKey)
    {
        try
        {
            // luid_0xHHHHHHHH_0xLLLLLLLL
            var parts = luidKey.Split('_');
            var open = new D3DKMT_OPENADAPTERFROMLUID
            {
                AdapterLuid = new LUID
                {
                    HighPart = (int)Convert.ToUInt32(parts[1], 16),
                    LowPart = Convert.ToUInt32(parts[2], 16),
                },
            };
            return D3DKMTOpenAdapterFromLuid(ref open) == 0 ? open.hAdapter : 0;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        foreach (var a in Adapters)
        {
            if (a.KmtHandle == 0) continue;
            var c = new D3DKMT_CLOSEADAPTER { hAdapter = a.KmtHandle };
            D3DKMTCloseAdapter(ref c);
            a.KmtHandle = 0;
        }
    }

    // ---- adapter enumeration (DXGI + D3D12 for the UMA flag) -----------------------------------

    private static List<GpuAdapter> EnumerateAdapters()
    {
        var list = new List<GpuAdapter>();
        var seen = new HashSet<string>();
        try
        {
            var iidFactory = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
            if (CreateDXGIFactory1(in iidFactory, out IntPtr factory) < 0) return list;
            try
            {
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(void***)factory)[12];
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr adapter;
                    if (enumAdapters1(factory, i, &adapter) < 0) break;
                    try
                    {
                        DXGI_ADAPTER_DESC1 desc;
                        var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)(*(void***)adapter)[10];
                        if (getDesc1(adapter, &desc) < 0) continue;
                        const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
                        if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || desc.VendorId == 0x1414) continue;
                        string luid = $"luid_0x{(uint)desc.AdapterLuid.HighPart:x8}_0x{desc.AdapterLuid.LowPart:x8}";
                        if (!seen.Add(luid)) continue;
                        list.Add(new GpuAdapter
                        {
                            Name = new string(desc.Description).Trim(),
                            LuidKey = luid,
                            VendorId = desc.VendorId,
                            DedicatedTotal = desc.DedicatedVideoMemory,
                            SharedTotal = desc.SharedSystemMemory,
                            Integrated = QueryUma(adapter),
                        });
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            finally { Marshal.Release(factory); }
        }
        catch { }
        return list;
    }

    private static bool? QueryUma(IntPtr adapter)
    {
        try
        {
            var iidDevice = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");   // ID3D12Device
            const int D3D_FEATURE_LEVEL_11_0 = 0xb000;
            if (D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, in iidDevice, out IntPtr device) < 0) return null;
            try
            {
                var arch = new D3D12_FEATURE_DATA_ARCHITECTURE();
                const int D3D12_FEATURE_ARCHITECTURE = 1;
                var check = (delegate* unmanaged[Stdcall]<IntPtr, int, void*, uint, int>)(*(void***)device)[13];
                if (check(device, D3D12_FEATURE_ARCHITECTURE, &arch, (uint)sizeof(D3D12_FEATURE_DATA_ARCHITECTURE)) < 0) return null;
                return arch.UMA != 0;
            }
            finally { Marshal.Release(device); }
        }
        catch { return null; }
    }

    // ---- interop -----------------------------------------------------------------------------

    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public LUID AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D12_FEATURE_DATA_ARCHITECTURE { public uint NodeIndex; public int TileBasedRenderer, UMA, CacheCoherentUMA; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMLUID { public LUID AdapterLuid; public uint hAdapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
        public uint FanRPM, Power, Temperature;
        public byte PowerStateOverride;
    }

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);
    [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(IntPtr adapter, int minFeatureLevel, in Guid riid, out IntPtr device);
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO p);
}
