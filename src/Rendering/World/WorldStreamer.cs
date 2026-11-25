using System.Collections.Concurrent;
using System.Numerics;
using HikariHopper.Core;
using RedFury.World;

namespace HikariHopper.Rendering.World;

/// <summary>
/// Streams a world into a viewport around its camera (after RedFury's WorldSession). Ten times a second the LOD graph is
/// cut where the camera is: the whole map through its lowest levels, the detail near the camera, within the memory
/// budget. Workers read the assets the cut asks for from the game's archives (several at once where the index can, one
/// at a time otherwise), and the first the grass batches nearest the camera; the viewport uploads the assets read, as many
/// a frame as its upload time allows. The cut's static lights are bound to their placements when it changes. Only the UI
/// thread touches the renderer.
/// </summary>
internal sealed class WorldStreamer : IDisposable
{
    /// <summary>What the world may keep, fixed for now.</summary>
    internal const long ResidenceBytes = 4L * 1024 * 1024 * 1024;

    // Nearby sibling groups streamed at once; admission is checked on their combined cost. RedFury's eight left the
    // uploads waiting on the next cut while a city's levels came in.
    private const int MaxRefinementGroups = 32;

    // Assets read ahead of the uploads, by count (those being read included) and bytes.
    private const int PreparedLimit = 16;
    private const long PreparedBytesLimit = 128L * 1024 * 1024;

    // How long a frame uploads assets read: 2.5 ms while the camera stays, 1 ms while it moves, so the detail coming in
    // as it flies doesn't hold its frames back (the rest goes up once it stops).
    private static readonly long FrameUploadTicks = System.Diagnostics.Stopwatch.Frequency / 400, MovingUploadTicks = System.Diagnostics.Stopwatch.Frequency / 1000;

    /// <summary>
    /// How long an asset the cut no longer asks for stays up, in seconds of streaming: see <see cref="ReleaseAfter"/>.
    /// </summary>
    internal const double ReleaseAfterSeconds = 30;

    // How long a frame lets go of assets left behind: a quarter of a millisecond is a dozen or more of them, several
    // times as many as a flight brings in.
    private static readonly long ReleaseTicks = System.Diagnostics.Stopwatch.Frequency / 4000;

    // What a frame moves out of the buffer pages the assets let go left half empty, once the camera is still, nothing
    // is coming in and nothing is left to let go (what's about to go isn't worth moving): two assets' bundles are
    // recorded again in a frame (RecordsPerFrame), no more.
    private const long CompactBytes = 1024 * 1024;
    private const int CompactAssets = 2;

    // Where the camera was at the last frame's uploads, and whether it was there the frame before too.
    private Vector3 uploadEye = new(float.NaN);
    private bool cameraStill;

    // The largest asset uploaded at once.
    private const long LargestAsset = 384L * 1024 * 1024;

    // The plants expanded at once, nearest batches first.
    private const int MaximumGrassInstances = 131072;

    private readonly WorldIndex index;
    private readonly Func<WorldLodNode, InteriorAmbient?> ambient;
    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly SemaphoreSlim workAvailable;
    private readonly ConcurrentQueue<WorldAsset> prepared = new();
    private readonly HashSet<WorldAssetKey> queued = [], failed = [];
    private readonly WorldDeferredRequests deferred = new();
    private readonly WorldStreamingBudget budget = new(ResidenceBytes);
    private readonly List<string> diagnostics = [];
    private readonly WorldTimeFilter timeFilter = new();
    private readonly Task[] workers;

    // The sky behind the world, read once, as the streaming starts.
    private readonly Task<GtaSkyDome?> sky;
    // Or RDR2's.
    private readonly Task<Rdr2Sky?> rdr2Sky;

    // The lens flare over it, read once, as the streaming starts.
    private readonly Task<GtaLensFlare?> lensFlare;
    // The cloud hat over it, read alongside.
    private readonly Task<GtaCloudHats?> cloudHats;
    // What the lights' coronas are drawn with, read once, as the streaming starts; and what they were last made for:
    // the cut's lights with coronas and where its lights are, the camera and the hour.
    private readonly Task<GtaCoronaData?> coronas;
    private IReadOnlyList<WorldCoronaLight> coronaLights = [];
    private IReadOnlyList<Vector3> coronaScene = [];
    private Vector3 coronaEye = new(float.NaN);
    private int coronaHour = -1;
    private bool coronasShown, coronasDue;
    private readonly List<GtaCoronaInstance> coronaBuffer = [];
    private WorldAssetKey[] wanted = [];
    private readonly HashSet<WorldAssetKey> loading = [];

    // What the viewport holds, copied when it changed (its WorldResidentRevision) into the one set, which the workers
    // read under the lock; and the revision the workers were last told of.
    private readonly HashSet<WorldAssetKey> resident = [];
    private long residentRevision = -1, signalledRevision = -1;

    // The viewport's costs last told to the budget (its WorldMemoryRevision).
    private long memoryRevision = -1;
    private readonly Action<WorldAssetKey, WorldMemoryCost> observe;

    // The budget's costs as the plan in the background reads them: one copy, filled again once no plan reads it.
    private WorldStreamingBudget? planCosts;

    // The ready assets the cut asks for, which an upload doesn't evict: filled again each frame that uploads.
    private readonly HashSet<WorldAssetKey> uploadProtect = [];

    // The seconds streamed, which say how long ago the cut last asked for an asset. A frame counts for a second at
    // most: time the streaming was stalled or paused for isn't time an asset was left behind.
    private double clock;
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
    private long selectedReady = -1;
    private int selectedFailures = -1;
    private int? selectedHour;
    private bool selectionPending = true, firstSelection = true;
    private volatile bool uploadPending;
    private (WorldSelection Display, Vector3 Eye, int Hour)? applying;
    private bool closing;
    private WorldSelection selected = new([], []);
    private ViewportRenderer? renderer;

    // The grass batches wanted, those expanded (their descriptor and the batch's plants), and those that couldn't be.
    private WorldLodNode[] wantedGrass = [];
    private readonly Dictionary<WorldEntityId, (WorldLodNode Source, WorldLodNode Ready)> readyGrass = [];
    private readonly HashSet<WorldEntityId> failedGrass = [];
    private WorldEntityId? loadingGrass;
    private long grassRevision, selectedGrassRevision = -1;

    // What the lights were last bound for.
    private IReadOnlyList<WorldLodNode> lightCoverage = [];
    private long lightReady = -1;
    private bool lightsTruncated;
    private Vector3 lightEye;
    private int? lightHour;

    // The camera's place in the world's timecycle, worked out each frame into the same one.
    private readonly TimecyclePlace place = new();

    public WorldStreamer(WorldIndex index)
    {
        ambient = index.AmbientOf;
        this.index = index;
        observe = budget.Observe;
        // Half the cores, at most four, leave the UI thread and the GPU's driver theirs.
        int count = index.LoadsConcurrently ? Math.Clamp(Environment.ProcessorCount / 2, 1, 4) : 1;
        workAvailable = new SemaphoreSlim(0, count);
        workers = [.. Enumerable.Range(0, count).Select(n => Task.Run(() => Work(grass: n == 0)))];
        sky = Task.Run(index.LoadSky);
        rdr2Sky = Task.Run(index.LoadRdr2Sky);
        lensFlare = Task.Run(index.LoadLensFlare);
        cloudHats = Task.Run(index.LoadCloudHats);
        coronas = Task.Run(index.LoadCoronas);
    }

    /// <summary>The cut's lights bound to their placements, last time they were.</summary>
    public int LightCount { get; private set; }


    /// <summary>
    /// How long an asset the cut no longer asks for stays up before it's let go, in seconds of streaming; infinity keeps
    /// them until the budget needs their room. Without it the world kept every model the camera had passed until the
    /// budget was full: a flight across the city held all of it, and the memory never came back down.
    /// </summary>
    /// <remarks>
    /// Thirty seconds: the cut itself is never let go (it's asked for at every selection), so this only says how long
    /// what's behind waits for the camera to come back. Flying straight across GTA V's city for 90 seconds at 50 m/s,
    /// the cut dropped assets 37,700 times and asked again for 30,200 of them: 26,900 within a second (the plan
    /// settling), 3,100 within thirty, 200 later. A shorter wait reads those again (1,000 more with ten seconds); a
    /// longer one only keeps more of the trail, about 150 assets and 40 MB of the budget for each second of that flight.
    /// </remarks>
    internal double ReleaseAfter { get; set; } = ReleaseAfterSeconds;

    /// <summary>The plants expanded and shown.</summary>
    public int GrassInstances { get; private set; }

    /// <summary>What it says in the status bar: the placements drawn, and what's streamed in.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Whether assets are still being read or uploaded for where the camera is.</summary>
    public bool Busy
    {
        get
        {
            lock (gate)
                return !sky.IsCompleted || !lensFlare.IsCompleted || !cloudHats.IsCompleted || planning is not null || loading.Count > 0 || loadingGrass is not null || !prepared.IsEmpty || uploadPending
                    || wanted.Any(key => !resident.Contains(key) && !failed.Contains(key) && !deferred.Contains(key))
                    || wantedGrass.Any(node => !readyGrass.ContainsKey(node.Id) && !failedGrass.Contains(node.Id));
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

    // A worker: reads the assets wanted while there's room for them ahead of the uploads, and (the first) the grass
    // batches wanted.
    private async Task Work(bool grass)
    {
        try
        {
            Func<WorldLodNode, CancellationToken, WorldLodNode>? grassLoader = grass ? index.CreateGrass() : null;
            while (!stop.IsCancellationRequested)
            {
                bool loaded = LoadNext();
                loaded = LoadGrassNext(grassLoader) || loaded;
                if (!loaded)
                    await workAvailable.WaitAsync(stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private bool LoadNext()
    {
        WorldAssetKey? next = null;
        lock (gate)
        {
            // Looked for in place: as a sequence of nullable keys, each one wanted was boxed at every look, 10 MB a
            // second of them while a city came in.
            if (prepared.Count + loading.Count < PreparedLimit && preparedBytes < PreparedBytesLimit)
                foreach (WorldAssetKey candidate in wanted)
                    if (!resident.Contains(candidate) && !queued.Contains(candidate) && !failed.Contains(candidate) && !deferred.Contains(candidate)
                        && !loading.Contains(candidate))
                    {
                        next = candidate;
                        break;
                    }
            if (next is { } taken)
                loading.Add(taken);
        }
        if (next is not { } key)
            return false;
        try
        {
            WorldAsset asset;
            // The archives held open are the index's, for whichever streamer reads it.
            if (index.LoadsConcurrently)
                asset = index.LoadAsset(key);
            else
                lock (index.Gate)
                    asset = index.LoadAsset(key);
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
                loading.Remove(key);
        }
        return true;
    }

    // Expands the nearest grass batch wanted and not yet expanded.
    private bool LoadGrassNext(Func<WorldLodNode, CancellationToken, WorldLodNode>? loader)
    {
        if (loader is null)
            return false;
        WorldLodNode? source;
        lock (gate)
        {
            source = wantedGrass.FirstOrDefault(node => !readyGrass.ContainsKey(node.Id) && !failedGrass.Contains(node.Id));
            loadingGrass = source?.Id;
        }
        if (source is null)
            return false;
        try
        {
            WorldLodNode expanded;
            lock (index.Gate)
                expanded = loader(source, stop.Token);
            lock (gate)
                if (wantedGrass.Any(node => ReferenceEquals(node, source)))
                {
                    readyGrass[source.Id] = (source, expanded);
                    grassRevision++;
                }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or OverflowException or IndexOutOfRangeException)
        {
            lock (gate)
            {
                failedGrass.Add(source.Id);
                grassRevision++;
                Note($"Grass {source.Id}: {error.Message}");
            }
        }
        finally
        {
            lock (gate)
                loadingGrass = null;
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
            // Another viewport (the native view was made again): it holds nothing yet. The game's pipelines start
            // compiling now, while the first assets are read.
            renderer = viewport;
            viewport.PrepareWorldFamilies(index.Families);
            selectionPending = true;
            firstSelection = true;
            selectedReady = lightReady = residentRevision = signalledRevision = memoryRevision = -1;
        }
        clock += Math.Clamp(seconds, 0, 1);
        Apply(viewport);
        // The timecycle at the camera's place, every frame as the game works it out; the view takes it only where it changed.
        // A world without them (RDR2's) has GLOBAL's alone, whatever world the view showed before.
        if (index.TimecycleZones is { } zones)
        {
            zones.Evaluate(viewport.Camera.Eye, viewport.Hours, place);
            viewport.SetTimecyclePlace(place);
        }
        else
            viewport.SetTimecyclePlace(null);
        // The sky as soon as it's read, and again for a viewport that hasn't it.
        if (sky.IsCompletedSuccessfully && sky.Result is { } dome && !viewport.HasWorldSky)
            viewport.ShowWorldSky(dome);
        if (rdr2Sky.IsCompletedSuccessfully && rdr2Sky.Result is { } air && !viewport.HasRdr2Sky)
            viewport.ShowRdr2Sky(air);
        if (lensFlare.IsCompletedSuccessfully && lensFlare.Result is { } flare && !viewport.HasWorldLensFlare)
            viewport.ShowWorldLensFlare(flare);
        if (cloudHats.IsCompletedSuccessfully && cloudHats.Result is { } hats && !viewport.HasWorldCloudHats)
            viewport.ShowWorldCloudHats(hats);
        UpdateCoronas(viewport, hours);
        selectionTime += seconds;
        // Ten times a second, as RedFury's session has it (and at once for a new viewport); the uploads go on every frame.
        bool selectionDue = selectionTime >= .1f || firstSelection;
        firstSelection = false;
        if (selectionDue)
        {
            float elapsed = selectionTime;
            selectionTime = 0;
            UpdateSelection(viewport, elapsed, Math.Clamp((int)hours, 0, 23));
        }
        UploadPrepared(viewport);
        uploadPending = viewport.HasPendingWorldUpload;
        // What the cut left behind long enough ago, a few a frame.
        viewport.ReleaseWorldAssets(ReleaseTicks);
        // And, once the camera rests with nothing coming in and all of them gone, the pages they left half empty are
        // emptied into the others.
        if (cameraStill && !uploadPending && prepared.IsEmpty && viewport.WorldLeftBehind == 0)
            viewport.CompactWorldBuffers(CompactBytes, CompactAssets);
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
            // Only when an asset's cost changed: an upload, or its placements' storage growing.
            if (memoryRevision != viewport.WorldMemoryRevision)
            {
                memoryRevision = viewport.WorldMemoryRevision;
                viewport.ObserveWorldMemory(observe);
            }
            if (planning?.IsCompleted == true)
            {
                (plan, planPressure) = planning.GetAwaiter().GetResult();
                planning = null;
            }
            if (planning is null && (plannedPosition != eye || plannedRevision != budget.Revision || plannedFailures != failed.Count || plannedHour != hour))
            {
                // No plan is running, so none reads the copy made for the last one: it's filled again.
                WorldStreamingBudget costs = planCosts = budget.Snapshot(planCosts);
                HashSet<WorldAssetKey> unavailable = [.. failed];
                plannedPosition = eye;
                plannedRevision = budget.Revision;
                plannedFailures = failed.Count;
                plannedHour = hour;
                // The plan reads the graph's metadata alone; only Select changes its state.
                planning = Task.Run(() => costs.Plan(graph, eye, unavailable.Contains, hour));
            }
            long ready = viewport.WorldReadyRevision;
            selectionChanged = selectionPending || selectedPosition != eye || selectedRevision != budget.Revision
                || !ReferenceEquals(selectedPlan, plan) || selectedFailures != failed.Count || selectedReady != ready
                || selectedHour != hour || timeFilter.HasPendingSwitches || selectedGrassRevision != grassRevision;
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
                selectedGrassRevision = grassRevision;
            }
        }
        if (selectionChanged)
        {
            // The grass batches nearest the camera, expanded, in their descriptors' place; the others wait.
            WorldSelection display;
            lock (gate)
            {
                wantedGrass = WorldGrass.Limit(selected.Coverage, eye, MaximumGrassInstances);
                foreach (var pair in readyGrass.ToArray())
                    if (!wantedGrass.Any(node => ReferenceEquals(node, pair.Value.Source)))
                        readyGrass.Remove(pair.Key);
                WorldLodNode[] coverage = [.. selected.Coverage.Where(node => node.Grass is null)
                    .Concat(wantedGrass.Where(node => readyGrass.ContainsKey(node.Id)).Select(node => readyGrass[node.Id].Ready))];
                GrassInstances = wantedGrass.Where(node => readyGrass.ContainsKey(node.Id)).Sum(node => readyGrass[node.Id].Ready.Instances.Count);
                display = selected with { Visible = coverage, Coverage = coverage };
            }
            // Applied on the next frame, so this one carries the selection and the next the coverage's new storage.
            applying = (display, eye, hour);
        }
        // The cut is asked for now; what it hasn't asked for in a while is lined up to go, a few a frame.
        viewport.KeepWorldAssets(selected.Requested, clock);
        if (double.IsFinite(ReleaseAfter))
            viewport.QueueWorldReleases(clock - ReleaseAfter);
        lock (gate)
        {
            deferred.Update(selected.Requested, selected.Requested.Where(viewport.IsWorldReady), budget.Limit);
            bool changed = selectionChanged || signalledRevision != viewport.WorldResidentRevision;
            signalledRevision = viewport.WorldResidentRevision;
            SyncResident(viewport);
            if (selectionChanged)
                wanted = [.. selected.Requested];
            if (changed || admissionPressure || planPressure || wantedGrass.Any(node => !readyGrass.ContainsKey(node.Id) && !failedGrass.Contains(node.Id)))
                SignalWorker();
        }
    }

    // Under the lock: what the viewport holds, for the workers, copied only when it changed.
    private void SyncResident(ViewportRenderer viewport)
    {
        if (residentRevision == viewport.WorldResidentRevision)
            return;
        residentRevision = viewport.WorldResidentRevision;
        viewport.CopyWorldResident(resident);
    }

    // The selection made last frame, applied: the renderer's coverage, and the lights bound again when the cut or its
    // uploads changed, the hour did, or the camera moved and some were left out.
    private void Apply(ViewportRenderer viewport)
    {
        if (applying is not var (display, eye, hour))
            return;
        applying = null;
        viewport.WorldAmbient = ambient;
        viewport.SetWorldSelection(display);
        // The lights again when the cut or its uploads changed, the hour did, or the camera moved and some were left out.
        long ready = viewport.WorldReadyRevision;
        lightDisplay = (display, eye, hour);
        if (!lightCoverage.SequenceEqual(display.Coverage, ReferenceEqualityComparer.Instance) || lightReady != ready
            || lightsTruncated && lightEye != eye || lightHour != hour)
            BindLights(viewport, display, eye, hour, ready);
    }

    // The cut last applied, which the lights are bound for.
    private (WorldSelection Display, Vector3 Eye, int Hour)? lightDisplay;

    // The cut's lights bound to their placements, with their coronas' once those can be drawn.
    private void BindLights(ViewportRenderer viewport, WorldSelection display, Vector3 eye, int hour, long ready)
    {
        bool withCoronas = coronas.IsCompletedSuccessfully && coronas.Result is not null;
        WorldLightBinding lighting = WorldLightBinding.Resolve(display, viewport.WorldAssetLightBones, eye, hour: hour,
            interior: withCoronas ? index.IsInInterior : null);
        viewport.SetLocalLights(lighting.Lights);
        if (withCoronas)
        {
            coronaLights = lighting.Coronas;
            coronaScene = lighting.Positions;
            coronasDue = true;
        }
        LightCount = lighting.Lights.Count;
        lightsTruncated = lighting.IsTruncated;
        lightEye = eye;
        lightHour = hour;
        lightCoverage = display.Coverage;
        lightReady = ready;
        lock (gate)
            foreach (string message in lighting.Diagnostics)
                Note(message);
    }

    // How far past their reach the LOD lights near the camera are taken, and how far the camera goes before they're
    // taken again.
    private const float CoronaMargin = 100, CoronaMove = 50;

    // The coronas' textures once they're read, and their lights again when the cut's lights were bound again, the hour
    // changed, or the camera went far enough for other LOD lights to be in reach.
    private void UpdateCoronas(ViewportRenderer viewport, float hours)
    {
        if (!coronas.IsCompletedSuccessfully || coronas.Result is not { } data)
            return;
        if (!coronasShown || !viewport.HasWorldCoronas)
        {
            viewport.ShowWorldCoronas(data);
            coronasShown = true;
            coronasDue = true;
            // The cut's lights again, with their coronas this time.
            if (lightDisplay is var (display, eye0, hour0))
                BindLights(viewport, display, eye0, hour0, viewport.WorldReadyRevision);
        }
        int hour = Math.Clamp((int)hours, 0, 23);
        Vector3 eye = viewport.Camera.Eye;
        if (!coronasDue && hour == coronaHour && Vector3.DistanceSquared(eye, coronaEye) < CoronaMove * CoronaMove)
            return;
        coronasDue = false;
        coronaHour = hour;
        coronaEye = eye;
        GtaCoronas.Build(coronaBuffer, coronaLights, coronaScene, data.LodLights, data.Visuals, eye, hour, CoronaMargin);
        viewport.SetWorldCoronas(coronaBuffer);
    }

    // The asset on its way up a frame's step further (let go when it's no longer wanted); then, while the frame's upload
    // time lasts, the next ones read that are still wanted, one after another: most are small enough for several a frame,
    // and one a frame kept a city's few thousand waiting most of a minute at 60 frames a second. What's resident is told
    // to the workers once, after.
    private void UploadPrepared(ViewportRenderer viewport)
    {
        Vector3 eye = viewport.Camera.Eye;
        cameraStill = eye == uploadEye;
        long uploadTicks = cameraStill ? FrameUploadTicks : MovingUploadTicks;
        uploadEye = eye;
        if (viewport.WorldUploadKey is { } uploading && !selected.Requested.Contains(uploading))
        {
            viewport.CancelWorldUpload();
            lock (gate)
                SyncResident(viewport);
        }
        if (viewport.HasPendingWorldUpload && !Step(viewport, uploadTicks))
            return;
        long until = System.Diagnostics.Stopwatch.GetTimestamp() + uploadTicks;
        bool protecting = false;
        List<WorldAsset>? taken = null;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until && prepared.TryDequeue(out WorldAsset? asset))
        {
            (taken ??= []).Add(asset);
            if (selected.Requested.Contains(asset.Key))
            {
                // The ready assets the cut asks for aren't evicted for it, nor those put up before it this frame.
                if (!protecting)
                {
                    protecting = true;
                    uploadProtect.Clear();
                    foreach (WorldAssetKey key in selected.Requested)
                        if (viewport.IsWorldReady(key))
                            uploadProtect.Add(key);
                }
                try
                {
                    if (viewport.AddWorldAsset(asset, uploadProtect, budget.Limit))
                        uploadProtect.Add(asset.Key);
                    else
                        lock (gate)
                            deferred.Add(asset.Key);
                }
                catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException or WebGpuSharp.WebGPUException)
                {
                    MarkFailed(asset.Key, error);
                }
            }
            if (viewport.HasPendingWorldUpload && !Step(viewport, uploadTicks))
                break;
        }
        if (taken is null)
            return;
        lock (gate)
        {
            // What's uploaded is told before its place in the queue is let go, so a worker doesn't read it again.
            SyncResident(viewport);
            foreach (WorldAsset asset in taken)
            {
                queued.Remove(asset.Key);
                preparedBytes -= asset.Bytes;
            }
            SignalWorker();
        }
    }

    // A frame's step of the asset on its way up; whether it's done (up, or let go).
    private bool Step(ViewportRenderer viewport, long uploadTicks)
    {
        WorldAssetKey key = viewport.WorldUploadKey!.Value;
        try
        {
            viewport.ProcessWorldUploads(uploadTicks);
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException or WebGpuSharp.WebGPUException)
        {
            MarkFailed(key, error);
            lock (gate)
            {
                SyncResident(viewport);
                SignalWorker();
            }
        }
        return !viewport.HasPendingWorldUpload;
    }

    // Under the lock: the workers look again. Signals coalesce, one per worker, and each checks after every wake.
    private void SignalWorker()
    {
        if (!closing && workAvailable.CurrentCount < workers.Length)
            workAvailable.Release(workers.Length - workAvailable.CurrentCount);
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

    /// <summary>Stops the workers, without waiting for the assets they're reading; what's uploaded stays with the viewport.</summary>
    public void Dispose()
    {
        lock (gate)
            closing = true;
        stop.Cancel();
        _ = Task.WhenAll(workers).ContinueWith(_ =>
        {
            prepared.Clear();
            workAvailable.Dispose();
            stop.Dispose();
        }, TaskScheduler.Default);
    }
}
