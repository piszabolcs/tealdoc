using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TealDocAiTool.Tests.Gui;

/// <summary>
/// Minimális, determinisztikus „Ollama” a képernyőképekhez: csak a 127.0.0.1-en figyel, a
/// <c>/api/tags</c> és a <c>/api/generate</c> végpontot ismeri, és szabályalapú, a prompt
/// tartalmából képzett válaszokat ad. NEM része a terméknek — a valódi app egy valódi helyi
/// Ollamával dolgozik.
/// </summary>
internal sealed class FakeOllamaServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public FakeOllamaServer()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private static async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = new BufferedStream(client.GetStream());
            string requestLine = ReadLine(stream);
            int contentLength = 0;
            bool chunked = false;
            for (string line = ReadLine(stream); line.Length > 0; line = ReadLine(stream))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(line["Content-Length:".Length..].Trim());
                if (line.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) && line.Contains("chunked"))
                    chunked = true;
            }

            // A HttpClient a JSON-törzset gyakran „chunked” kódolással küldi.
            var body = new MemoryStream();
            if (chunked)
            {
                for (int size = Convert.ToInt32(ReadLine(stream).Split(';')[0], 16); size > 0;
                     size = Convert.ToInt32(ReadLine(stream).Split(';')[0], 16))
                {
                    body.Write(ReadExactly(stream, size));
                    ReadLine(stream);
                }
                ReadLine(stream);
            }
            else if (contentLength > 0)
            {
                body.Write(ReadExactly(stream, contentLength));
            }

            string json = requestLine.Contains("/api/tags")
                ? """{"models":[{"name":"qwen3:8b"}]}"""
                : JsonSerializer.Serialize(new { response = Answer(Encoding.UTF8.GetString(body.ToArray())) });
            byte[] payload = Encoding.UTF8.GetBytes(json);
            string header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
            await stream.WriteAsync(payload);
            await stream.FlushAsync();
        }
    }

    /// <summary>Egy CRLF-fel lezárt fejléc-/chunk-sor, bájtonként olvasva (a törzs bájtszámlálása miatt).</summary>
    private static string ReadLine(Stream stream)
    {
        var bytes = new List<byte>();
        int b;
        while ((b = stream.ReadByte()) >= 0)
        {
            if (b == '\n') break;
            if (b != '\r') bytes.Add((byte)b);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>A „modell”: a prompt fajtája szerint egy kiszámítható válasz.</summary>
    private static string Answer(string requestJson)
    {
        string prompt = JsonDocument.Parse(requestJson).RootElement.GetProperty("prompt").GetString() ?? "";
        string firstLine = DocumentText(prompt).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

        if (prompt.Contains("Válaszolj egy szóval")) return "OK";
        if (prompt.Contains("MILYEN FAJTA gép"))
            return prompt.Contains("ESZTERGA") ? "CNC esztergagép"
                 : prompt.Contains("MARÓGÉP") ? "CNC marógép"
                 : prompt.Contains("PRÉS") ? "hidraulikus prés"
                 : "NINCS_CÍM";
        if (prompt.Contains("MIT SZERELNEK")) return "alkatrész-összeszerelés";
        if (prompt.Contains("összefoglalnod"))
            return $"{firstLine}. A dokumentum a berendezés kezeléséhez és karbantartásához ad gyakorlati útmutatást.";
        if (prompt.Contains("MILYEN TÍPUSÚ dokumentum")) return "Kezelési útmutató";
        return firstLine.Length > 0 ? firstLine : "NINCS_CÍM";
    }

    private static string DocumentText(string prompt)
    {
        foreach (string marker in new[] { "Dokumentum szövege:", "Néhány dokumentum" })
        {
            int at = prompt.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0) return prompt[(at + marker.Length)..];
        }
        return prompt;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
