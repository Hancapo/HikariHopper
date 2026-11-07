namespace HikariHopper.ViewModels;

/// <summary>
/// A collision, navmesh or drawable file opened for the 3D viewer: its name and its bytes, and for a drawable the
/// texture dictionaries found beside it.
/// </summary>
public sealed record ModelFile(string Name, byte[] Data)
{
    /// <summary>The largest file the viewer reads.</summary>
    public const long MaxSize = 256L * 1024 * 1024;

    /// <summary>The .ytd files beside a drawable that may hold its textures, nearest first.</summary>
    public IReadOnlyList<byte[]> Dictionaries { get; init; } = [];
}
