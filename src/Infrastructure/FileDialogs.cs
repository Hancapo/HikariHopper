namespace HikariHopper.Infrastructure;

/// <summary>A named file-type filter, e.g. <c>("Images", ["*.png", "*.jpg"])</c>.</summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>Native file pickers. View models depend on this instead of a window, so they stay testable.</summary>
public interface IFileDialogs
{
    Task<string?> PickFolderAsync(string title, string? startPath = null);

    Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters,
        bool allowMultiple = false, string? startPath = null);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters,
        string? startPath = null);
}

/// <summary>Process-wide services, replaced by fakes in tests.</summary>
public static class AppServices
{
    public static IFileDialogs Dialogs { get; set; } = new NullFileDialogs();

    /// <summary>The system clipboard; without an Avalonia window it reads nothing and ignores writes.</summary>
    public static IClipboardService Clipboard { get; set; } = new AvaloniaClipboardService();

    private sealed class NullFileDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title, string? startPath = null) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters,
            bool allowMultiple = false, string? startPath = null) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters,
            string? startPath = null) => Task.FromResult<string?>(null);
    }
}
