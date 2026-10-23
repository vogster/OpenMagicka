using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Tests.Engine;

public class RenderQueueTests
{
    private sealed class Device
    {
        public int ThreadId = Environment.CurrentManagedThreadId;
        public List<string> Created { get; } = [];
    }

    [Fact]
    public async Task Work_RunsOnTheThreadThatDrainsTheQueue_AndCompletesItsTask()
    {
        var queue = new RenderQueue<Device>();
        var device = new Device();
        Task<int>? task = null;
        var loader = new Thread(() => task = queue.Enqueue(d => { d.Created.Add("texture"); return Environment.CurrentManagedThreadId; }));
        loader.Start();
        loader.Join();

        Assert.False(task!.IsCompleted);
        Assert.Equal(1, queue.RunPending(device));

        Assert.Equal(device.ThreadId, await task);
        Assert.Equal(["texture"], device.Created);
    }

    [Fact]
    public void RunPending_RunsAtMostTheBudget_InOrder()
    {
        var queue = new RenderQueue<Device>();
        var device = new Device();
        for (var i = 0; i < 5; i++)
        {
            var name = $"r{i}";
            queue.Enqueue(d => d.Created.Add(name));
        }

        Assert.Equal(2, queue.RunPending(device, maxItems: 2));
        Assert.Equal(["r0", "r1"], device.Created);
        Assert.Equal(3, queue.Pending);
        Assert.Equal(3, queue.RunPending(device));
        Assert.Equal(0, queue.RunPending(device));
    }

    [Fact]
    public async Task AFailingItem_FaultsItsTask_AndTheRestStillRun()
    {
        var queue = new RenderQueue<Device>();
        var device = new Device();
        var failing = queue.Enqueue<int>(_ => throw new InvalidDataException("bad texture"));
        var next = queue.Enqueue(_ => 7);

        queue.RunPending(device);

        await Assert.ThrowsAsync<InvalidDataException>(() => failing);
        Assert.Equal(7, await next);
    }

    [Fact]
    public async Task Close_CancelsWhatIsPending_AndRefusesNewWork()
    {
        var queue = new RenderQueue<Device>();
        var pending = queue.Enqueue(_ => 1);

        queue.Close();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var late = queue.Enqueue(_ => 2);        // during shutdown: cancelled, not an exception
        Assert.True(late.IsCanceled);
        Assert.Equal(0, queue.RunPending(new Device()));
    }

    [Fact]
    public async Task WorkThatIsCancelled_CancelsItsTask()
    {
        var queue = new RenderQueue<Device>();
        var task = queue.Enqueue<int>(_ => throw new OperationCanceledException());

        queue.RunPending(new Device());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public void RunPending_StopsWhenTheTimeBudgetIsSpent()
    {
        var queue = new RenderQueue<Device>();
        for (var i = 0; i < 5; i++)
        {
            queue.Enqueue(_ => Thread.Sleep(20));
        }

        var ran = queue.RunPending(new Device(), maxItems: 100, timeBudget: TimeSpan.FromMilliseconds(30));

        Assert.InRange(ran, 1, 3);                // at least one item always runs
        Assert.Equal(5 - ran, queue.Pending);
    }

    [Fact]
    public async Task EnqueueRacingClose_NeverLeavesATaskHanging()
    {
        for (var round = 0; round < 200; round++)
        {
            var queue = new RenderQueue<Device>();
            var tasks = new System.Collections.Concurrent.ConcurrentBag<Task<int>>();
            var producers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                {
                    tasks.Add(queue.Enqueue(_ => 1));
                }
            })).ToArray();
            queue.RunPending(new Device(), maxItems: 10);
            queue.Close();
            await Task.WhenAll(producers);

            Assert.All(tasks, t => Assert.True(t.IsCompleted, "a task was left hanging"));
        }
    }

    [Fact]
    public void ClosingFromAnItem_StopsTheRest()
    {
        var queue = new RenderQueue<Device>();
        queue.Enqueue(_ => queue.Close());
        var second = queue.Enqueue(_ => 2);

        Assert.Equal(1, queue.RunPending(new Device()));
        Assert.True(second.IsCanceled);
    }
}
