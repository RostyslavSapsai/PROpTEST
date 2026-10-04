using System.Net.Sockets;
using System.Text;

namespace PropTest.Infrastructure;

// Same bounded line protocol over a local Wi-Fi link; no motor transport in v2 preview.
public sealed class TcpConnection(string host, int port = 8768) : ISerialConnection
{
    readonly TcpClient client = new() { NoDelay = true, SendTimeout = 500, ReceiveTimeout = 500 };
    public void Open()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        client.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();
    }
    public void WriteLine(string command) => client.GetStream().Write(Encoding.ASCII.GetBytes(command + "\n"));
    public string ReadAvailable()
    {
        if (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0)
            throw new IOException("Wi-Fi: плата закрила з’єднання.");
        var buffer = new byte[Math.Min(client.Available, 4096)];
        return buffer.Length == 0 ? "" : Encoding.ASCII.GetString(buffer, 0, client.GetStream().Read(buffer));
    }
    public void ClearInput() { for (var n = 0; n < 8 && client.Available > 0; n++) ReadAvailable(); }
    public void Dispose() => client.Dispose();
}
