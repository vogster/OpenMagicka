using OpenMagicka.Net;

namespace OpenMagicka.Tests.Net;

public class LoopbackTransportTests
{
    [Fact]
    public void CreatePair_GivesHostAndClientDistinctPeers()
    {
        var (host, client) = LoopbackTransport.CreatePair();

        Assert.Equal(LoopbackTransport.HostPeer, host.LocalPeer);
        Assert.NotEqual(host.LocalPeer, client.LocalPeer);
        Assert.Equal(client.LocalPeer, Assert.Single(host.ConnectedPeers));
        Assert.Equal(host.LocalPeer, Assert.Single(client.ConnectedPeers));
    }

    [Fact]
    public void Send_DeliversPayloadAndChannelToTheOtherEnd()
    {
        var (host, client) = LoopbackTransport.CreatePair();

        client.Send(host.LocalPeer, DeliveryChannel.ReliableOrdered, [1, 2, 3]);

        Assert.True(host.TryReceive(out var message));
        Assert.Equal(client.LocalPeer, message.From);
        Assert.Equal(DeliveryChannel.ReliableOrdered, message.Channel);
        Assert.Equal(new byte[] { 1, 2, 3 }, message.Payload.ToArray());
        Assert.False(client.TryReceive(out _));
    }

    [Fact]
    public void Send_CopiesThePayload()
    {
        var (host, client) = LoopbackTransport.CreatePair();
        var buffer = new byte[] { 7 };

        host.Send(client.LocalPeer, DeliveryChannel.Unreliable, buffer);
        buffer[0] = 9;

        Assert.True(client.TryReceive(out var message));
        Assert.Equal(7, message.Payload.Span[0]);
    }

    [Fact]
    public void Messages_ArriveInSendOrder()
    {
        var (host, client) = LoopbackTransport.CreatePair();

        for (byte i = 0; i < 10; i++)
        {
            host.Send(client.LocalPeer, DeliveryChannel.ReliableOrdered, [i]);
        }

        for (byte i = 0; i < 10; i++)
        {
            Assert.True(client.TryReceive(out var message));
            Assert.Equal(i, message.Payload.Span[0]);
        }
    }

    [Fact]
    public void Send_ToUnknownPeer_Throws()
    {
        var (host, _) = LoopbackTransport.CreatePair();

        Assert.Throws<ArgumentException>(() => host.Send(new PeerId(42), DeliveryChannel.Unreliable, [1]));
    }

    [Fact]
    public void Send_OversizedPayload_Throws()
    {
        var (host, client) = LoopbackTransport.CreatePair();
        var payload = new byte[LoopbackTransport.MaxPayloadBytes + 1];

        Assert.Throws<ArgumentException>(() => host.Send(client.LocalPeer, DeliveryChannel.Unreliable, payload));
    }

    [Fact]
    public void Send_ToDisposedRemote_ThrowsAndPeerIsGone()
    {
        var (host, client) = LoopbackTransport.CreatePair();
        client.Dispose();

        Assert.Empty(host.ConnectedPeers);
        Assert.Throws<ArgumentException>(() => host.Send(client.LocalPeer, DeliveryChannel.Unreliable, [1]));
    }

    [Fact]
    public void TryReceive_AfterDispose_Throws()
    {
        var (host, _) = LoopbackTransport.CreatePair();
        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => host.TryReceive(out _));
    }

    [Fact]
    public async Task ConcurrentSendAndReceive_DeliversEveryMessageOnce()
    {
        const int count = 10_000;
        var (host, client) = LoopbackTransport.CreatePair();

        var sender = Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                client.Send(host.LocalPeer, DeliveryChannel.ReliableOrdered, BitConverter.GetBytes(i));
            }
        });

        var expected = 0;
        while (expected < count && !sender.IsFaulted)
        {
            if (host.TryReceive(out var message))
            {
                Assert.Equal(expected++, BitConverter.ToInt32(message.Payload.Span));
            }
        }

        await sender;
        Assert.False(host.TryReceive(out _));
    }

    [Fact]
    public void Send_AfterDispose_Throws()
    {
        var (host, client) = LoopbackTransport.CreatePair();
        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => host.Send(client.LocalPeer, DeliveryChannel.Unreliable, [1]));
    }
}
