using System.Collections.Concurrent;

namespace OpenMagicka.Net;

/// <summary>
/// In-process transport between a host and one client. Single player runs as a host over
/// loopback, so it exercises the same message path as network play. Both channels are
/// delivered reliably and in order. Safe to send and receive from different threads.
/// </summary>
public sealed class LoopbackTransport : INetTransport
{
    public const int MaxPayloadBytes = 64 * 1024;

    public static readonly PeerId HostPeer = new(0);
    public static readonly PeerId ClientPeer = new(1);

    private readonly ConcurrentQueue<NetMessage> inbox = new();
    private LoopbackTransport? remote;
    private volatile bool disposed;

    private LoopbackTransport(PeerId localPeer)
    {
        LocalPeer = localPeer;
    }

    public PeerId LocalPeer { get; }

    public IReadOnlyCollection<PeerId> ConnectedPeers =>
        remote is { disposed: false } ? [remote.LocalPeer] : [];

    public static (LoopbackTransport Host, LoopbackTransport Client) CreatePair()
    {
        var host = new LoopbackTransport(HostPeer);
        var client = new LoopbackTransport(ClientPeer);
        host.remote = client;
        client.remote = host;
        return (host, client);
    }

    public void Send(PeerId to, DeliveryChannel channel, ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (remote is null || remote.disposed || to != remote.LocalPeer)
        {
            throw new ArgumentException($"Peer {to.Value} is not connected.", nameof(to));
        }
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentException($"Payload of {payload.Length} bytes exceeds {MaxPayloadBytes}.", nameof(payload));
        }

        remote.inbox.Enqueue(new NetMessage(LocalPeer, channel, payload.ToArray()));
        if (remote.disposed)
        {
            // The remote closed while we were enqueueing: don't leave messages behind.
            remote.inbox.Clear();
        }
    }

    public bool TryReceive(out NetMessage message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return inbox.TryDequeue(out message);
    }

    public void Dispose()
    {
        disposed = true;
        inbox.Clear();
    }
}
