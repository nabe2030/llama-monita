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
        while (_q.Count > HistoryChart.Capacity) _q.Dequeue();
        return _q.ToArray();
    }
}

public sealed class GpuVm : Observable
{
    private readonly History _history = new();
    private string _name = "", _kind = "GPU", _utilText = "0", _engine = "", _dedText = "", _sharedText = "",
        _temp = "", _fan = "", _power = "";
    private double _dedFrac, _sharedFrac;
    private bool _hasTemp, _hasFan, _hasPower;
    private double[] _util = Array.Empty<double>();

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Kind { get => _kind; set => Set(ref _kind, value); }
    public string UtilText { get => _utilText; set => Set(ref _utilText, value); }
    public string Engine { get => _engine; set => Set(ref _engine, value); }
    public string DedicatedText { get => _dedText; set => Set(ref _dedText, value); }
    public string SharedText { get => _sharedText; set => Set(ref _sharedText, value); }
    public double DedicatedFraction { get => _dedFrac; set => Set(ref _dedFrac, value); }
    public double SharedFraction { get => _sharedFrac; set => Set(ref _sharedFrac, value); }
    public string TempText { get => _temp; set => Set(ref _temp, value); }
    public string FanText { get => _fan; set => Set(ref _fan, value); }
    public string PowerText { get => _power; set => Set(ref _power, value); }
    // A sensor tile appears once the driver has reported a value, and then stays.
    public bool HasTemp { get => _hasTemp; set => Set(ref _hasTemp, value); }
    public bool HasFan { get => _hasFan; set => Set(ref _hasFan, value); }
    public bool HasPower { get => _hasPower; set => Set(ref _hasPower, value); }
    public double[] UtilHistory { get => _util; set => Set(ref _util, value); }

    public void Apply(GpuAdapterReading r)
    {
        var a = r.Adapter;
        Name = a.Name;
        Kind = a.Integrated switch { true => "iGPU", false => "dGPU", _ => "GPU" };
        UtilText = r.UtilPercent.ToString("0");
        Engine = r.UtilPercent >= 1 ? PrettyEngine(r.TopEngine) : "";
        DedicatedText = $"{Fmt.Gb(r.DedicatedUsed)} / {Fmt.Gb((long)a.DedicatedTotal)} GB";
        SharedText = $"{Fmt.Gb(r.SharedUsed)} / {Fmt.Gb((long)a.SharedTotal)} GB";
        DedicatedFraction = a.DedicatedTotal > 0 ? Math.Clamp(r.DedicatedUsed / (double)a.DedicatedTotal, 0, 1) : 0;
        SharedFraction = a.SharedTotal > 0 ? Math.Clamp(r.SharedUsed / (double)a.SharedTotal, 0, 1) : 0;
        if (r.TemperatureC is { } t) { TempText = $"{t:0} °C"; HasTemp = true; } else TempText = "—";
        if (r.FanRpm is { } f) { FanText = $"{f:N0} rpm"; HasFan = true; } else FanText = "—";
        if (r.PowerPercent is { } p) { PowerText = $"{p:0}%"; HasPower = true; } else PowerText = "—";
        UtilHistory = _history.Push(r.UtilPercent);
    }

    // "Compute 0" -> "Compute", "3d" -> "3D", "videodecode" -> "Video decode"
    private static string PrettyEngine(string raw)
    {
        string s = raw.TrimEnd(' ', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ToLowerInvariant();
        return s switch
        {
            "" => "",
            "3d" => "3D",
            "videodecode" => "Video decode",
            "videoencode" => "Video encode",
            "videoprocessing" => "Video processing",
            _ => char.ToUpperInvariant(s[0]) + s[1..],
        };
    }
}

public sealed class SlotVm : Observable
{
    private string _title = "", _detail = "";
    private double _fraction;
    private Brush _stateBrush = Palette.Muted;

    public string Title { get => _title; set => Set(ref _title, value); }
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
    private readonly History _tpsHistory = new(), _cpuHistory = new(), _ramHistory = new();
    private long _sessionPrompt, _sessionGenerated;
    private const double SpeedWindowSeconds = 5;
    private readonly Queue<(long Stamp, long Generated)> _genWindow = new();

    private string _status = "Connecting…", _serverInfo = "", _modelName = "—", _modelDetail = "", _state = "", _tps = "0.0",
        _sessionPromptText = "0", _sessionGeneratedText = "0", _serverTotals = "", _cpuText = "", _ramText = "", _procText = "",
        _slotsNote = "", _title = "LlamaMonita";
    private Brush _statusBrush = Palette.Muted, _stateBrush = Palette.Muted;
    private double[] _tpsValues = Array.Empty<double>(), _cpuValues = Array.Empty<double>(), _ramValues = Array.Empty<double>();
    private bool _showModels, _showTopBar = true, _showInfo = true, _showTiles = true, _showFooter = true;

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
    public string SessionPrompt { get => _sessionPromptText; set => Set(ref _sessionPromptText, value); }
    public string SessionGenerated { get => _sessionGeneratedText; set => Set(ref _sessionGeneratedText, value); }
    public string ServerTotals { get => _serverTotals; set => Set(ref _serverTotals, value); }
    public string CpuText { get => _cpuText; set => Set(ref _cpuText, value); }
    public double[] CpuHistory { get => _cpuValues; set => Set(ref _cpuValues, value); }
    public string RamText { get => _ramText; set => Set(ref _ramText, value); }
    public double[] RamHistory { get => _ramValues; set => Set(ref _ramValues, value); }
    public string ProcessText { get => _procText; set => Set(ref _procText, value); }
    public string SlotsNote { get => _slotsNote; set => Set(ref _slotsNote, value); }
    public bool ShowModels { get => _showModels; set => Set(ref _showModels, value); }
    public string WindowTitle { get => _title; set => Set(ref _title, value); }

    // Responsive layout switches, set by the window from its current size.
    public bool ShowTopBar { get => _showTopBar; set => Set(ref _showTopBar, value); }
    public bool ShowInfo { get => _showInfo; set => Set(ref _showInfo, value); }
    public bool ShowTiles { get => _showTiles; set => Set(ref _showTiles, value); }
    public bool ShowFooter { get => _showFooter; set => Set(ref _showFooter, value); }

    public void ResetSession()
    {
        _tracks.Clear();
        _genWindow.Clear();
        _sessionPrompt = _sessionGenerated = 0;
    }

    public void ApplyHardware(GpuReading gpu, SystemReading sys, int llamaProcesses)
    {
        while (Gpus.Count < gpu.Adapters.Count) Gpus.Add(new GpuVm());
        while (Gpus.Count > gpu.Adapters.Count) Gpus.RemoveAt(Gpus.Count - 1);
        for (int i = 0; i < gpu.Adapters.Count; i++) Gpus[i].Apply(gpu.Adapters[i]);

        CpuText = $"{sys.CpuPercent:0}%";
        CpuHistory = _cpuHistory.Push(sys.CpuPercent);
        long used = (long)(sys.RamTotal - sys.RamAvailable);
        RamText = $"{Fmt.Gb(used)} / {Fmt.Gb((long)sys.RamTotal)} GB";
        RamHistory = _ramHistory.Push(sys.RamTotal > 0 ? 100.0 * used / sys.RamTotal : 0);
        ProcessText = llamaProcesses == 0
            ? "No local llama-server process"
            : $"llama-server ×{llamaProcesses}:  GPU {gpu.WatchUtilPercent:0}%  ·  {Fmt.Gb(gpu.WatchShared)} GB shared + {Fmt.Gb(gpu.WatchDedicated)} GB dedicated";
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
            WindowTitle = "LlamaMonita — disconnected";
            return;
        }

        Status = "Connected";
        StatusBrush = Palette.Green;
        ServerInfo = (s.IsRouter ? "router mode" : "single model") + (s.BuildInfo is null ? "" : $"  ·  {s.BuildInfo}");

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

        bool hasModel = !string.IsNullOrEmpty(s.ActiveModel);
        ModelName = hasModel ? s.ActiveModel! : "No model loaded";
        var detail = new List<string>();
        if (!string.IsNullOrEmpty(s.Ftype)) detail.Add(s.Ftype);
        if (s.NCtxPerSlot > 0) detail.Add($"ctx {s.NCtxPerSlot:N0}" + (s.TotalSlots > 1 ? $" × {s.TotalSlots}" : ""));
        if (s.Vision) detail.Add("vision");
        ModelDetail = string.Join("  ·  ", detail);

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

            t.Prompt = Math.Max(t.Prompt, slot.PromptTokens);
            t.Decoded = slot.Decoded;
            t.Stamp = now;
            livePrompt += t.Prompt - t.PromptBase;
            liveGenerated += t.Decoded - t.DecodedBase;

            var vm = Slots[i];
            vm.Title = slot.Id.ToString();
            vm.StateBrush = slot.IsProcessing ? Palette.Green : Palette.Muted;
            int usedCtx = slot.PromptTokens + slot.Decoded;
            vm.Detail = $"{usedCtx:N0} / {slot.NCtx:N0}";
            vm.Fraction = slot.NCtx > 0 ? Math.Clamp(usedCtx / (double)slot.NCtx, 0, 1) : 0;
        }
        SlotsNote = s.SlotsAvailable || !hasModel || s.Loading ? "" : "/slots is disabled on this server";

        bool busy = s.Slots.Any(x => x.IsProcessing);

        // Generation speed over a sliding window: /slots advances in bursts (more so with speculative
        // decoding), so a one-second difference is too noisy to read.
        long totalGenerated = _sessionGenerated + liveGenerated;
        _genWindow.Enqueue((now, totalGenerated));
        while (_genWindow.Count > (busy ? 2 : 1) &&
               (!busy || Stopwatch.GetElapsedTime(_genWindow.Peek().Stamp, now).TotalSeconds > SpeedWindowSeconds))
            _genWindow.Dequeue();
        if (busy && _genWindow.Count > 1)
        {
            var first = _genWindow.Peek();
            double span = Stopwatch.GetElapsedTime(first.Stamp, now).TotalSeconds;
            if (span > 0.5) tps = (totalGenerated - first.Generated) / span;
        }

        (State, StateBrush) = s switch
        {
            { Loading: true } => ("Loading model…", Palette.Amber),
            _ when !hasModel => ("No model loaded", Palette.Muted),
            { Sleeping: true } => ("Sleeping", Palette.Muted),
            _ when busy => ("Generating", Palette.Green),
            _ => ("Idle", Palette.Muted),
        };

        PushTps(tps);
        SessionPrompt = (_sessionPrompt + livePrompt).ToString("N0");
        SessionGenerated = (_sessionGenerated + liveGenerated).ToString("N0");
        if (s.Metrics is { } m)
        {
            m.TryGetValue("prompt_tokens_total", out double pt);
            m.TryGetValue("tokens_predicted_total", out double gt);
            m.TryGetValue("predicted_tokens_seconds", out double avg);
            ServerTotals = $"Server total:  {pt:N0} prompt  ·  {gt:N0} generated  ·  avg {avg:0.0} tok/s";
        }
        else
        {
            ServerTotals = "Server totals need --metrics";
        }
        WindowTitle = hasModel ? $"LlamaMonita — {ModelName} · {TokensPerSecond} tok/s" : "LlamaMonita";
    }

    private void PushTps(double tps)
    {
        TokensPerSecond = tps.ToString("0.0");
        TpsHistory = _tpsHistory.Push(tps);
    }
}
