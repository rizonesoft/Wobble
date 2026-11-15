using System.Net;
using System.Net.Sockets;
using Wobble.Core;

namespace Wobble.Iscsi;

/// <summary>
/// A userspace iSCSI target serving one SCSI device as LUN 0.
/// RFC 7143 subset: one connection per session, error recovery level 0, no digests, no authentication.
/// </summary>
public sealed class IscsiTarget(IScsiDevice device, string targetName, IPEndPoint endpoint) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<Task> connections = [];
    private TcpListener? listener;
    private Task? acceptLoop;

    public string TargetName { get; } = targetName;

    /// <summary>The bound endpoint, with the real port when port 0 was requested.</summary>
    public IPEndPoint LocalEndpoint => (IPEndPoint)listener!.LocalEndpoint;

    public void Start()
    {
        listener = new TcpListener(endpoint);
        listener.Start();
        acceptLoop = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var client = await listener!.AcceptTcpClientAsync(stop.Token);
                client.NoDelay = true;
                var task = ServeAsync(client);
                lock (connections)
                {
                    connections.RemoveAll(t => t.IsCompleted);
                    connections.Add(task);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await new IscsiConnection(client, device, TargetName).RunAsync(stop.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or EndOfStreamException)
            {
                // The initiator went away or the target is stopping: the session ends with the connection.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener?.Stop();
        if (acceptLoop is not null) await acceptLoop;
        Task[] open;
        lock (connections) open = [.. connections];
        await Task.WhenAll(open);
        stop.Dispose();
    }
}
