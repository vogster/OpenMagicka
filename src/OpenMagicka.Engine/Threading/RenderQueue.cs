using System.Collections.Concurrent;

namespace OpenMagicka.Engine.Threading;

/// <summary>
/// Work that must run on the render thread, the only one that touches the GPU (ADR-0001): the
/// loader decodes content on its own thread and enqueues the creation of textures and buffers;
/// the render thread runs a budget of items per frame. Each item completes the task it returned.
/// Enqueue is safe from any thread; RunPending and Close belong to the render thread.
/// </summary>
public sealed class RenderQueue<TContext>
{
    private readonly ConcurrentQueue<Item> items = new();
    private volatile bool closed;

    public int Pending => items.Count;

    /// <summary>Queues work; after <see cref="Close"/> the task is cancelled at once (shutdown is not an error).</summary>
    public Task<T> Enqueue<T>(Func<TContext, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (closed)
        {
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        }
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        items.Enqueue(new Item(
            context =>
            {
                try
                {
                    completion.SetResult(work(context));
                }
                catch (OperationCanceledException)
                {
                    completion.SetCanceled();
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            },
            () => completion.TrySetCanceled()));
        if (closed && items.TryDequeue(out var raced))
        {
            raced.Cancel();   // Close ran meanwhile; do not leave the task hanging
        }
        return completion.Task;
    }

    public Task Enqueue(Action<TContext> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Enqueue(context =>
        {
            work(context);
            return true;
        });
    }

    /// <summary>
    /// Render thread: runs up to <paramref name="maxItems"/> items in order, stopping early once
    /// <paramref name="timeBudget"/> is spent (at least one item runs); returns how many ran.
    /// </summary>
    public int RunPending(TContext context, int maxItems = int.MaxValue, TimeSpan? timeBudget = null)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var count = 0;
        while (count < maxItems && !closed
            && (count == 0 || timeBudget is not { } budget || System.Diagnostics.Stopwatch.GetElapsedTime(started) < budget)
            && items.TryDequeue(out var item))
        {
            item.Run(context);
            count++;
        }
        return count;
    }

    /// <summary>Cancels every pending item and refuses new ones (the device is going away).</summary>
    public void Close()
    {
        closed = true;
        while (items.TryDequeue(out var item))
        {
            item.Cancel();
        }
    }

    private sealed record Item(Action<TContext> Run, Action Cancel);
}
