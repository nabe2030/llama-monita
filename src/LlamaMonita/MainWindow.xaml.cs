using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LlamaMonita.Hardware;
using LlamaMonita.Llama;

namespace LlamaMonita;

public partial class MainWindow : Window
{
    private readonly MainVm _vm = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string? _apiKey;
    private readonly string? _screenshotPath;
    private readonly int _screenshotDelay;
    private volatile LlamaClient _client;
    private MetricsExporter? _exporter;
    private string _connectedUrl = "";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlamaMonita", "settings.json");

    public MainWindow(string? url, string? apiKey, string? screenshotPath, Size? size = null, int screenshotDelay = 14, bool preferLog = false, string? logDir = null, string? metricsListen = null)
    {
        InitializeComponent();
        LogToggle.IsChecked = preferLog;
        VersionRun.Text = "v" + App.Version;
        // Test captures must not write to the user's request log.
        if (screenshotPath is null || logDir is not null) _vm.Log = new RequestLog(logDir);
        _vm.RefreshDaily();
        if (metricsListen is not null)
        {
            try { _exporter = new MetricsExporter(metricsListen); _vm.MetricsNote = "metrics: " + _exporter.Url; }
            catch (Exception ex) { _vm.MetricsNote = "metrics endpoint failed: " + ex.Message; }
        }
        DataContext = _vm;
        _apiKey = apiKey;
        _screenshotPath = screenshotPath;
        _screenshotDelay = screenshotDelay;
        if (size is { } sz) { Width = sz.Width; Height = sz.Height; }
        UrlBox.Text = url ?? LoadSavedUrl() ?? App.DefaultUrl;
        _client = new LlamaClient(UrlBox.Text, _apiKey);
        _connectedUrl = UrlBox.Text.Trim();
        _vm.ServerUrl = _connectedUrl;
        ConnectButton.IsEnabled = false;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainVm.ShowModels)) UpdateLayoutMode();
        };
        _vm.Gpus.CollectionChanged += (_, _) => UpdateLayoutMode();

        SourceInitialized += (_, _) =>
        {
            int dark = 1;   // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        Loaded += (_, _) => _ = Task.Run(() => PollLoopAsync(_cts.Token));
        Closed += (_, _) => { _cts.Cancel(); _exporter?.Dispose(); };
    }

    // ---- responsive layout ---------------------------------------------------------------------
    //
    // The combined history graph always stays on screen and absorbs all spare space. Everything else is
    // dropped in steps as the window shrinks:
    //   details row -> memory bars / footers -> sensor and token tiles -> connection bar.
    // Below the "mini" threshold only the headline number and its graph remain, so the window can
    // sit in a corner of the desktop. There is no upper limit: on a large window the graphs grow.

    private void Shell_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();

    private void UpdateLayoutMode()
    {
        double w = Shell.ActualWidth, h = Shell.ActualHeight;
        if (w <= 0 || h <= 0) return;

        bool mini = h < 300 || w < 440;
        double infoHeight = 150;   // models card (up to three rows plus details) and the system card
        double barHeight = mini ? 0 : 48;
        // Below the graph there are two optional blocks: the details row and the request log. Both are
        // shown when there is room; otherwise the "Request log" toggle decides which one wins.
        const double logHeight = 178, minGraph = 180;
        double space = h - barHeight;
        bool wantLog = LogToggle.IsChecked == true;
        bool showInfo = false, showLog = false;
        if (!mini)
        {
            if (space - infoHeight - logHeight >= minGraph) showInfo = showLog = true;
            else if (wantLog && space - logHeight >= minGraph) showLog = true;
            else if (space - infoHeight >= minGraph) showInfo = true;
        }
        double graphArea = space - (showInfo ? infoHeight : 0) - (showLog ? logHeight : 0);
        _vm.ShowRequests = showLog;

        _vm.ShowTopBar = !mini;
        _vm.ShowServerLabel = w >= 700;
        _vm.ShowInfo = showInfo;
        // Sensor and token tiles sit beside the headline numbers; each extra GPU needs its own block.
        int extraGpus = Math.Max(0, _vm.Gpus.Count - 1);
        _vm.ShowTiles = w >= 480 + 340 * extraGpus;
        _vm.ShowTokenTiles = w >= 760 + 340 * extraGpus;
        _vm.ShowFooter = !mini && graphArea >= 240 + 40 * _vm.Gpus.Count;

        Shell.Padding = new Thickness(mini ? 8 : 16);
    }

    private void SetClientSize(double width, double height)
    {
        WindowState = WindowState.Normal;
        Width = width;
        Height = height;
    }

    private void SizeCompact_Click(object sender, RoutedEventArgs e) => SetClientSize(460, 200);
    private void SizeGraphs_Click(object sender, RoutedEventArgs e) => SetClientSize(900, 420);
    private void SizeStandard_Click(object sender, RoutedEventArgs e) => SetClientSize(1080, 820);

    private void SetPinned(bool pinned)
    {
        Topmost = pinned;
        PinToggle.IsChecked = pinned;
        PinMenu.IsChecked = pinned;
    }

    private void PinToggle_Click(object sender, RoutedEventArgs e) => SetPinned(PinToggle.IsChecked == true);
    private void LogToggle_Click(object sender, RoutedEventArgs e) => UpdateLayoutMode();

    private static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    private void VersionLink_Click(object sender, RoutedEventArgs e) => OpenExternal(App.ReleaseNotesUrl);

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Log is not null) OpenExternal(_vm.Log.Folder);
    }
    private void PinMenu_Click(object sender, RoutedEventArgs e) => SetPinned(PinMenu.IsChecked);

    // ---- polling -------------------------------------------------------------------------------

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var gpu = new GpuMonitor();   // slow first call (performance counter registry), keep off the UI thread
        var sys = new SystemMonitor();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int ticks = 0;
        try
        {
            do
            {
                int[] pids = Process.GetProcessesByName("llama-server").Select(p => { int id = p.Id; p.Dispose(); return id; }).ToArray();
                var g = gpu.Sample(pids);
                var s = sys.Sample();
                var client = _client;
                var server = await client.PollAsync(ct);
                // Who is connected right now. Only knowable when the server runs on this machine.
                IReadOnlyList<string>? clients = client.IsLocal ? ConnectionMonitor.Clients(client.Port, Environment.ProcessId) : null;
                await Dispatcher.InvokeAsync(() =>
                {
                    _vm.ApplyHardware(g, s, pids.Length);
                    _vm.ApplyServer(server, clients);
                    _exporter?.Update(_vm.BuildMetrics(g, s, server));
                });
                if (_screenshotPath is not null && ++ticks == _screenshotDelay)
                    await Dispatcher.InvokeAsync(() => { SaveScreenshot(_screenshotPath); Close(); });
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    private void Connect()
    {
        try
        {
            var next = new LlamaClient(UrlBox.Text.Trim(), _apiKey);
            var old = _client;
            _client = next;
            old.Dispose();
            _vm.ResetSession();
            SaveUrl(UrlBox.Text.Trim());
            _connectedUrl = UrlBox.Text.Trim();
            _vm.ServerUrl = _connectedUrl;
            ConnectButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LlamaMonita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // The Connect button is only live while the box holds an address other than the one being watched.
    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ConnectButton is not null) ConnectButton.IsEnabled = UrlBox.Text.Trim() != _connectedUrl;
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => Connect();

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Connect();
    }

    private static string? LoadSavedUrl()
    {
        try { return JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement.GetProperty("url").GetString(); }
        catch { return null; }
    }

    private static void SaveUrl(string url)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { url }));
        }
        catch { }
    }

    private void SaveScreenshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(Shell.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(Shell.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(Shell);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
