using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CodeLearning.DockerGuard;

// HTTP over the Docker daemon's Unix socket: used both to forward allowed calls and for the guard's own checks
public sealed class DockerSocketClient : IDisposable
{
    // Placeholder only: ConnectCallback sends every request over the local Unix socket, never over a network,
    // so there is no transport to encrypt. HTTP is simply the protocol the Docker API speaks on that socket
    public const string BaseAddress = "http://docker/";

    private readonly HttpClient _client;

    public DockerSocketClient(IOptions<GuardOptions> options)
    {
        var socketPath = options.Value.DockerSocket;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        Invoker = new HttpMessageInvoker(handler);
        _client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(BaseAddress) };
    }

    public HttpMessageInvoker Invoker { get; }

    // Reads the labels from the daemon itself, so the caller cannot claim a container is a sandbox
    public async Task<bool> IsSandboxContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync($"containers/{Uri.EscapeDataString(containerId)}/json", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return IsSandbox(document.RootElement);
    }

    public static bool IsSandbox(JsonElement inspect) =>
        inspect.TryGetProperty("Config", out var config)
        && config.TryGetProperty("Labels", out var labels)
        && labels.ValueKind == JsonValueKind.Object
        && labels.TryGetProperty(GuardOptions.SandboxLabel, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() == "true";

    public void Dispose()
    {
        _client.Dispose();
        Invoker.Dispose();
    }
}
