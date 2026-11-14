using System.Globalization;
using System.Text;

namespace LlamaMonita;

public readonly record struct DayTotal(int Requests, long Prompt, long Generated)
{
    public DayTotal Add(int prompt, int generated) => new(Requests + 1, Prompt + prompt, Generated + generated);
}

/// <summary>
/// Appends every finished request to a monthly CSV file and keeps per-day totals in memory.
/// llama-server only counts tokens in memory, per model process, so this file is the durable record.
/// Location: %LOCALAPPDATA%\LlamaMonita\requests\requests-YYYY-MM.csv
/// </summary>
public sealed class RequestLog
{
    public const string Header = "started,finished,server,from,model,slot,prompt_tokens,generated_tokens,seconds,tokens_per_second";

    private readonly object _lock = new();
    private readonly SortedDictionary<DateOnly, DayTotal> _days = new();

    public string Folder { get; }

    public RequestLog(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlamaMonita", "requests");
        try
        {
            Directory.CreateDirectory(Folder);
            LoadMonth(DateTime.Now.AddMonths(-1));
            LoadMonth(DateTime.Now);
        }
        catch { /* the log is a convenience; never let it stop the monitor */ }
    }

    private string FileFor(DateTime day) => Path.Combine(Folder, $"requests-{day:yyyy-MM}.csv");

    public void Append(DateTime started, DateTime finished, string server, string from, string model, string slot,
                       int prompt, int generated, double seconds, double tokensPerSecond)
    {
        lock (_lock)
        {
            var day = DateOnly.FromDateTime(started);
            _days[day] = _days.GetValueOrDefault(day).Add(prompt, generated);
            try
            {
                string file = FileFor(started);
                var sb = new StringBuilder();
                if (!File.Exists(file)) sb.AppendLine(Header);
                sb.AppendJoin(',',
                    started.ToString("yyyy-MM-ddTHH:mm:sszzz"), finished.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    Csv(server), Csv(from), Csv(model), Csv(slot),
                    prompt.ToString(CultureInfo.InvariantCulture), generated.ToString(CultureInfo.InvariantCulture),
                    seconds.ToString("0.0", CultureInfo.InvariantCulture), tokensPerSecond.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    public DayTotal Today
    {
        get { lock (_lock) return _days.GetValueOrDefault(DateOnly.FromDateTime(DateTime.Now)); }
    }

    /// <summary>Totals of the most recent days that have any request, newest first.</summary>
    public List<(DateOnly Day, DayTotal Total)> Recent(int days)
    {
        lock (_lock) return _days.Reverse().Take(days).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private void LoadMonth(DateTime month)
    {
        string file = FileFor(month);
        if (!File.Exists(file)) return;
        foreach (string line in File.ReadLines(file))
        {
            if (line.Length == 0 || line.StartsWith("started")) continue;
            var f = Split(line);
            if (f.Count < 8) continue;
            if (!DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)) continue;
            int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prompt);
            int.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int generated);
            var day = DateOnly.FromDateTime(started.LocalDateTime);
            _days[day] = _days.GetValueOrDefault(day).Add(prompt, generated);
        }
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static List<string> Split(string line)
    {
        var fields = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cur.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        fields.Add(cur.ToString());
        return fields;
    }
}
