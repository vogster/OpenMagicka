namespace OpenMagicka.Net;

/// <summary>Identifies a connected peer within one session.</summary>
public readonly record struct PeerId(int Value);

/// <summary>Delivery guarantees, see ADR-0002.</summary>
public enum DeliveryChannel
{
    /// <summary>May be lost or reordered: snapshots.</summary>
    Unreliable,

    /// <summary>Delivered once, in send order: events, spawns, triggers, scene changes.</summary>
    ReliableOrdered,
}

public readonly record struct NetMessage(PeerId From, DeliveryChannel Channel, ReadOnlyMemory<byte> Payload);

/// <summary>
/// Moves opaque payloads between peers. Implementations: loopback (single player and tests),
/// UDP with NAT punch-through, relay. Message encoding lives above this layer.
/// </summary>
public interface INetTransport : IDisposable
{
    PeerId LocalPeer { get; }

    IReadOnlyCollection<PeerId> ConnectedPeers { get; }

    void Send(PeerId to, DeliveryChannel channel, ReadOnlySpan<byte> payload);

    /// <summary>Dequeues the next received message; returns false when none is pending.</summary>
    bool TryReceive(out NetMessage message);
}
