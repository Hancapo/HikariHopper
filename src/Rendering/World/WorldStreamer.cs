using System.Collections.Concurrent;
using System.Numerics;
using HikariHopper.Core;
using RedFury.World;

namespace HikariHopper.Rendering.World;

/// <summary>
/// Streams a world into a viewport around its camera (after RedFury's WorldSession, without grass and lights yet). Ten
/// times a second the LOD graph is cut where the camera is: the whole map through its lowest levels, the detail near the
/// camera, within the memory budget. A worker reads the assets the cut asks for from the game's archives, one at a
/// time, and the viewport uploads one of those read each frame. Only the UI thread touches the renderer.
/// </summary>
internal sealed class WorldStreamer : IDisposable
{
    /// <summary>What the world may keep, fixed for now.</summary>
    internal const long ResidenceBytes = 4L * 1024 * 1024 * 1024;

    // Nearby sibling groups streamed at once; admission is checked on their combined cost.
    private const int MaxRefinementGroups = 8;

    // Assets read ahead of the one-per-frame upload, by count and bytes.
    private const int PreparedLimit = 8;
    private const long PreparedBytesLimit = 128L * 1024 * 1024;

    // The largest asset uploaded at once.
    private const long LargestAsset = 384L * 1024 * 1024;

    private readonly Rdr2WorldIndex index;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly SemaphoreSlim workAvailable = new(0, 1);
    private readonly ConcurrentQueue<WorldAsset> prepared = new();
    private readonly HashSet<WorldAssetKey> queued = [], failed = [];
    private readonly WorldDeferredRequests deferred = new();
    private readonly WorldStreamingBudget budget = new(ResidenceBytes);
    private readonly List<string> diagnostics = [];
    private readonly WorldTimeFilter timeFilter = new();
    private readonly Task worker;
    private WorldAssetKey[] wanted = [];
    private WorldAssetKey? loading;
    private HashSet<WorldAssetKey> resident = [];
    private long preparedBytes;
    private float selectionTime;
    private Task<(IReadOnlySet<WorldEntityId> Cut, bool Pressure)>? planning;
    private bool planPressure, admissionPressure;
    private IReadOnlySet<WorldEntityId> plan = new HashSet<WorldEntityId>();
    private Vector3 plannedPosition = new(float.NaN);
    private long plannedRevision = -1;
    private int plannedFailures = -1;
    private int? plannedHour;
    private Vector3 selectedPosition = new(float.NaN);
    private long selectedRevision = -1;
    private IReadOnlySet<WorldEntityId>? selectedPlan;
    private HashSet<WorldAssetKey> selectedReady = [];
    private int selectedFailures = -1;
    private int? selectedHour;
    private bool selectionPending = true;
    private bool closing;
    private WorldSelection selected = new([], []);
    private ViewportRenderer? renderer;

    public WorldStreamer(Rdr2WorldIndex index)
    {
        this.index = index;
        worker = Task.Run(Work);
    }

    /// <summary>What it says in the status bar: the placements drawn, and what's streamed in.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Whether assets are still being read or uploaded for where the camera is.</summary>
    public bool Busy
    {
        get
        {
            lock (gate)
                return planning is not null || loading is not null || !prepared.IsEmpty
                    || wanted.Any(key => !resident.Contains(key) && !failed.Contains(key) && !deferred.Contains(key));
        }
    }

    /// <summary>What couldn't be read, and why: an asset and its message, at most 512 of them.</summary>
    public IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (gate)
                return [.. diagnostics];
        }
    }

    // The worker: reads the assets wanted, one at a time, while there's room for them ahead of the uploads.
    private async Task Work()
    {
        try
        {
            while (!stop.IsCancellationRequested)
                if (!LoadNext())
                    await workAvailable.WaitAsync(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private bool LoadNext()
    {
        WorldAssetKey? next;
        lock (gate)
            next = prepared.Count >= PreparedLimit || preparedBytes >= PreparedBytesLimit ? null
                : wanted.Cast<WorldAssetKey?>().FirstOrDefault(k => !resident.Contains(k!.Value) && !queued.Contains(k.Value)
                    && !failed.Contains(k.Value) && !deferred.Contains(k.Value));
        if (next is not { } key)
            return false;
        lock (gate)
            loading = key;
        try
        {
            WorldAsset asset;
            // The archives held open are the index's, for whichever streamer reads it.
            lock (index.Reads)
                asset = WorldAsset.Load(key, index.Assets, index.Crypto, index.Reads);
            if (asset.Bytes > LargestAsset)
                throw new InvalidDataException($"Asset exceeds the world's upload budget ({asset.Bytes:N0} bytes).");
            lock (gate)
            {
                budget.Observe(key, asset.MemoryCost);
                foreach (string message in asset.Diagnostics)
                    Note($"{key}: {message}");
                if (!wanted.Contains(key))
                    return true;
                queued.Add(key);
                preparedBytes += asset.Bytes;
                prepared.Enqueue(asset);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or OverflowException or IndexOutOfRangeException)
        {
            MarkFailed(key, error);
        }
        finally
        {
            lock (gate)
                loading = null;
        }
        return true;
    }

    /// <summary>
    /// Before each frame, on the UI thread: the cut and what's wanted ten times a second, and one asset read up to the
    /// GPU when there's one.
    /// </summary>
    /// <param name="hours">The time of day, which time archetypes (lights on at night, say) follow.</param>
    public void Update(ViewportRenderer viewport, float seconds, float hours)
    {
        if (!ReferenceEquals(renderer, viewport))
        {
            // Another viewport (the native view was made again): it holds nothing yet.
            renderer = viewport;
            selectionPending = true;
            selectedReady = [];
        }
        selectionTime += seconds;
        bool selectionDue = selectionTime >= .1f || selectionPending;
        if (selectionDue)
        {
            float elapsed = selectionTime;
            selectionTime = 0;
            UpdateSelection(viewport, elapsed, Math.Clamp((int)hours, 0, 23));
        }
        UploadPrepared(viewport);
        if (selectionDue)
            Status = string.Create(System.Globalization.CultureInfo.CurrentCulture,
                $"{viewport.WorldDrawnCount:N0} entities · {viewport.WorldBytes / 1048576:N0} MB streamed");
    }

    private void UpdateSelection(ViewportRenderer viewport, float elapsed, int hour)
    {
        WorldLodGraph graph = index.Lod;
        Vector3 eye = viewport.Camera.Eye;
        bool selectionChanged;
        lock (gate)
        {
            IReadOnlySet<WorldAssetKey> currentResident = viewport.WorldResident;
            foreach (WorldAssetKey key in currentResident)
                if (viewport.WorldAssetMemory(key) is { } cost)
                    budget.Observe(key, cost);
            if (planning?.IsCompleted == true)
            {
                (plan, planPressure) = planning.GetAwaiter().GetResult();
                planning = null;
            }
            if (planning is null && (plannedPosition != eye || plannedRevision != budget.Revision || plannedFailures != failed.Count || plannedHour != hour))
            {
                WorldStreamingBudget costs = budget.Snapshot();
                HashSet<WorldAssetKey> unavailable = [.. failed];
                plannedPosition = eye;
                plannedRevision = budget.Revision;
                plannedFailures = failed.Count;
                plannedHour = hour;
                // The plan reads the graph's metadata alone; only Select changes its state.
                planning = Task.Run(() => costs.Plan(graph, eye, unavailable.Contains, hour));
            }
            HashSet<WorldAssetKey> ready = [.. currentResident.Where(viewport.IsWorldReady)];
            selectionChanged = selectionPending || selectedPosition != eye || selectedRevision != budget.Revision
                || !ReferenceEquals(selectedPlan, plan) || selectedFailures != failed.Count || !selectedReady.SetEquals(ready)
                || selectedHour != hour || timeFilter.HasPendingSwitches;
            if (selectionChanged)
            {
                // The whole cut near the camera stays, not only what's in view.
                budget.Pin(selected.Requested.Where(viewport.IsWorldReady));
                IReadOnlyList<WorldAssetKey> previousRequests = selected.Requested;
                admissionPressure = false;
                bool Admit(WorldRefinement group)
                {
                    if (group.Required.Any(failed.Contains))
                        return false;
                    bool allowed = budget.Allows(group);
                    admissionPressure |= !allowed;
                    return allowed;
                }
                // Time archetypes out of their hours are left out; those that mustn't switch in view wait until they're out of it.
                float aspect = viewport.Aspect;
                Vector3 forward = viewport.Camera.Forward;
                timeFilter.Hour = hour;
                timeFilter.InView = (center, radius) => new CameraFrustum(eye, forward, aspect).IsVisible(center, radius);
                timeFilter.BeginSelection();
                selected = graph.Select(eye, viewport.IsWorldReady, null, Admit, failed.Contains, plan, MaxRefinementGroups, timeFilter.IsActive);
                // A replaced parent can free room even when nothing uploaded changed.
                selectionPending = !previousRequests.SequenceEqual(selected.Requested);
                selectedPosition = eye;
                selectedRevision = budget.Revision;
                selectedPlan = plan;
                selectedReady = ready;
                selectedFailures = failed.Count;
                selectedHour = hour;
            }
        }
        if (selectionChanged)
            viewport.SetWorldSelection(selected);
        lock (gate)
        {
            HashSet<WorldAssetKey> protect = [.. selected.Requested.Where(viewport.IsWorldReady)];
            deferred.Update(selected.Requested, protect, budget.Limit);
            HashSet<WorldAssetKey> current = [.. viewport.WorldResident];
            bool changed = selectionChanged || !resident.SetEquals(current);
            resident = current;
            wanted = [.. selected.Requested];
            if (changed || admissionPressure || planPressure)
                SignalWorker();
        }
    }

    // One asset read up to the GPU, if one's waiting and still wanted.
    private void UploadPrepared(ViewportRenderer viewport)
    {
        if (!prepared.TryDequeue(out WorldAsset? asset))
            return;
        if (selected.Requested.Contains(asset.Key))
        {
            HashSet<WorldAssetKey> protect = [.. selected.Requested.Where(viewport.IsWorldReady)];
            try
            {
                if (!viewport.AddWorldAsset(asset, protect, budget.Limit))
                    lock (gate)
                        deferred.Add(asset.Key);
            }
            catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException or WebGpuSharp.WebGPUException)
            {
                MarkFailed(asset.Key, error);
            }
        }
        lock (gate)
        {
            // What's uploaded is told before its place in the queue is let go, so the worker doesn't read it again.
            resident = [.. viewport.WorldResident];
            queued.Remove(asset.Key);
            preparedBytes -= asset.Bytes;
            SignalWorker();
        }
    }

    // Under the lock: the worker looks again. Signals coalesce, and it checks after every wake.
    private void SignalWorker()
    {
        if (!closing && workAvailable.CurrentCount == 0)
            workAvailable.Release();
    }

    private void MarkFailed(WorldAssetKey key, Exception error)
    {
        lock (gate)
        {
            failed.Add(key);
            budget.Forget(key);
            Note($"{key}: {error.Message}");
        }
    }

    private void Note(string message)
    {
        if (diagnostics.Count < 512)
            diagnostics.Add(message);
    }

    /// <summary>Stops the worker, without waiting for the asset it's reading; what's uploaded stays with the viewport.</summary>
    public void Dispose()
    {
        lock (gate)
            closing = true;
        stop.Cancel();
        _ = worker.ContinueWith(_ =>
        {
            prepared.Clear();
            workAvailable.Dispose();
            stop.Dispose();
        }, TaskScheduler.Default);
    }
}
