using HikariHopper.Core;

namespace HikariHopper.ViewModels;

/// <summary>A text file opened for the text viewer: the text as read, and where Save puts it back.</summary>
public sealed record TextFile(TextDocument Document)
{
    /// <summary>Where the viewer saves the text, into its RPF or over the loose file; none where neither can be had (Save as only).</summary>
    internal SaveTarget? SaveTarget { get; init; }
}
