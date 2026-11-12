using FiveFury.Cryptography;
using FiveFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// Applies one edit to an archive on disk: opens the root RPF afresh, walks to the nested
/// archive concerned, mutates it, writes every edited nested archive back into its owner and
/// atomically replaces the root file.
/// </summary>
public static class ArchiveWriter
{
    /// <param name="rootPath">The root RPF on disk.</param>
    /// <param name="archivePrefix">Full path of the nested archive to edit; empty for the root.</param>
    public static void Mutate(string rootPath, GameCrypto? crypto, string archivePrefix, Action<RpfArchive> mutation) =>
        Edit(rootPath, crypto, root =>
        {
            ArchiveNode target = NestedArchive(root, archivePrefix);
            mutation(target.Archive);
            return target;
        });

    /// <summary>Replaces the file at <paramref name="entryPath"/> (a full path from the root) with standalone bytes.</summary>
    public static void ReplaceEntry(string rootPath, GameCrypto? crypto, string entryPath, byte[] data) =>
        Edit(rootPath, crypto, root =>
        {
            var (node, entry) = root.FindEntry(entryPath)
                ?? throw new ArgumentException($"Could not find archive entry: {entryPath}");
            switch (entry)
            {
                case RpfResourceFileEntry resource:
                    resource.Replace(data);
                    break;
                case RpfBinaryFileEntry binary:
                    binary.Replace(data, binary.Compression);
                    break;
                default:
                    throw new ArgumentException($"Could not find archive entry: {entryPath}");
            }
            return node;
        });

    /// <summary>Adds a standalone file: RSC7 data becomes a resource entry, anything else a binary.</summary>
    public static void AddStandalone(RpfArchive archive, string path, byte[] data)
    {
        if (data.AsSpan().StartsWith("RSC7"u8))
            archive.AddResource(path, data);
        else
            archive.AddBinary(path, data);
    }

    /// <summary>The loaded archive whose prefix is exactly <paramref name="prefix"/>, opening nested archives on the way.</summary>
    public static ArchiveNode NestedArchive(ArchiveNode root, string prefix)
    {
        ArchiveNode current = root;
        string remaining = prefix.Replace('\\', '/').Trim('/');
        while (remaining.Length > 0)
        {
            string[] parts = remaining.Split('/');
            ArchiveNode? next = null;
            for (int count = 1; count <= parts.Length && next is null; count++)
            {
                if (current.Archive.Find(string.Join('/', parts[..count])) is RpfBinaryFileEntry binary
                    && binary.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    next = current.OpenNested(binary);
                    remaining = string.Join('/', parts[count..]);
                }
            }
            current = next ?? throw new ArgumentException($"Could not find nested archive: {prefix}");
        }
        return current;
    }

    // Each edit reads the root RPF afresh and replaces it whole, so two at once would lose the first one's change: an
    // explorer change, a texture save and a model save each wait for the one before to be written.
    private static readonly Lock EditGate = new();

    private static void Edit(string rootPath, GameCrypto? crypto, Func<ArchiveNode, ArchiveNode> edit)
    {
        lock (EditGate)
            EditAlone(rootPath, crypto, edit);
    }

    private static void EditAlone(string rootPath, GameCrypto? crypto, Func<ArchiveNode, ArchiveNode> edit)
    {
        string path = Path.GetFullPath(rootPath);
        string staging = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            ArchiveNode root = ArchiveNode.OpenRoot(path, crypto);
            try
            {
                for (ArchiveNode? node = edit(root); node?.Parent is not null; node = node.Parent)
                    node.Entry!.Replace(node.Archive.Write(crypto), node.Entry.Compression);
                using var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                // Measured against the archive as it was, and stopped mid-write if asked: the staging file goes, and
                // the archive is as it was.
                root.Archive.Write(OperationProgress.Current.Track(output, new FileInfo(path).Length, Path.GetFileName(path)), crypto);
                output.Flush(flushToDisk: true);
            }
            finally { root.Close(); }
            // The last moment to stop: after this the archive is the new one.
            OperationProgress.Current.Check();
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }
}
