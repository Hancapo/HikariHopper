using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace HikariHopper.Infrastructure;

/// <summary>Native pickers through the focused window's <see cref="IStorageProvider"/>.</summary>
public sealed class AvaloniaFileDialogs : IFileDialogs
{
    public async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        IStorageProvider storage = Storage();
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(storage, startPath),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters,
        bool allowMultiple = false, string? startPath = null)
    {
        IStorageProvider storage = Storage();
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = filters.Select(ToFileType).ToArray(),
            SuggestedStartLocation = await StartFolder(storage, startPath),
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters,
        string? startPath = null)
    {
        IStorageProvider storage = Storage();
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = filters.Select(ToFileType).ToArray(),
            SuggestedStartLocation = await StartFolder(storage, startPath),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType ToFileType(FileFilter filter) => new(filter.Name) { Patterns = filter.Patterns };

    private static async Task<IStorageFolder?> StartFolder(IStorageProvider storage, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return folder is not null && Directory.Exists(folder) ? await storage.TryGetFolderFromPathAsync(folder) : null;
    }

    private static IStorageProvider Storage()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        Window? window = lifetime?.Windows.FirstOrDefault(window => window.IsActive) ?? lifetime?.MainWindow
            ?? throw new InvalidOperationException("No window is available for a file dialog.");
        return window.StorageProvider;
    }
}
