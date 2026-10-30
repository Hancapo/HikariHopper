namespace HikariHopper.Infrastructure;

/// <summary>A bounded worker pool with priorities, the managed equivalent of a <c>QThreadPool</c> with
/// <c>setMaxThreadCount</c>: higher priorities start first, equal priorities run FIFO, and <see cref="Clear"/>
/// drops work that has not started yet. Work items deliver their own results (usually by posting to the UI thread).</summary>
public sealed class PriorityWorkQueue(int maxThreadCount)
{
    private readonly object gate = new();
    private readonly List<(int Priority, long Sequence, Action Work)> queued = [];
    private long sequence;
    private int running;

    public int MaxThreadCount { get; } = maxThreadCount > 0 ? maxThreadCount : throw new ArgumentOutOfRangeException(nameof(maxThreadCount));

    /// <summary>Queues <paramref name="work"/>; exceptions it throws are swallowed, so it must report its own failures.</summary>
    public void Start(Action work, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (gate)
        {
            queued.Add((priority, sequence++, work));
            if (running >= MaxThreadCount)
                return;
            running++;
        }
        _ = Task.Run(Drain);
    }

    /// <summary>Removes queued work that has not started; running work continues.</summary>
    public void Clear()
    {
        lock (gate)
            queued.Clear();
    }

    /// <summary>Blocks until no work is queued or running. Returns false on timeout.</summary>
    public bool WaitForDone(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        lock (gate)
        {
            while (running > 0 || queued.Count > 0)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(gate, remaining))
                    return running == 0 && queued.Count == 0;
            }
            return true;
        }
    }

    private void Drain()
    {
        while (true)
        {
            Action work;
            lock (gate)
            {
                if (queued.Count == 0)
                {
                    running--;
                    Monitor.PulseAll(gate);
                    return;
                }
                int next = 0;
                for (int index = 1; index < queued.Count; index++)
                    if (queued[index].Priority > queued[next].Priority)
                        next = index;
                work = queued[next].Work;
                queued.RemoveAt(next);
            }
            try
            { work(); }
            catch (Exception)
            {
                // Work items report their own failures; one failure must not stop the worker.
            }
        }
    }
}
