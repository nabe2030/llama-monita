using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace LlamaMonita.Llama;

public sealed record ModelEntry(string Id, string Status);

/// <param name="ContextTokens">n_prompt_tokens: every token currently in the slot's context (prompt plus what has been generated).</param>
/// <param name="PromptSize">Size of the request's prompt: tokens processed plus tokens reused from the cache. 0 once the slot is idle.</param>
/// <param name="Decoded">Tokens generated so far. Resets to 0 when the slot goes idle.</param>
public sealed record SlotInfo(int Id, bool IsProcessing, int TaskId, int NCtx, int ContextTokens, int PromptSize, int Decoded, bool Speculative);

public sealed class LlamaSnapshot
{
    public bool Reachable { get; set; }
    public string? Error { get; set; }
    public string? BuildInfo { get; set; }
    public bool IsRouter { get; set; }
    public List<ModelEntry> Models { get; } = new();
    public string? ActiveModel { get; set; }
    public bool Loading { get; set; }
    public string? Ftype { get; set; }
    public int TotalSlots { get; set; }
    public int NCtxPerSlot { get; set; }
    public bool Vision { get; set; }
    public bool Sleeping { get; set; }
    public List<SlotInfo> Slots { get; } = new();
    public bool SlotsAvailable { get; set; }
    public Dictionary<string, double>? Metrics { get; set; }   // null when /metrics is not enabled
}

/// <summary>
/// Client for llama-server. Polling sends GET requests only, and in router mode never queries a
/// model that is not already loaded (so monitoring cannot trigger a model load or swap).
/// The one exception is <see cref="LoadModelAsync"/>, which runs only when the user confirms a switch.
/// </summary>
public sealed class LlamaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _base;
    private int _metricsRetryIn;

    /// <summary>TCP port of the server.</summary>
    public int Port { get; }

    /// <summary>True when the server address is this machine, so its client connections can be inspected.</summary>
    public bool IsLocal { get; }

    public LlamaClient(string baseUrl, string? apiKey = null)
    {
        if (!baseUrl.Contains("://")) baseUrl = "http://" + baseUrl;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        _base = new Uri(baseUrl);
        Port = _base.Port;
        IsLocal = LlamaMonita.Hardware.ConnectionMonitor.IsLocalHost(_base.Host);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        // POST /models/load answers only after the old instance has exited and the new one is spawned
        // (2-4 s on a 780M, more on a busy machine), so it gets its own, longer timeout.
        _loadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _loadHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private readonly HttpClient _loadHttp;

    /// <summary>
    /// Set while POST /models/load is in flight. Polling then skips every per-model endpoint: the router
    /// treats "?model=X" as a request for X, so a poll for the model being unloaded would queue a reload
    /// of it and could take the slot the new model was meant to get.
    /// </summary>
    private volatile string? _switchingTo;

    public async Task<LlamaSnapshot> PollAsync(CancellationToken ct)
    {
        var s = new LlamaSnapshot();
        try
        {
            var props = await GetJsonAsync("props", ct);
            if (props is null) { s.Error = "no response from /props"; return s; }
            s.Reachable = true;
            s.BuildInfo = Str(props["build_info"]);
            s.IsRouter = Str(props["role"]) == "router";

            string query = "";
            if (s.IsRouter)
            {
                var models = await GetJsonAsync("models", ct);
                if (models?["data"] is JsonArray arr)
                    foreach (var m in arr)
                        s.Models.Add(new ModelEntry(Str(m?["id"]) ?? "?", Str(m?["status"]?["value"]) ?? "unknown"));
                if (_switchingTo is { } target)
                {
                    s.ActiveModel = target; s.Loading = true;
                    return s;   // a switch is in progress: do not touch per-model endpoints
                }
                s.ActiveModel = s.Models.FirstOrDefault(m => m.Status == "loaded")?.Id;
                if (s.ActiveModel is null)
                {
                    var loading = s.Models.FirstOrDefault(m => m.Status == "loading");
                    if (loading is not null) { s.ActiveModel = loading.Id; s.Loading = true; }
                    return s;   // nothing loaded: do not touch per-model endpoints
                }
                // autoload=false: without it the router treats this GET as a request for the model and, if the
                // model is being unloaded at that moment (a switch by us or by any other client), queues a
                // reload of it that can win the slot over the model that was asked for.
                query = "?model=" + Uri.EscapeDataString(s.ActiveModel) + "&autoload=false";
                props = await GetJsonAsync("props" + query, ct);
            }

            if (props is not null)
            {
                s.ActiveModel ??= Str(props["model_alias"]) ?? Path.GetFileName(Str(props["model_path"]) ?? "");
                s.Ftype = Str(props["model_ftype"]);
                s.TotalSlots = Int(props["total_slots"]);
                s.NCtxPerSlot = Int(props["default_generation_settings"]?["n_ctx"]);
                s.Vision = Bool(props["modalities"]?["vision"]);
                s.Sleeping = Bool(props["is_sleeping"]);
            }

            if (await GetJsonAsync("slots" + query, ct) is JsonArray slots)
            {
                s.SlotsAvailable = true;
                foreach (var n in slots)
                {
                    var next = n?["next_token"];
                    if (next is JsonArray na) next = na.Count > 0 ? na[0] : null;
                    s.Slots.Add(new SlotInfo(
                        Int(n?["id"]), Bool(n?["is_processing"]), Int(n?["id_task"], -1), Int(n?["n_ctx"]),
                        Int(n?["n_prompt_tokens"]), Int(n?["n_prompt_tokens_processed"]) + Int(n?["n_prompt_tokens_cache"]),
                        Int(next?["n_decoded"]), Bool(n?["speculative"])));
                }
            }

            s.Metrics = await GetMetricsAsync(query, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            s.Error = ex is TaskCanceledException ? "timeout" : (ex.InnerException?.Message ?? ex.Message);
        }
        return s;
    }

    /// <summary>
    /// Asks a router to load a model (POST /models/load). With --models-max 1 the router unloads the
    /// current model first. No completion is requested, so nothing is generated. The router answers once
    /// the old instance has exited and the new one is spawned; the load itself then shows up as "loading"
    /// in /models. Cancelling the request while it waits cancels the switch, so it is given minutes, not seconds.
    /// </summary>
    /// <returns>null on success, otherwise the reason it failed.</returns>
    public async Task<string?> LoadModelAsync(string model, CancellationToken ct)
    {
        _switchingTo = model;
        try
        {
            string? error = null;
            // "model limit reached": another client's request for the old model took the freed slot first.
            // That client is served by now, so asking again usually goes through.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1) await Task.Delay(TimeSpan.FromSeconds(3), ct);
                var body = new JsonObject { ["model"] = model }.ToJsonString();
                using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                using var r = await _loadHttp.PostAsync(new Uri(_base, "models/load"), content, ct);
                if (r.IsSuccessStatusCode) return null;
                string text = await r.Content.ReadAsStringAsync(ct);
                error = $"HTTP {(int)r.StatusCode}" + (text.Length > 0 ? $": {(text.Length > 300 ? text[..300] : text)}" : "");
                if (!text.Contains("limit reached")) break;
            }
            return error;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return ex is TaskCanceledException ? $"no answer within {_loadHttp.Timeout.TotalMinutes:0} minutes" : (ex.InnerException?.Message ?? ex.Message);
        }
        finally { _switchingTo = null; }
    }

    /// <summary>Router mode: the status of every model (GET /models, which never loads anything).</summary>
    public async Task<List<ModelEntry>?> GetModelsAsync(CancellationToken ct)
    {
        try
        {
            if (await GetJsonAsync("models", ct) is not { } models || models["data"] is not JsonArray arr) return null;
            return arr.Select(m => new ModelEntry(Str(m?["id"]) ?? "?", Str(m?["status"]?["value"]) ?? "unknown")).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    private async Task<Dictionary<string, double>?> GetMetricsAsync(string query, CancellationToken ct)
    {
        // /metrics exists only when the server was started with --metrics; back off when it is absent.
        if (_metricsRetryIn > 0) { _metricsRetryIn--; return null; }
        using var r = await _http.GetAsync(new Uri(_base, "metrics" + query), ct);
        if (!r.IsSuccessStatusCode) { _metricsRetryIn = 30; return null; }
        var result = new Dictionary<string, double>();
        foreach (var raw in (await r.Content.ReadAsStringAsync(ct)).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int sp = line.LastIndexOf(' ');
            if (sp <= 0) continue;
            string name = line[..sp];
            int brace = name.IndexOf('{');
            if (brace > 0) name = name[..brace];
            if (name.StartsWith("llamacpp:")) name = name[9..];
            if (double.TryParse(line[(sp + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                result[name] = v;
        }
        return result;
    }

    private async Task<JsonNode?> GetJsonAsync(string path, CancellationToken ct)
    {
        using var r = await _http.GetAsync(new Uri(_base, path), ct);
        if (!r.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await r.Content.ReadAsStringAsync(ct));
    }

    private static string? Str(JsonNode? n) { try { return n?.GetValue<string>(); } catch { return n?.ToString(); } }
    private static int Int(JsonNode? n, int fallback = 0) { try { return n is null ? fallback : (int)n.GetValue<double>(); } catch { return fallback; } }
    private static bool Bool(JsonNode? n) { try { return n?.GetValue<bool>() ?? false; } catch { return false; } }

    public void Dispose() { _http.Dispose(); _loadHttp.Dispose(); }
}
