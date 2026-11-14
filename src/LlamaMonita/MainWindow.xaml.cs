using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
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
    private volatile LlamaClient _client;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlamaMonita", "settings.json");

    public MainWindow(string? url, string? apiKey, string? screenshotPath)
    {
        InitializeComponent();
        DataContext = _vm;
        _apiKey = apiKey;
        _screenshotPath = screenshotPath;
        UrlBox.Text = url ?? LoadSavedUrl() ?? App.DefaultUrl;
        _client = new LlamaClient(UrlBox.Text, _apiKey);

        SourceInitialized += (_, _) =>
        {
            int dark = 1;   // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        Loaded += (_, _) => _ = Task.Run(() => PollLoopAsync(_cts.Token));
        Closed += (_, _) => _cts.Cancel();
    }

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
                var server = await _client.PollAsync(ct);
                await Dispatcher.InvokeAsync(() =>
                {
                    _vm.ApplyHardware(g, s, pids.Length);
                    _vm.ApplyServer(server);
                });
                if (_screenshotPath is not null && ++ticks == 12)
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
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LlamaMonita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => Connect();

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Connect();
    }

    private void Pin_Changed(object sender, RoutedEventArgs e) => Topmost = PinBox.IsChecked == true;

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
