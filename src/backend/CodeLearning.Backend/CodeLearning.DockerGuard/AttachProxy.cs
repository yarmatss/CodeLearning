using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace CodeLearning.DockerGuard;

// Tunnels POST /containers/{id}/attach. The daemon answers an attach by hijacking the connection (101 Switching
// Protocols, then raw bytes both ways), which YARP doesn't forward for a non-WebSocket upgrade. The route and the
// container are checked by RequestPolicy and the sandbox label check before this runs
public static class AttachProxy
{
    private const int MaxResponseHeaderBytes = 16 * 1024;

    public static async Task ProxyAsync(HttpContext context, string dockerSocket, ILogger logger)
    {
        var upgrade = context.Features.Get<IHttpUpgradeFeature>();
        if (upgrade is not { IsUpgradableRequest: true })
        {
            await GuardHttp.DenyAsync(context, "Attach requires an HTTP/1.1 upgrade (Connection: Upgrade, Upgrade: tcp)");
            return;
        }

        var cancellationToken = context.RequestAborted;
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(dockerSocket), cancellationToken);
        await using var daemon = new NetworkStream(socket, ownsSocket: false);

        // Only the path and query the policy approved; no client headers are passed on
        var target = context.Request.Path.Value + context.Request.QueryString.Value;
        var request = $"POST {target} HTTP/1.1\r\nHost: docker\r\nConnection: Upgrade\r\nUpgrade: tcp\r\n\r\n";
        await daemon.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);

        var (statusCode, head) = await ReadResponseHeadAsync(daemon, cancellationToken);
        if (statusCode is not (101 or 200))
        {
            logger.LogWarning("Attach {Target} refused by the daemon: {Status}", target, head.Split("\r\n")[0]);
            context.Response.StatusCode = statusCode is >= 400 and < 600 ? statusCode : StatusCodes.Status502BadGateway;
            return;
        }

        context.Response.Headers.Upgrade = "tcp";
        await using var client = await upgrade.UpgradeAsync();

        // Client to daemon is the container's stdin: when the client stops sending, the daemon gets end of file
        var toDaemon = Task.Run(async () =>
        {
            try
            {
                await client.CopyToAsync(daemon, cancellationToken);
            }
            finally
            {
                socket.Shutdown(SocketShutdown.Send);
            }
        }, cancellationToken);

        // Daemon to client ends when the attach ends (container exited or the daemon closed it)
        var toClient = daemon.CopyToAsync(client, cancellationToken);

        try
        {
            await Task.WhenAll(toDaemon, toClient);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
        {
            // Either side closing the tunnel ends it
        }
    }

    private static async Task<(int StatusCode, string Head)> ReadResponseHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>();
        var buffer = new byte[1];

        while (head.Count < MaxResponseHeaderBytes && await stream.ReadAsync(buffer, cancellationToken) == 1)
        {
            head.Add(buffer[0]);
            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
            {
                var text = Encoding.ASCII.GetString(head.ToArray());
                var parts = text.Split(' ', 3);
                return (parts.Length > 1 && int.TryParse(parts[1], out var code) ? code : 0, text);
            }
        }

        return (0, Encoding.ASCII.GetString(head.ToArray()));
    }
}
