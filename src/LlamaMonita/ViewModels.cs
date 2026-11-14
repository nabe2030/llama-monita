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
    private Brush _brush = Palette.Blue;

    /// <summary>Colour of this adapter's line in the combined graph.</summary>
    public Brush Brush { get => _brush; set => Set(ref _brush, value); }
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
        Engine = r.UtilPercent >= 1 && r.TopEngine.Length > 0 ? PrettyEngine(r.TopEngine) : "—";
        DedicatedText = $"{Fmt.Gb(r.DedicatedUsed)} / {Fmt.Gb((long)a.DedicatedTotal)} GB";
        SharedText = $"{Fmt.Gb(r.SharedUsed)} / {Fmt.Gb((long)a.SharedTotal)} GB";
        DedicatedFraction = a.DedicatedTotal > 0 ? Math.Clamp(r.DedicatedUsed / (double)a.DedicatedTotal, 0, 1) : 0;
        SharedFraction = a.SharedTotal > 0 ? Math.Clamp(r.SharedUsed / (double)a.SharedTotal, 0, 1) : 0;
        if (r.TemperatureC is { } t) { TempText = $"{t:0} °C"; HasTemp = true; } else TempText = "—";
        if (r.FanRpm is { } f) { FanText = $"{f:N0} rpm"; HasFan = true; } else FanText = "—";
        // Integrated GPUs report a power figure that does not follow load (5% at full load on a Radeon 780M),
        // so it is only shown for discrete adapters.
        if (r.PowerPercent is { } p && a.Integrated != true) { PowerText = $"{p:0}%"; HasPower = true; } else PowerText = "—";
        UtilHistory = _history.Push(r.UtilPercent);
    }

    // "Compute 0" -> "Compute", "3d" -> "3D", "videodecode" -> "Video decode"
    private static string PrettyEngine(string raw)
    {
        string s = raw.TrimEnd(' ', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ToLowerInvariant();
        // "High priority 3D" / "High priority compute" are the same engines at a higher scheduling class.
        if (s.StartsWith("high priority ")) s = s["high priority ".Length..];
        else if (s.StartsWith("highpriority")) s = s["highpriority".Length..];
        s = s.Trim();
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

public sealed class RequestVm : Observable
{
    private string _time = "", _from = "", _model = "", _slot = "", _prompt = "", _generated = "", _duration = "", _speed = "";
    private Brush _brush = Palette.Green;

    public bool Done { get; set; }
    public string Time { get => _time; set => Set(ref _time, value); }
    public string From { get => _from; set => Set(ref _from, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string Slot { get => _slot; set => Set(ref _slot, value); }
    public string Prompt { get => _prompt; set => Set(ref _prompt, value); }
    public string Generated { get => _generated; set => Set(ref _generated, value); }
    public string Duration { get => _duration; set => Set(ref _duration, value); }
    public string Speed { get => _speed; set => Set(ref _speed, value); }
    public Brush Brush { get => _brush; set => Set(ref _brush, value); }
}

public sealed class ModelVm : Observable
{
    private string _id = "", _status = "", _detail = "";
    private Brush _brush = Palette.Muted;

    public string Id { get => _id; set => Set(ref _id, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    /// <summary>Quantization, context size and so on; only filled in for the loaded model.</summary>
    public string Detail { get => _detail; set { Set(ref _detail, value); HasDetail = value.Length > 0; } }
    private bool _hasDetail;
    public bool HasDetail { get => _hasDetail; private set => Set(ref _hasDetail, value); }
    public Brush Brush { get => _brush; set => Set(ref _brush, value); }
}

public static class Palette
{
    public static readonly Brush Green = Frozen(0x4A, 0xDE, 0x80);
    public static readonly Brush Blue = Frozen(0x60, 0xA5, 0xFA);
    public static readonly Brush Amber = Frozen(0xFB, 0xBF, 0x24);
    public static readonly Brush Red = Frozen(0xF8, 0x71, 0x71);
    public static readonly Brush Muted = Frozen(0x8B, 0x93, 0xA1);
    public static readonly Brush Purple = Frozen(0xA7, 0x8B, 0xFA);
    public static readonly Brush Pink = Frozen(0xF4, 0x72, 0xB6);

    /// <summary>Line colours for GPU adapters, in adapter order. Green is reserved for generation speed.</summary>
    public static readonly Brush[] GpuSeries = { Blue, Purple, Amber, Pink };

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
        public int Task = int.MinValue, Prompt, Generated, LastDecoded;
        public bool Counted;      // false for a task that had already finished when we first saw the slot
        public bool SawDecode;    // generation was observed, so Prompt is the final prompt size
        public long StartStamp, FirstDecodeStamp;
        public DateTime StartedAt = DateTime.Now;
        public double Speed;
        public RequestVm? Request;
    }

    private const int MaxRequests = 200;
    private readonly Dictionary<int, SlotTrack> _tracks = new();
    private readonly History _tpsHistory = new(), _cpuHistory = new(), _ramHistory = new();
    private long _sessionPrompt, _sessionGenerated, _speedTokens;
    private const double SpeedWindowSeconds = 5;
    private readonly Queue<(long Stamp, long Generated)> _genWindow = new();

    /// <summary>Requests seen since this window connected, newest first.</summary>
    public ObservableCollection<RequestVm> Requests { get; } = new();
    private bool _showRequests;
    public bool ShowRequests { get => _showRequests; set => Set(ref _showRequests, value); }
    private string _requestsNote = "";
    public string RequestsNote { get => _requestsNote; set => Set(ref _requestsNote, value); }

    private string _status = "Connecting…", _serverInfo = "", _modelName = "—", _modelDetail = "", _state = "", _tps = "0.0",
        _sessionPromptText = "0", _sessionGeneratedText = "0", _serverTotals = "", _cpuText = "", _ramText = "", _procText = "",
        _slotsNote = "", _title = "LlamaMonita";
    private Brush _statusBrush = Palette.Muted, _stateBrush = Palette.Muted;
    private double[] _tpsValues = Array.Empty<double>(), _cpuValues = Array.Empty<double>(), _ramValues = Array.Empty<double>();
    private bool _showModels, _showTopBar = true, _showInfo = true, _showTiles = true, _showTokenTiles = true, _showFooter = true;

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
    private IReadOnlyList<ChartSeries> _heroSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> HeroSeries { get => _heroSeries; set => Set(ref _heroSeries, value); }
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
    public bool ShowTokenTiles { get => _showTokenTiles; set => Set(ref _showTokenTiles, value); }
    public bool ShowFooter { get => _showFooter; set => Set(ref _showFooter, value); }

    public void ResetSession()
    {
        _tracks.Clear();
        _genWindow.Clear();
        Requests.Clear();
        _sessionPrompt = _sessionGenerated = _speedTokens = 0;
    }

    private RequestVm StartRequest(SlotInfo slot, IReadOnlyList<string>? clients)
    {
        var r = new RequestVm { Time = DateTime.Now.ToString("HH:mm:ss"), Slot = slot.Id.ToString(), From = FromText(clients), Model = _activeModel };
        Requests.Insert(0, r);
        while (Requests.Count > MaxRequests) Requests.RemoveAt(Requests.Count - 1);
        return r;
    }

    // Clients that were already connected while the server was idle (an open chat UI, another monitor).
    // A request is attributed to the connections that appeared with it; if none did, to all of them.
    private IReadOnlyList<string> _idleClients = Array.Empty<string>();
    private string _activeModel = "";

    /// <summary>Durable request log; null when logging is off (screenshot runs).</summary>
    public RequestLog? Log { get; set; }
    public string ServerUrl { get; set; } = "";
    private string _todayText = "", _dailyTooltip = "";
    private string _modelsTitle = "MODEL", _metricsNote = "";
    public string ModelsTitle { get => _modelsTitle; set => Set(ref _modelsTitle, value); }
    public string MetricsNote { get => _metricsNote; set => Set(ref _metricsNote, value); }

    // Finished requests since this window started, by (model, client): the counters served on /metrics.
    private readonly Dictionary<(string Model, string From), (long Requests, long Prompt, long Generated, double Seconds)> _totals = new();
    private double _lastTps;

    /// <summary>Prometheus exposition text for the optional exporter.</summary>
    public string BuildMetrics(GpuReading gpu, SystemReading sys, LlamaSnapshot server)
    {
        var sb = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        static string L(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
        void Head(string name, string type, string help) => sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append("\n# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        void Row(string name, string labels, double value) => sb.Append(name).Append(labels.Length > 0 ? "{" + labels + "}" : "").Append(' ').Append(value.ToString("0.###", inv)).Append('\n');

        Head("llamamonita_info", "gauge", "LlamaMonita build information.");
        Row("llamamonita_info", $"version=\"{L(App.Version)}\"", 1);
        Head("llamamonita_server_up", "gauge", "1 when llama-server answered the last poll.");
        Row("llamamonita_server_up", $"server=\"{L(ServerUrl)}\"", server.Reachable ? 1 : 0);
        Head("llamamonita_model_loaded", "gauge", "1 for the model that is currently loaded.");
        if (!string.IsNullOrEmpty(server.ActiveModel) && !server.Loading) Row("llamamonita_model_loaded", $"model=\"{L(server.ActiveModel!)}\"", 1);
        Head("llamamonita_slots_processing", "gauge", "Slots that are processing a request.");
        Row("llamamonita_slots_processing", "", server.Slots.Count(x => x.IsProcessing));
        Head("llamamonita_generation_tokens_per_second", "gauge", "Generation speed over the last few seconds.");
        Row("llamamonita_generation_tokens_per_second", "", _lastTps);

        Head("llamamonita_requests_total", "counter", "Requests finished since LlamaMonita started.");
        foreach (var (k, v) in _totals) Row("llamamonita_requests_total", $"model=\"{L(k.Model)}\",from=\"{L(k.From)}\"", v.Requests);
        Head("llamamonita_prompt_tokens_total", "counter", "Prompt tokens of finished requests.");
        foreach (var (k, v) in _totals) Row("llamamonita_prompt_tokens_total", $"model=\"{L(k.Model)}\",from=\"{L(k.From)}\"", v.Prompt);
        Head("llamamonita_generated_tokens_total", "counter", "Generated tokens of finished requests.");
        foreach (var (k, v) in _totals) Row("llamamonita_generated_tokens_total", $"model=\"{L(k.Model)}\",from=\"{L(k.From)}\"", v.Generated);
        Head("llamamonita_request_seconds_total", "counter", "Wall-clock seconds spent on finished requests.");
        foreach (var (k, v) in _totals) Row("llamamonita_request_seconds_total", $"model=\"{L(k.Model)}\",from=\"{L(k.From)}\"", v.Seconds);

        Head("llamamonita_gpu_utilization_percent", "gauge", "Busiest engine of the adapter.");
        foreach (var a in gpu.Adapters) Row("llamamonita_gpu_utilization_percent", $"gpu=\"{L(a.Adapter.Name)}\"", a.UtilPercent);
        Head("llamamonita_gpu_memory_bytes", "gauge", "GPU memory in use (kind: dedicated or shared).");
        foreach (var a in gpu.Adapters)
        {
            Row("llamamonita_gpu_memory_bytes", $"gpu=\"{L(a.Adapter.Name)}\",kind=\"dedicated\"", a.DedicatedUsed);
            Row("llamamonita_gpu_memory_bytes", $"gpu=\"{L(a.Adapter.Name)}\",kind=\"shared\"", a.SharedUsed);
        }
        Head("llamamonita_gpu_memory_limit_bytes", "gauge", "GPU memory the adapter can use (kind: dedicated or shared).");
        foreach (var a in gpu.Adapters)
        {
            Row("llamamonita_gpu_memory_limit_bytes", $"gpu=\"{L(a.Adapter.Name)}\",kind=\"dedicated\"", a.Adapter.DedicatedTotal);
            Row("llamamonita_gpu_memory_limit_bytes", $"gpu=\"{L(a.Adapter.Name)}\",kind=\"shared\"", a.Adapter.SharedTotal);
        }
        Head("llamamonita_gpu_temperature_celsius", "gauge", "GPU temperature, when the driver reports it.");
        foreach (var a in gpu.Adapters) if (a.TemperatureC is { } c) Row("llamamonita_gpu_temperature_celsius", $"gpu=\"{L(a.Adapter.Name)}\"", c);
        Head("llamamonita_cpu_percent", "gauge", "CPU utilization of this PC.");
        Row("llamamonita_cpu_percent", "", sys.CpuPercent);
        Head("llamamonita_ram_bytes", "gauge", "System memory (kind: used or total).");
        Row("llamamonita_ram_bytes", "kind=\"used\"", sys.RamTotal - sys.RamAvailable);
        Row("llamamonita_ram_bytes", "kind=\"total\"", sys.RamTotal);
        return sb.ToString();
    }
    public string TodayText { get => _todayText; set => Set(ref _todayText, value); }
    public string DailyTooltip { get => _dailyTooltip; set => Set(ref _dailyTooltip, value); }
    private bool _showServerLabel = true;
    public bool ShowServerLabel { get => _showServerLabel; set => Set(ref _showServerLabel, value); }

    /// <summary>Refreshes the "Today" line and the per-day tooltip from the request log.</summary>
    public void RefreshDaily()
    {
        if (Log is null) { TodayText = ""; DailyTooltip = ""; return; }
        var today = Log.Today;
        TodayText = $"Today:  {today.Requests:N0} request{(today.Requests == 1 ? "" : "s")}  ·  {today.Prompt:N0} prompt  ·  {today.Generated:N0} generated";
        var lines = new List<string> { "Requests logged by LlamaMonita, per day", "" };
        foreach (var (day, total) in Log.Recent(14))
            lines.Add($"{day:yyyy-MM-dd}    {total.Requests,5:N0} requests    {total.Prompt,10:N0} prompt    {total.Generated,10:N0} generated");
        if (lines.Count == 2) lines.Add("(nothing logged yet)");
        lines.Add("");
        lines.Add("Right-click the window → Open request log folder");
        DailyTooltip = string.Join("\n", lines);
    }

    private string FromText(IReadOnlyList<string>? clients)
    {
        if (clients is null) return "—";
        var fresh = clients.Where(c => !_idleClients.Contains(c)).ToList();
        var pick = fresh.Count > 0 ? fresh : clients;
        return pick.Count == 0 ? "" : string.Join(", ", pick);
    }

    private void UpdateRequest(SlotTrack t, long now, bool processing, IReadOnlyList<string>? clients)
    {
        if (t.Request is not { } r || r.Done) return;
        // The connection may not have been in the table yet when the task was first seen.
        if (r.From.Length == 0 && clients is { Count: > 0 }) r.From = FromText(clients);
        r.Prompt = t.Prompt > 0 ? t.Prompt.ToString("N0") : "";
        r.Generated = t.Generated > 0 ? t.Generated.ToString("N0") : "";
        double seconds = Stopwatch.GetElapsedTime(t.StartStamp, now).TotalSeconds;
        r.Duration = seconds < 60 ? $"{seconds:0} s" : $"{(int)seconds / 60}:{(int)seconds % 60:00}";
        double genSeconds = t.SawDecode ? Stopwatch.GetElapsedTime(t.FirstDecodeStamp, now).TotalSeconds : 0;
        t.Speed = genSeconds >= 1 && t.Generated > 0 ? t.Generated / genSeconds : 0;
        r.Speed = t.Speed > 0 ? t.Speed.ToString("0.0") : "";
        r.Brush = processing ? Palette.Green : Palette.Muted;
        if (!processing)
        {
            if (r.From.Length == 0) r.From = "?";   // finished before its connection was seen
            r.Done = true;
            if (Log is not null && (t.Prompt > 0 || t.Generated > 0))
            {
                Log.Append(t.StartedAt, DateTime.Now, ServerUrl, r.From, r.Model, r.Slot, t.Prompt, t.Generated, seconds, t.Speed);
                RefreshDaily();
            }
            if (t.Prompt > 0 || t.Generated > 0)
            {
                var key = (r.Model, r.From);
                var sum = _totals.GetValueOrDefault(key);
                _totals[key] = (sum.Requests + 1, sum.Prompt + t.Prompt, sum.Generated + t.Generated, sum.Seconds + seconds);
            }
        }
    }

    public void ApplyHardware(GpuReading gpu, SystemReading sys, int llamaProcesses)
    {
        while (Gpus.Count < gpu.Adapters.Count) Gpus.Add(new GpuVm());
        while (Gpus.Count > gpu.Adapters.Count) Gpus.RemoveAt(Gpus.Count - 1);
        for (int i = 0; i < gpu.Adapters.Count; i++)
        {
            Gpus[i].Brush = Palette.GpuSeries[i % Palette.GpuSeries.Length];
            Gpus[i].Apply(gpu.Adapters[i]);
        }

        CpuText = $"{sys.CpuPercent:0}%";
        CpuHistory = _cpuHistory.Push(sys.CpuPercent);
        long used = (long)(sys.RamTotal - sys.RamAvailable);
        RamText = $"{Fmt.Gb(used)} / {Fmt.Gb((long)sys.RamTotal)} GB";
        RamHistory = _ramHistory.Push(sys.RamTotal > 0 ? 100.0 * used / sys.RamTotal : 0);
        ProcessText = llamaProcesses == 0
            ? "No local llama-server process"
            : $"llama-server ×{llamaProcesses}:  GPU {gpu.WatchUtilPercent:0}%  ·  {Fmt.Gb(gpu.WatchShared)} GB shared + {Fmt.Gb(gpu.WatchDedicated)} GB dedicated";
    }

    /// <param name="clients">Who is connected to the server right now, or null when that cannot be known (remote server).</param>
    public void ApplyServer(LlamaSnapshot s, IReadOnlyList<string>? clients = null)
    {
        RequestsNote = clients is null ? "client address is only available for a server on this PC" : "";
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
            WindowTitle = $"LlamaMonita {App.Version} — disconnected";
            return;
        }

        Status = "Connected";
        StatusBrush = Palette.Green;
        ServerInfo = (s.IsRouter ? "router mode" : "single model") + (s.BuildInfo is null ? "" : $"  ·  {s.BuildInfo}");

        // models (router mode)
        ShowModels = s.IsRouter;
        int modelRows = s.IsRouter ? s.Models.Count : 1;   // a single-model server gets one row
        while (Models.Count < modelRows) Models.Add(new ModelVm());
        while (Models.Count > modelRows) Models.RemoveAt(Models.Count - 1);
        for (int i = 0; i < s.Models.Count && s.IsRouter; i++)
        {
            Models[i].Id = s.Models[i].Id;
            Models[i].Status = s.Models[i].Status == "unloaded" ? "" : s.Models[i].Status;   // grey dot already says "not loaded"
            Models[i].Brush = s.Models[i].Status switch { "loaded" => Palette.Green, "loading" => Palette.Amber, _ => Palette.Muted };
        }

        bool hasModel = !string.IsNullOrEmpty(s.ActiveModel);

        // A model switch (router mode) replaces the slots: close what the old model was doing so its
        // task ids cannot be mistaken for the new model's.
        string model = hasModel ? s.ActiveModel! : "";
        if (model != _activeModel)
        {
            long stamp = Stopwatch.GetTimestamp();
            foreach (var old in _tracks.Values)
            {
                if (old.Counted) { _sessionPrompt += old.Prompt; _sessionGenerated += old.Generated; }
                UpdateRequest(old, stamp, false, clients);
            }
            _tracks.Clear();
            _activeModel = model;
        }
        ModelName = hasModel ? s.ActiveModel! : "No model loaded";
        var detail = new List<string>();
        if (!string.IsNullOrEmpty(s.Ftype)) detail.Add(s.Ftype);
        if (s.NCtxPerSlot > 0) detail.Add($"ctx {s.NCtxPerSlot:N0}" + (s.TotalSlots > 1 ? $" × {s.TotalSlots}" : ""));
        if (s.Vision) detail.Add("vision");
        ModelDetail = string.Join("  ·  ", detail);

        // One model card: the router list with the details under the loaded entry, or the single model.
        ModelsTitle = s.IsRouter ? "MODELS (ROUTER)" : "MODEL";
        if (!s.IsRouter)
        {
            Models[0].Id = ModelName;
            Models[0].Status = hasModel ? "loaded" : "";
            Models[0].Brush = hasModel ? Palette.Green : Palette.Muted;
        }
        foreach (var row in Models) row.Detail = !s.Loading && (!s.IsRouter || row.Id == s.ActiveModel) ? ModelDetail : "";

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
                // First sight: a task that is already finished predates this session and is not counted.
                t = new SlotTrack { Task = slot.TaskId, Counted = slot.IsProcessing, StartStamp = now };
                _tracks[slot.Id] = t;
                if (t.Counted) t.Request = StartRequest(slot, clients);
            }
            else if (slot.TaskId != t.Task)
            {
                if (t.Counted) { _sessionPrompt += t.Prompt; _sessionGenerated += t.Generated; }
                UpdateRequest(t, now, false, clients);
                t.Task = slot.TaskId;
                t.Prompt = t.Generated = t.LastDecoded = 0;
                t.SawDecode = false;
                t.Counted = true;
                t.StartStamp = now;
                t.StartedAt = DateTime.Now;
                t.Request = StartRequest(slot, clients);
            }

            if (slot.IsProcessing)
            {
                t.Prompt = Math.Max(t.Prompt, slot.PromptSize);
                if (slot.Decoded > 0 && !t.SawDecode) { t.SawDecode = true; t.FirstDecodeStamp = now; }
                if (slot.Decoded > t.LastDecoded) _speedTokens += slot.Decoded - t.LastDecoded;
                t.LastDecoded = slot.Decoded;
                t.Generated = Math.Max(t.Generated, slot.Decoded);
            }
            else
            {
                // n_decoded resets when the slot goes idle; the final context size still holds the
                // tokens generated after our last poll. Only trust it when generation was observed:
                // otherwise Prompt may be a half-processed prompt and the difference is meaningless.
                if (t.SawDecode && t.Prompt > 0)
                    t.Generated = Math.Max(t.Generated, slot.ContextTokens - t.Prompt);
                t.LastDecoded = 0;
            }
            if (t.Counted) { livePrompt += t.Prompt; liveGenerated += t.Generated; }
            UpdateRequest(t, now, slot.IsProcessing, clients);

            var vm = Slots[i];
            vm.Title = slot.Id.ToString();
            vm.StateBrush = slot.IsProcessing ? Palette.Green : Palette.Muted;
            int usedCtx = slot.ContextTokens;
            vm.Detail = $"{usedCtx:N0} / {slot.NCtx:N0}";
            vm.Fraction = slot.NCtx > 0 ? Math.Clamp(usedCtx / (double)slot.NCtx, 0, 1) : 0;
        }
        SlotsNote = s.SlotsAvailable || !hasModel || s.Loading ? "" : "/slots is disabled on this server";

        bool busy = s.Slots.Any(x => x.IsProcessing);
        if (!busy && clients is not null) _idleClients = clients;

        // Generation speed over a sliding window: /slots advances in bursts (more so with speculative
        // decoding), so a one-second difference is too noisy to read.
        // It counts only tokens seen arriving while a slot was generating, so end-of-task corrections to
        // the totals can never show up as a burst of speed.
        long totalGenerated = _speedTokens;
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
            ServerTotals = ServerInfo;   // no /metrics: show the server line instead (README explains --metrics)
        }
        WindowTitle = hasModel ? $"LlamaMonita {App.Version} — {ModelName} · {TokensPerSecond} tok/s" : $"LlamaMonita {App.Version}";
    }

    private void PushTps(double tps)
    {
        TokensPerSecond = tps.ToString("0.0");
        _lastTps = tps;
        TpsHistory = _tpsHistory.Push(tps);

        // Combined graph: GPU utilization per adapter on the left axis, generation speed on the right.
        var series = new List<ChartSeries>(Gpus.Count + 1);
        foreach (var g in Gpus) series.Add(new ChartSeries(g.UtilHistory, g.Brush));
        series.Add(new ChartSeries(TpsHistory, Palette.Green, RightAxis: true, Fill: false));
        HeroSeries = series;
    }
}
