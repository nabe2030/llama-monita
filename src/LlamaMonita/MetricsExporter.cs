using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LlamaMonita;

/// <summary>
/// Optional Prometheus endpoint (GET /metrics). Off unless --metrics-listen is given.
/// A plain TcpListener is used instead of HttpListener so that binding to a LAN address does not
/// need administrator rights or a URL reservation. It only ever serves the latest snapshot text.
/// </summary>
public sealed class MetricsExporter : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private volatile string _body = "# LlamaMonita is starting\n";

    /// <summary>Address to show to the user, e.g. http://127.0.0.1:9777/metrics</summary>
    public string Url { get; }

    /// <param name="listen">"PORT" (loopback only) or "ADDRESS:PORT", e.g. "0.0.0.0:9777" for the LAN.</param>
    public MetricsExporter(string listen)
    {
        string host = "127.0.0.1", portText = listen.Trim();
        int colon = portText.LastIndexOf(':');
        if (colon > 0) { host = portText[..colon]; portText = portText[(colon + 1)..]; }
        int port = int.Parse(portText);
        var address = IPAddress.Parse(host);
        _listener = new TcpListener(address, port);
        _listener.Start();
        Url = $"http://{(address.Equals(IPAddress.Any) ? "<this PC>" : host)}:{port}/metrics";
        _ = Task.Run(AcceptLoopAsync);
    }

    public void Update(string body) => _body = body;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                client.ReceiveTimeout = client.SendTimeout = 3000;
                using var stream = client.GetStream();
                string request = await ReadHeadAsync(stream);
                bool metrics = request.StartsWith("GET /metrics", StringComparison.Ordinal);
                string body = metrics ? _body : "LlamaMonita exporter. Metrics are at /metrics\n";
                byte[] payload = Encoding.UTF8.GetBytes(body);
                string head = (metrics ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") + "\r\n" +
                              "Content-Type: text/plain; version=0.0.4; charset=utf-8\r\n" +
                              $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _cts.Token);
                await stream.WriteAsync(payload, _cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { /* a broken client must not stop the exporter */ }
        }
    }

    private async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(3000);
        while (sb.Length < 8192)
        {
            int n = await stream.ReadAsync(buffer, timeout.Token);
            if (n <= 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
            if (sb.ToString().Contains("\r\n\r\n")) break;
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
