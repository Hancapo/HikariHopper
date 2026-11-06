namespace HikariHopper.ViewModels;

/// <summary>A collision or navmesh file opened for the 3D viewer: its name and its bytes.</summary>
public sealed record ModelFile(string Name, byte[] Data)
{
    /// <summary>The largest file the viewer reads.</summary>
    public const long MaxSize = 256L * 1024 * 1024;
}
