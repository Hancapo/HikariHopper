using RedFury.World;

namespace HikariHopper.Rendering.World;

/// <summary>
/// What the world may keep in memory, and what each asset costs (RedFury's, copied): its estimates outlive an asset's
/// eviction, its decoded data doesn't. The streamer holds its lock around every call. The limit is fixed for now.
/// </summary>
internal sealed class WorldStreamingBudget(long limit)
{
    internal long Limit { get; } = limit;

    /// <summary>What the plan aims for: the limit, less room for what's coming in while the old goes.</summary>
    internal long Target => Math.Max(0, Limit - 256L * 1024 * 1024);

    private readonly Dictionary<WorldAssetKey, long> costs = [];
    private HashSet<WorldAssetKey> pinned = [];
    private long pinnedBytes, replacementBytes;
    private readonly Dictionary<WorldAssetKey, WorldMemoryCost> footprints = [];
    private static readonly WorldMemoryCost UnknownCost = new(1024 * 1024);
    private readonly Dictionary<long, long> pinnedShared = [], extraShared = [];
    private readonly HashSet<WorldAssetKey> extraKeys = [];
    private long pinnedFootprintBytes;
    private long pinnedRevision = -1;

    internal long Revision { get; private set; }

    /// <summary>
    /// The cut of the graph the budget allows from <paramref name="eye"/>, and whether it had to leave anything out.
    /// Run on a snapshot only: it reads nothing the loader changes.
    /// </summary>
    internal (IReadOnlySet<WorldEntityId> Cut, bool Pressure) Plan(WorldLodGraph graph, System.Numerics.Vector3 eye, Func<WorldAssetKey, bool> unavailable,
        int? hour = null)
    {
        // Each asset's cost, kept as a running total over the cut while it's refined (RedFury's per-asset Plan): pricing
        // the whole cut at every step allocated most of what the world allocated while flying.
        WorldPlan plan = graph.Plan(eye, Cost, Target, unavailable, hour is { } h ? node => WorldTimeFilter.ActiveAt(node, h) : null);
        return (plan.Refinements, plan.Pressure);
    }

    /// <summary>The costs alone, for planning in the background: it never reads the live loader's.</summary>
    internal WorldStreamingBudget Snapshot()
    {
        var snapshot = new WorldStreamingBudget(Limit);
        foreach (var pair in costs)
            snapshot.costs.Add(pair.Key, pair.Value);
        foreach (var pair in footprints)
            snapshot.footprints.Add(pair.Key, pair.Value);
        return snapshot;
    }

    internal void Observe(WorldAssetKey key, WorldMemoryCost footprint)
    {
        if (footprints.TryGetValue(key, out WorldMemoryCost? previous) && ReferenceEquals(previous, footprint))
            return;
        footprints[key] = footprint;
        Revision++;
        Observe(key, WorldMemoryCost.Total([footprint]));
    }

    private WorldMemoryCost Cost(WorldAssetKey key) =>
        footprints.GetValueOrDefault(key) ?? (costs.TryGetValue(key, out long bytes) ? new WorldMemoryCost(bytes) : UnknownCost);

    internal void Observe(WorldAssetKey key, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        long value = Math.Max(costs.GetValueOrDefault(key), bytes);
        if (!costs.TryGetValue(key, out long old) || old != value)
            Revision++;
        costs[key] = value;
        replacementBytes = Math.Max(replacementBytes, bytes);
    }

    /// <summary>Holds these assets' cost as taken: what's shown stays while more is admitted.</summary>
    internal void Pin(IEnumerable<WorldAssetKey> visible)
    {
        pinned = visible.ToHashSet();
        pinnedBytes = pinned.Sum(k => costs.GetValueOrDefault(k));
        pinnedShared.Clear();
        pinnedFootprintBytes = 0;
        foreach (WorldAssetKey key in pinned)
        {
            WorldMemoryCost cost = Cost(key);
            pinnedFootprintBytes = checked(pinnedFootprintBytes + cost.OwnedBytes);
            foreach (WorldSharedMemory item in cost.Shared)
            {
                long previous = pinnedShared.GetValueOrDefault(item.Identity);
                if (item.Bytes > previous)
                {
                    pinnedFootprintBytes = checked(pinnedFootprintBytes + item.Bytes - previous);
                    pinnedShared[item.Identity] = item.Bytes;
                }
            }
        }
        pinnedRevision = Revision;
    }

    internal void Forget(WorldAssetKey key)
    {
        Revision++;
        costs.Remove(key);
        footprints.Remove(key);
        pinned.Remove(key);
        pinnedBytes = pinned.Sum(k => costs.GetValueOrDefault(k));
        replacementBytes = costs.Values.DefaultIfEmpty().Max();
        Pin(pinned);
    }

    /// <summary>Whether the assets a refinement needs fit beside the pinned ones.</summary>
    internal bool Allows(WorldRefinement group)
    {
        if (pinnedRevision != Revision)
            Pin(pinned);
        if (footprints.Count == 0)
            return pinnedBytes + group.Required.Where(k => !pinned.Contains(k)).Distinct().Sum(k => costs.GetValueOrDefault(k)) + replacementBytes <= Limit;
        long bytes = pinnedFootprintBytes;
        extraShared.Clear();
        extraKeys.Clear();
        foreach (WorldAssetKey key in group.Required)
        {
            if (pinned.Contains(key) || !extraKeys.Add(key))
                continue;
            WorldMemoryCost cost = Cost(key);
            bytes = checked(bytes + cost.OwnedBytes);
            foreach (WorldSharedMemory item in cost.Shared)
            {
                long previous = Math.Max(pinnedShared.GetValueOrDefault(item.Identity), extraShared.GetValueOrDefault(item.Identity));
                if (item.Bytes > previous)
                {
                    bytes = checked(bytes + item.Bytes - previous);
                    extraShared[item.Identity] = item.Bytes;
                }
            }
        }
        return bytes <= Limit - 1024 * 1024;
    }
}

/// <summary>
/// Assets the renderer turned away for want of room (RedFury's, copied): not asked for again until what's wanted, what's
/// protected or the budget changes.
/// </summary>
internal sealed class WorldDeferredRequests
{
    private HashSet<WorldAssetKey> previousWanted = [], previousProtected = [];
    private readonly HashSet<WorldAssetKey> deferred = [];
    private long previousBudget;

    internal bool HasPending => deferred.Count != 0;

    internal bool Contains(WorldAssetKey key) => deferred.Contains(key);

    internal void Add(WorldAssetKey key) => deferred.Add(key);

    internal void Update(IEnumerable<WorldAssetKey> wanted, IEnumerable<WorldAssetKey> protectedAssets, long budget = 0)
    {
        HashSet<WorldAssetKey> next = wanted.ToHashSet(), protectedSet = protectedAssets.ToHashSet();
        if (previousBudget != budget || !previousWanted.SetEquals(next) || !previousProtected.SetEquals(protectedSet))
            deferred.Clear();
        previousBudget = budget;
        previousWanted = next;
        previousProtected = protectedSet;
    }
}
