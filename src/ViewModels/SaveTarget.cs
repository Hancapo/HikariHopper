namespace HikariHopper.ViewModels;

/// <summary>Where a viewer saves the file it changed: the 3D viewer's drawable, the text viewer's text.</summary>
/// <param name="Write">Writes the file's new bytes where it lives, off the UI thread.</param>
/// <param name="Prepare">Run first, on the UI thread: lets go of what holds the file open (its archive).</param>
/// <param name="Finished">Told on the UI thread whether it was saved, so what lists the file can catch up.</param>
internal sealed record SaveTarget(Action<byte[]> Write, Action? Prepare = null, Action<bool>? Finished = null);
