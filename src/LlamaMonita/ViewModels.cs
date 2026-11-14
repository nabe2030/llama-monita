using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using LlamaMonita.Hardware;
using LlamaMonita.Llama;

namespace LlamaMonita;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class History
{
    private readonly Queue<double> _q = new();

    public double[] Push(double v)
    {
        _q.Enqueue(v);
        while (_q.Count > Sparkline.Capacity) _q.Dequeue();
        return _q.ToArray();
    }
}

public sealed class GpuVm : Observable
{
    private readonly History _history = new();
    private string _name = "", _kind = "GPU", _utilText = "0", _engine = "", _dedText = "", _sharedText = "", _sensors = "";
    private double _dedFrac, _sharedFrac;
    private double[] _util = Array.Empty<double>();

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Kind { get => _kind; set => Set(ref _kind, value); }
    public string UtilText { get => _utilText; set => Set(ref _utilText, value); }
    public string Engine { get => _engine; set => Set(ref _engine, value); }
    public string DedicatedText { get => _dedText; set => Set(ref _dedText, value); }
    public string SharedText { get => _sharedText; set => Set(ref _sharedText, value); }
    public double DedicatedFraction { get => _dedFrac; set => Set(ref _dedFrac, value); }
    public double SharedFraction { get => _sharedFrac; set => Set(ref _sharedFrac, value); }
    public string Sensors { get => _sensors; set => Set(ref _sensors, value); }
    public double[] UtilHistory { get => _util; set => Set(ref _util, value); }

    public void Apply(GpuAdapterReading r)
    {
        var a = r.Adapter;
        Name = a.Name;
        Kind = a.Integrated switch { true => "iGPU", false => "dGPU", _ => "GPU" };
        UtilText = r.UtilPercent.ToString("0");
        Engine = r.UtilPercent >= 1 && r.TopEngine.Length > 0 ? r.TopEngine : "";
        DedicatedText = $"{Fmt.Gb(r.DedicatedUsed)} / {Fmt.Gb((long)a.DedicatedTotal)} GB";
        SharedText = $"{Fmt.Gb(r.SharedUsed)} / {Fmt.Gb((long)a.SharedTotal)} GB";
        DedicatedFraction = a.DedicatedTotal > 0 ? Math.Clamp(r.DedicatedUsed / (double)a.DedicatedTotal, 0, 1) : 0;
        SharedFraction = a.SharedTotal > 0 ? Math.Clamp(r.SharedUsed / (double)a.SharedTotal, 0, 1) : 0;
        var parts = new List<string>();
        if (r.TemperatureC is { } t) parts.Add($"{t:0} °C");
        if (r.FanRpm is { } f) parts.Add($"{f:N0} rpm");
        if (r.PowerPercent is { } p) parts.Add($"power {p:0}%");
        Sensors = parts.Count > 0 ? string.Join("   ·   ", parts) : "temperature / fan / power: not reported by driver";
        UtilHistory = _history.Push(r.UtilPercent);
    }
}

public sealed class SlotVm : Observable
{
    private string _title = "", _state = "", _detail = "";
    private double _fraction;
    private Brush _stateBrush = Palette.Muted;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string State { get => _state; set => Set(ref _state, value); }
    public Brush StateBrush { get => _stateBrush; set => Set(ref _stateBrush, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public double Fraction { get => _fraction; set => Set(ref _fraction, value); }
}

public sealed class ModelVm : Observable
{
    private string _id = "", _status = "";
    private Brush _brush = Palette.Muted;

    public string Id { get => _id; set => Set(ref _id, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public Brush Brush { get => _brush; set => Set(ref _brush, value); }
}

public static class Palette
{
    public static readonly Brush Green = Frozen(0x4A, 0xDE, 0x80);
    public static readonly Brush Blue = Frozen(0x60, 0xA5, 0xFA);
    public static readonly Brush Amber = Frozen(0xFB, 0xBF, 0x24);
    public static readonly Brush Red = Frozen(0xF8, 0x71, 0x71);
    public static readonly Brush Muted = Frozen(0x8B, 0x93, 0xA1);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

public static class Fmt
{
    public static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.0");
}

public sealed class MainVm : Observable
{
    private sealed class SlotTrack
    {
        public int Task = int.MinValue, Prompt, Decoded, PromptBase, DecodedBase;
        public long Stamp;
    }

    private readonly Dictionary<int, SlotTrack> _tracks = new();
    private readonly History _tpsHistory = new();
    private long _sessionPrompt, _sessionGenerated;

    private string _status = "Connecting…", _serverInfo = "", _modelName = "—", _modelDetail = "", _state = "", _tps = "0.0",
        _session = "", _serverTotals = "", _cpuText = "", _ramText = "", _procText = "", _slotsNote = "";
    private Brush _statusBrush = Palette.Muted, _stateBrush = Palette.Muted;
    private double _cpuFrac, _ramFrac;
    private double[] _tpsValues = Array.Empty<double>();
    private bool _showModels;

    public ObservableCollection<GpuVm> Gpus { get; } = new();
    public ObservableCollection<SlotVm> Slots { get; } = new();
    public ObservableCollection<ModelVm> Models { get; } = new();

    public string Status { get => _status; set => Set(ref _status, value); }
    public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }
    public string ServerInfo { get => _serverInfo; set => Set(ref _serverInfo, value); }
    public string ModelName { get => _modelName; set => Set(ref _modelName, value); }
    public string ModelDetail { get => _modelDetail; set => Set(ref _modelDetail, value); }
    public string State { get => _state; set => Set(ref _state, value); }
    public Brush StateBrush { get => _stateBrush; set => Set(ref _stateBrush, value); }
    public string TokensPerSecond { get => _tps; set => Set(ref _tps, value); }
    public double[] TpsHistory { get => _tpsValues; set => Set(ref _tpsValues, value); }
    public string Session { get => _session; set => Set(ref _session, value); }
    public string ServerTotals { get => _serverTotals; set => Set(ref _serverTotals, value); }
    public string CpuText { get => _cpuText; set => Set(ref _cpuText, value); }
    public double CpuFraction { get => _cpuFrac; set => Set(ref _cpuFrac, value); }
    public string RamText { get => _ramText; set => Set(ref _ramText, value); }
    public double RamFraction { get => _ramFrac; set => Set(ref _ramFrac, value); }
    public string ProcessText { get => _procText; set => Set(ref _procText, value); }
    public string SlotsNote { get => _slotsNote; set => Set(ref _slotsNote, value); }
    public bool ShowModels { get => _showModels; set => Set(ref _showModels, value); }

    public void ResetSession()
    {
        _tracks.Clear();
        _sessionPrompt = _sessionGenerated = 0;
    }

    public void ApplyHardware(GpuReading gpu, SystemReading sys, int llamaProcesses)
    {
        while (Gpus.Count < gpu.Adapters.Count) Gpus.Add(new GpuVm());
        while (Gpus.Count > gpu.Adapters.Count) Gpus.RemoveAt(Gpus.Count - 1);
        for (int i = 0; i < gpu.Adapters.Count; i++) Gpus[i].Apply(gpu.Adapters[i]);

        CpuText = $"{sys.CpuPercent:0}%";
        CpuFraction = sys.CpuPercent / 100.0;
        long used = (long)(sys.RamTotal - sys.RamAvailable);
        RamText = $"{Fmt.Gb(used)} / {Fmt.Gb((long)sys.RamTotal)} GB";
        RamFraction = sys.RamTotal > 0 ? used / (double)sys.RamTotal : 0;
        ProcessText = llamaProcesses == 0
            ? "No local llama-server process (remote server, or not running)"
            : $"{llamaProcesses} local llama-server process{(llamaProcesses > 1 ? "es" : "")}   ·   GPU {gpu.WatchUtilPercent:0}%   ·   " +
              $"{Fmt.Gb(gpu.WatchShared)} GB shared + {Fmt.Gb(gpu.WatchDedicated)} GB dedicated";
    }

    public void ApplyServer(LlamaSnapshot s)
    {
        if (!s.Reachable)
        {
            Status = "Disconnected" + (s.Error is null ? "" : $" — {s.Error}");
            StatusBrush = Palette.Red;
            ServerInfo = "";
            ModelName = "—";
            ModelDetail = "";
            State = "Server not reachable";
            StateBrush = Palette.Red;
            Slots.Clear();
            Models.Clear();
            ShowModels = false;
            SlotsNote = "";
            PushTps(0);
            return;
        }

        Status = "Connected";
        StatusBrush = Palette.Green;
        ServerInfo = (s.IsRouter ? "router mode" : "single model") + (s.BuildInfo is null ? "" : $"   ·   {s.BuildInfo}");

        // models (router mode)
        ShowModels = s.IsRouter;
        while (Models.Count < s.Models.Count) Models.Add(new ModelVm());
        while (Models.Count > s.Models.Count) Models.RemoveAt(Models.Count - 1);
        for (int i = 0; i < s.Models.Count; i++)
        {
            Models[i].Id = s.Models[i].Id;
            Models[i].Status = s.Models[i].Status;
            Models[i].Brush = s.Models[i].Status switch { "loaded" => Palette.Green, "loading" => Palette.Amber, _ => Palette.Muted };
        }

        ModelName = string.IsNullOrEmpty(s.ActiveModel) ? "No model loaded" : s.ActiveModel;
        var detail = new List<string>();
        if (!string.IsNullOrEmpty(s.Ftype)) detail.Add(s.Ftype);
        if (s.NCtxPerSlot > 0) detail.Add($"ctx {s.NCtxPerSlot:N0}" + (s.TotalSlots > 1 ? $" × {s.TotalSlots} slots" : ""));
        if (s.Vision) detail.Add("vision");
        ModelDetail = string.Join("   ·   ", detail);

        // slots and throughput
        double tps = 0;
        long now = Stopwatch.GetTimestamp();
        while (Slots.Count < s.Slots.Count) Slots.Add(new SlotVm());
        while (Slots.Count > s.Slots.Count) Slots.RemoveAt(Slots.Count - 1);
        long livePrompt = 0, liveGenerated = 0;
        for (int i = 0; i < s.Slots.Count; i++)
        {
            var slot = s.Slots[i];
            if (!_tracks.TryGetValue(slot.Id, out var t))
            {
                // First sight: whatever the slot already holds predates this session.
                t = new SlotTrack { Task = slot.TaskId, Prompt = slot.PromptTokens, Decoded = slot.Decoded, PromptBase = slot.PromptTokens, DecodedBase = slot.Decoded, Stamp = now };
                _tracks[slot.Id] = t;
            }
            else if (slot.TaskId != t.Task || slot.Decoded < t.Decoded)
            {
                _sessionPrompt += t.Prompt - t.PromptBase;
                _sessionGenerated += t.Decoded - t.DecodedBase;
                t.Task = slot.TaskId;
                t.Prompt = t.Decoded = t.PromptBase = t.DecodedBase = 0;
                t.Stamp = now;
            }

            double seconds = Stopwatch.GetElapsedTime(t.Stamp, now).TotalSeconds;
            if (slot.IsProcessing && slot.Decoded > t.Decoded && seconds > 0.2)
                tps += (slot.Decoded - t.Decoded) / seconds;
            t.Prompt = Math.Max(t.Prompt, slot.PromptTokens);
            t.Decoded = slot.Decoded;
            t.Stamp = now;
            livePrompt += t.Prompt - t.PromptBase;
            liveGenerated += t.Decoded - t.DecodedBase;

            var vm = Slots[i];
            vm.Title = $"Slot {slot.Id}";
            vm.State = slot.IsProcessing ? "generating" : "idle";
            vm.StateBrush = slot.IsProcessing ? Palette.Green : Palette.Muted;
            int usedCtx = slot.PromptTokens + slot.Decoded;
            vm.Detail = $"{usedCtx:N0} / {slot.NCtx:N0} tokens" + (slot.Speculative ? "   ·   speculative" : "");
            vm.Fraction = slot.NCtx > 0 ? Math.Clamp(usedCtx / (double)slot.NCtx, 0, 1) : 0;
        }
        SlotsNote = s.SlotsAvailable ? "" : (s.ActiveModel is null || s.Loading ? "" : "/slots is disabled on this server (--no-slots)");

        bool busy = s.Slots.Any(x => x.IsProcessing);
        (State, StateBrush) = s switch
        {
            { Loading: true } => ("Loading model…", Palette.Amber),
            { ActiveModel: null or "" } => ("Idle — no model loaded", Palette.Muted),
            { Sleeping: true } => ("Sleeping", Palette.Muted),
            _ when busy => ("Generating", Palette.Green),
            _ => ("Idle", Palette.Muted),
        };

        PushTps(tps);
        Session = $"This session:   {_sessionPrompt + livePrompt:N0} prompt   ·   {_sessionGenerated + liveGenerated:N0} generated tokens";
        if (s.Metrics is { } m)
        {
            m.TryGetValue("prompt_tokens_total", out double pt);
            m.TryGetValue("tokens_predicted_total", out double gt);
            m.TryGetValue("predicted_tokens_seconds", out double avg);
            ServerTotals = $"Server total:   {pt:N0} prompt   ·   {gt:N0} generated   ·   avg {avg:0.0} tok/s";
        }
        else
        {
            ServerTotals = "Server totals: start llama-server with --metrics to enable";
        }
    }

    private void PushTps(double tps)
    {
        TokensPerSecond = tps.ToString("0.0");
        TpsHistory = _tpsHistory.Push(tps);
    }
}
