namespace OpenMagicka.Engine.Threading;

/// <summary>
/// Hands frames from the logic thread to the render thread through three reused slots, like
/// the original's DataChannel (docs/engine/ARCHITECTURE.md §2.2). The writer fills a slot and
/// publishes it; the reader takes the newest published frame and keeps it until a newer one
/// arrives. Neither ever waits for the other: the writer always finds a slot that is neither
/// held by the reader nor waiting for it, and frames the reader never took are overwritten.
/// One writer thread and one reader thread.
/// </summary>
public sealed class FrameExchange<T>
    where T : class
{
    private const int None = -1;

    private readonly T[] slots;
    private readonly Lock gate = new();
    private int writing = None;
    private int published = None;
    private int held = None;

    public FrameExchange(Func<T> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        slots = [create(), create(), create()];
    }

    /// <summary>Writer: a slot to fill, which no one else touches until <see cref="Publish"/>.</summary>
    public T BeginWrite()
    {
        lock (gate)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (i != held && i != published)
                {
                    writing = i;
                    return slots[i];
                }
            }
        }
        throw new InvalidOperationException("no free frame slot");   // unreachable with three slots
    }

    /// <summary>Writer: makes the slot from <see cref="BeginWrite"/> the newest frame.</summary>
    public void Publish()
    {
        lock (gate)
        {
            if (writing == None)
            {
                throw new InvalidOperationException("Publish without BeginWrite");
            }
            published = writing;
            writing = None;
        }
    }

    /// <summary>
    /// Reader: the newest published frame, which the reader now holds (giving back the one it
    /// held before); the held frame again when nothing newer was published; null before the first.
    /// </summary>
    public T? Acquire()
    {
        lock (gate)
        {
            if (published != None)
            {
                held = published;
                published = None;
            }
            return held == None ? null : slots[held];
        }
    }
}
