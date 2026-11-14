using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using LlamaMonita.Hardware;
using LlamaMonita.Llama;

namespace LlamaMonita;

public partial class App : Application
{
    public const string DefaultUrl = "http://127.0.0.1:8080";

    /// <summary>Version from the project file (&lt;Version&gt;), without the build metadata.</summary>
    public static string Version { get; } =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public static string ReleaseNotesUrl => "https://github.com/nabe2030/llama-monita/releases/tag/v" + Version;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Arg(string name)
        {
            int i = Array.IndexOf(e.Args, name);
            return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null;
        }

        string? url = Arg("--url");
        string? apiKey = Arg("--api-key") ?? Environment.GetEnvironmentVariable("LLAMA_API_KEY");

        // --probe <file>: sample every data source without showing a window (diagnostics / bug reports).
        if (Arg("--probe") is { } probeFile)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await File.WriteAllTextAsync(probeFile, await ProbeAsync(url ?? DefaultUrl, apiKey)); }
            catch (Exception ex) { await File.WriteAllTextAsync(probeFile, ex.ToString()); }
            Shutdown();
            return;
        }

        // --size WxH sets the initial window size (used with --screenshot to capture each layout).
        Size? size = null;
        if (Arg("--size")?.Split('x') is [var sw, var sh] && double.TryParse(sw, out double pw) && double.TryParse(sh, out double ph))
            size = new Size(pw, ph);

        // --shot-delay N: seconds to collect history before --screenshot captures (default 14).
        int shotDelay = int.TryParse(Arg("--shot-delay"), out int sd) ? sd : 14;

        // --log starts with the "Request log" toggle on.
        // --metrics-listen [ADDRESS:]PORT serves Prometheus metrics (loopback unless an address is given).
        // --log-dir PATH writes the request log there instead of %LOCALAPPDATA%\LlamaMonita\requests.
        new MainWindow(url, apiKey, Arg("--screenshot"), size, shotDelay, e.Args.Contains("--log"), Arg("--log-dir"), Arg("--metrics-listen")).Show();
    }

    private static async Task<string> ProbeAsync(string url, string? apiKey)
    {
        using var gpu = new GpuMonitor();
        var sys = new SystemMonitor();
        using var client = new LlamaClient(url, apiKey);
        int[] pids = Process.GetProcessesByName("llama-server").Select(p => p.Id).ToArray();
        var samples = new List<object>();
        for (int i = 0; i < 4; i++)
        {
            var g = gpu.Sample(pids);
            samples.Add(new { gpu = g, system = sys.Sample() });
            await Task.Delay(1000);
        }
        var server = await client.PollAsync(CancellationToken.None);
        return JsonSerializer.Serialize(new { pids, samples, server }, new JsonSerializerOptions { WriteIndented = true });
    }
}
