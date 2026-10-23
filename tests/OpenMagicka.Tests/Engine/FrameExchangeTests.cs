using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Tests.Engine;

public class FrameExchangeTests
{
    private sealed class Frame
    {
        public long A;
        public long B;
    }

    [Fact]
    public void NothingIsAcquired_BeforeTheFirstPublish()
    {
        var exchange = new FrameExchange<Frame>(() => new Frame());

        Assert.Null(exchange.Acquire());
        exchange.BeginWrite().A = 1;
        Assert.Null(exchange.Acquire());           // written but not published
    }

    [Fact]
    public void TheReaderGetsTheNewestPublishedFrame_AndKeepsItUntilANewerOne()
    {
        var exchange = new FrameExchange<Frame>(() => new Frame());
        Publish(exchange, 1);
        Publish(exchange, 2);

        Assert.Equal(2, exchange.Acquire()!.A);     // frame 1 was overwritten, never shown
        Assert.Equal(2, exchange.Acquire()!.A);     // nothing newer: the same frame again
        Publish(exchange, 3);
        Assert.Equal(3, exchange.Acquire()!.A);
    }

    [Fact]
    public void TheWriter_NeverGetsTheFrameTheReaderHolds_OrTheOneWaitingForIt()
    {
        var exchange = new FrameExchange<Frame>(() => new Frame());
        Publish(exchange, 1);
        var held = exchange.Acquire();
        Publish(exchange, 2);
        var waiting = exchange.BeginWrite();
        exchange.Publish();
        waiting.A = 99;       // the slot just published; the next write must use a third one

        for (var i = 0; i < 10; i++)
        {
            var slot = exchange.BeginWrite();
            Assert.NotSame(held, slot);
            Assert.NotSame(waiting, slot);
            Assert.Equal(3, new[] { held, waiting, slot }.Distinct().Count());
        }
    }

    [Fact]
    public void PublishWithoutBeginWrite_IsAnError()
    {
        var exchange = new FrameExchange<Frame>(() => new Frame());

        Assert.Throws<InvalidOperationException>(exchange.Publish);
    }

    [Fact]
    public async Task UnderLoad_FramesAreNeverTornAndNeverGoBackwards()
    {
        var exchange = new FrameExchange<Frame>(() => new Frame());
        const long frames = 200_000;
        var writer = Task.Run(() =>
        {
            for (long i = 1; i <= frames; i++)
            {
                var frame = exchange.BeginWrite();
                frame.A = i;
                Thread.SpinWait(5);
                frame.B = i;
                exchange.Publish();
            }
        });

        long last = 0;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((!writer.IsCompleted || last < frames) && !writer.IsFaulted && DateTime.UtcNow < deadline)
        {
            var frame = exchange.Acquire();
            if (frame is null)
            {
                continue;
            }
            var (a, b) = (frame.A, frame.B);
            Thread.SpinWait(5);
            Assert.Equal(a, frame.A);                // the writer does not touch a held frame
            Assert.Equal(a, b);
            Assert.True(a >= last, $"frame {a} after {last}");
            last = a;
        }
        await writer;
    }

    private static void Publish(FrameExchange<Frame> exchange, long value)
    {
        exchange.BeginWrite().A = value;
        exchange.Publish();
    }
}
