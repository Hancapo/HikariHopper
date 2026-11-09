using CommonFury.Textures;
using FiveFury.Textures;

namespace HikariHopper.ViewModels;

/// <summary>
/// What a texture document saved: its textures, and its file as written (a .ytd, or a .ydr with its textures in it).
/// </summary>
public sealed record SavedDocument(IReadOnlyList<Texture> Textures, byte[] Data);

/// <summary>Result of writing an edited document back into its source file or RPF.</summary>
public sealed record SourceWriteResult(string RootPath, bool Success);

/// <summary>An entry of <see cref="TextureViewerViewModel.FormatOptions"/>: <c>Label</c> is shown, <c>Value</c> is
/// passed to <see cref="TextureViewerViewModel.ChangeSelectedFormat"/>.</summary>
public sealed record TextureFormatOption(string Label, string Value);

/// <summary>A texture size, e.g. the rounded power-of-two size of the current texture (Python <c>{"width", "height"}</c>).</summary>
public sealed record TextureSize(int Width, int Height);

/// <summary>What a tool dialog needs to know of a selected texture, to say what it is now and what it would become.</summary>
public sealed record TextureFacts(string Name, int Width, int Height, TextureFormat Format, int MipCount, long DataSize);

/// <summary>How an undo entry restores the dictionary.</summary>
public enum TextureUndoKind
{
    /// <summary>Puts one texture back at its row.</summary>
    Replace,
    /// <summary>Re-inserts one removed texture at its row.</summary>
    Insert,
    /// <summary>Restores the entire texture list.</summary>
    Snapshot,
}

/// <summary>One undo step. <c>Textures</c> holds the single texture of Replace/Insert, or the whole previous list for a
/// Snapshot. YDR rename and deletion bookkeeping is restored with it.</summary>
public sealed record TextureUndoAction(
    string Label,
    TextureUndoKind Kind,
    int Row,
    IReadOnlyList<Texture> Textures,
    int PreviousRevision,
    IReadOnlyList<int> Selection,
    IReadOnlyList<KeyValuePair<string, string>> Renames,
    IReadOnlySet<string> DeletedOrigins)
{
    /// <summary>Encoded bytes this entry retains, used for the undo memory budget.</summary>
    public long DataSize => Textures.Sum(texture => (long)texture.Data.Length);
}
