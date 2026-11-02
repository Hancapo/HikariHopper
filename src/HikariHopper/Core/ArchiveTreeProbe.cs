using System.Collections.Concurrent;
using FiveFury.Cryptography;
using FiveFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// Whether an RPF holds anything the folder tree can expand into (a folder or a nested RPF), read from its table of
/// contents in the background. Reading an archive's table takes milliseconds, too long to do for every row while the tree
/// is built, so a row asks here and gets <c>true</c> until the answer is known; <see cref="Found"/> then reports it.
/// </summary>
/// <remarks>
/// Results are keyed by the root file's size and write time, so an archive that changes on disk is read again. The worker
/// opens its own read-only copy of each root archive and never touches the archives the explorer has open. An archive that
/// can't be read counts as having children, so it can still be expanded to show why.
/// </remarks>
public sealed class ArchiveTreeProbe
{
    private readonly ConcurrentDictionary<string, bool> results = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Key, string RootFile, string NestedPath, GameCrypto? Crypto)> queue = new();
    private int working;

    /// <summary>Raised on a worker thread when an archive turns out to have nothing to expand.</summary>
    public event EventHandler? Found;

    /// <summary>
    /// Whether the archive at <paramref name="nestedPath"/> inside <paramref name="rootFile"/> (or the root itself, for an
    /// empty path) has tree children; <c>true</c> while that isn't known yet, with a read queued.
    /// </summary>
    public bool HasTreeChildren(string rootFile, string nestedPath, GameCrypto? crypto)
    {
        string key;
        try
        {
            var info = new FileInfo(rootFile);
            if (!info.Exists)
                return true;
            key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{nestedPath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
        if (results.TryGetValue(key, out bool known))
            return known;
        if (pending.TryAdd(key, 0))
        {
            queue.Enqueue((key, rootFile, nestedPath, crypto));
            if (Interlocked.CompareExchange(ref working, 1, 0) == 0)
                Task.Run(Work);
        }
        return true;
    }

    private void Work()
    {
        ArchiveNode? root = null;
        string rootFile = "";
        try
        {
            while (true)
            {
                while (queue.TryDequeue(out var request))
                {
                    bool has = true;
                    try
                    {
                        // Requests come a folder at a time, so the last root stays open for the next one.
                        if (root is null || !string.Equals(rootFile, request.RootFile, StringComparison.OrdinalIgnoreCase))
                        {
                            root?.Close();
                            root = null;
                            root = ArchiveNode.OpenRoot(request.RootFile, request.Crypto);
                            rootFile = request.RootFile;
                        }
                        ArchiveNode? archive = request.NestedPath.Length == 0 ? root
                            : root.FindEntry(request.NestedPath) is ({ } node, RpfBinaryFileEntry entry) ? node.OpenNested(entry) : null;
                        if (archive is not null)
                            has = archive.Archive.Root.Children.Any(child => child is RpfDirectoryEntry
                                || child.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception)
                    {
                        // Unreadable here, for whatever reason: leave the arrow, and let opening it report the problem.
                        root?.Close();
                        root = null;
                    }
                    results[request.Key] = has;
                    pending.TryRemove(request.Key, out _);
                    if (!has)
                        Found?.Invoke(this, EventArgs.Empty);
                }
                Volatile.Write(ref working, 0);
                // A request queued after the loop drained but before the flag was cleared still gets a worker.
                if (queue.IsEmpty || Interlocked.CompareExchange(ref working, 1, 0) != 0)
                    return;
            }
        }
        finally
        {
            root?.Close();
        }
    }
}
