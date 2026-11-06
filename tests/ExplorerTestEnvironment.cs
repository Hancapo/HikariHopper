using System.Collections.Concurrent;
using Avalonia.Headless;
using Avalonia.Threading;
using FiveFury;
using FiveFury.Cryptography;
using FiveFury.Rpf;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
using HikariHopper.ViewModels;
using Microsoft.Win32;

namespace HikariHopper.Tests;

/// <summary>Explorer tests share process-wide seams (settings key, key loader, Recycle Bin), so they run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExplorerCollection
{
    public const string Name = "Explorer view models";
}

/// <summary>
/// Per-test isolation: a scratch HKCU settings key, a scratch FiveFury key cache, fake game keys,
/// an in-memory clipboard and a fake Recycle Bin, plus a temporary <see cref="Work"/> folder.
/// </summary>
public abstract class ExplorerTestBase : IDisposable
{
    private const string ScratchRegistryRoot = @"Software\HikariHopperTests";

    private readonly string scratch;
    private readonly string previousRegistryPath;
    private readonly string previousKeyCache;
    private readonly Func<string, LoadedGame> previousGameLoader;
    private readonly IClipboardService previousClipboard;
    private readonly IFileDialogs previousDialogs;
    private readonly string registryPath;

    protected ExplorerTestBase()
    {
        scratch = Directory.CreateTempSubdirectory("hikari-vm-").FullName;
        Work = Directory.CreateDirectory(Path.Combine(scratch, "work")).FullName;
        Trash = Directory.CreateDirectory(Path.Combine(scratch, "trash")).FullName;

        previousRegistryPath = AppSettings.RegistryPath;
        registryPath = $@"{ScratchRegistryRoot}\{Guid.NewGuid():N}";
        AppSettings.RegistryPath = registryPath;
        previousKeyCache = GameKeys.CachePath;
        GameKeys.CachePath = Path.Combine(scratch, "keys.json");
        previousGameLoader = ExplorerViewModel.GameLoader;
        ExplorerViewModel.GameLoader = FakeLoadGame;
        previousClipboard = AppServices.Clipboard;
        AppServices.Clipboard = Clipboard;
        // Search indexes are cached beside the scratch game, never in the user's own folder.
        Core.Search.SearchIndex.CacheFolder = Path.Combine(scratch, "index-cache");
        Core.Search.SearchIndex.ForgetAll();
        previousDialogs = AppServices.Dialogs;
        AppServices.Dialogs = Dialogs;
        RecycleBin.Override = MoveToFakeTrash;
    }

    /// <summary>A temporary folder for games, archives and sources (Python's <c>tmp_path</c>).</summary>
    protected string Work { get; }

    /// <summary>Where the fake Recycle Bin moves items.</summary>
    protected string Trash { get; }

    protected FakeClipboard Clipboard { get; } = new();

    protected FakeDialogs Dialogs { get; } = new();

    /// <summary>Paths passed to the Recycle Bin, in order.</summary>
    protected ConcurrentQueue<string> TrashAttempts { get; } = new();

    /// <summary>Replaces the fake Recycle Bin; return false to simulate a refusal.</summary>
    protected Func<string, bool>? TrashHandler { get; set; }

    /// <summary>Blocks game loads until released (Python's patched <c>load_game_keys</c>).</summary>
    protected Func<string, LoadedGame>? LoadHook { get; set; }

    public static GameCrypto FakeCrypto { get; } = new(new byte[32]);

    public void Dispose()
    {
        RecycleBin.Override = null;
        AppServices.Dialogs = previousDialogs;
        AppServices.Clipboard = previousClipboard;
        ExplorerViewModel.GameLoader = previousGameLoader;
        GameKeys.CachePath = previousKeyCache;
        AppSettings.RegistryPath = previousRegistryPath;
        Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        using (RegistryKey? root = Registry.CurrentUser.OpenSubKey(ScratchRegistryRoot))
        {
            if (root is { SubKeyCount: 0, ValueCount: 0 })
                Registry.CurrentUser.DeleteSubKey(ScratchRegistryRoot, throwOnMissingSubKey: false);
        }
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>A game folder whose executable only marks the edition; keys are faked.</summary>
    protected string MakeGame(string name = "", string executable = "GTA5.exe")
    {
        string root = name.Length == 0 ? Work : Directory.CreateDirectory(Path.Combine(Work, name)).FullName;
        File.WriteAllBytes(Path.Combine(root, executable), "game"u8.ToArray());
        return root;
    }

    protected static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    protected static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    /// <summary>Builds an archive in memory: files are <c>path → text</c>, directories end with <c>/</c>.</summary>
    protected static RpfArchive Archive(string name, params (string Path, object Content)[] items)
    {
        var archive = new RpfArchive(name);
        foreach (var (path, content) in items)
        {
            switch (content)
            {
                case null:
                    archive.AddDirectory(path);
                    break;
                case string text:
                    archive.AddBinary(path, Bytes(text));
                    break;
                case byte[] data:
                    archive.AddBinary(path, data);
                    break;
                case RpfArchive nested:
                    archive.AddBinary(path, nested.Write());
                    break;
                default:
                    throw new ArgumentException($"Unsupported archive item: {content.GetType()}");
            }
        }
        return archive;
    }

    protected static string SaveArchive(string path, params (string Path, object Content)[] items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Archive(Path.GetFileName(path), items).Save(path);
        return path;
    }

    protected static RpfArchive ReadArchive(string path) => RpfArchive.Read(File.ReadAllBytes(path), Path.GetFileName(path));

    /// <summary>Reads a file from a saved archive, descending into nested RPFs on the way.</summary>
    protected static byte[]? ReadSavedEntry(string archivePath, string entryPath)
    {
        RpfArchive archive = ReadArchive(archivePath);
        string[] parts = entryPath.Split('/');
        for (int count = 1; count <= parts.Length; count++)
        {
            RpfEntry? entry = archive.Find(string.Join('/', parts[..count]));
            if (entry is null)
                return null;
            if (count == parts.Length)
                return entry is RpfFileEntry file ? file.ReadStandalone() : [];
            if (entry is RpfBinaryFileEntry binary && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                archive = RpfArchive.Read(binary.ReadStandalone(), binary.Name);
                parts = parts[count..];
                count = 0;
            }
        }
        return null;
    }

    protected static bool SavedEntryExists(string archivePath, string entryPath) =>
        ReadSavedEntry(archivePath, entryPath) is not null;

    /// <summary>Kept so explorer tests read as before; <c>[AvaloniaFact]</c> already runs them on the UI thread.</summary>
    protected static void OnUiThread(Action body) => body();

    protected static Task OnUiThreadAsync(Func<Task> body) => body();

    /// <summary>Waits for every queued operation and delivers results (Python's <c>waitForDone</c> + events).</summary>
    protected static void Settle(params ExplorerViewModel[] bridges)
    {
        for (int round = 0; round < 4; round++)
        {
            foreach (ExplorerViewModel bridge in bridges)
                Assert.True(bridge.OperationQueue.Wait(TimeSpan.FromSeconds(10)), "Operation did not finish");
            Dispatcher.UIThread.RunJobs();
        }
    }

    protected static void Settle(ExplorerTabsViewModel tabs) => Settle(tabs.Tabs.ToArray());

    protected static List<string> ListedNames(ExplorerViewModel bridge) =>
        bridge.EntriesModel.Select(item => item.Name).ToList();

    protected static int RowFor(ExplorerViewModel bridge, string name) =>
        bridge.EntriesModel.Select((item, index) => (item, index)).First(pair => pair.item.Name == name).index;

    protected static List<string> TreeLabels(ExplorerViewModel bridge) =>
        bridge.TreeModel.Select(item => item.Label).ToList();

    protected static TreeItem TreeRow(ExplorerViewModel bridge, string label) =>
        bridge.TreeModel.First(item => item.Label == label);

    private LoadedGame FakeLoadGame(string path)
    {
        if (LoadHook is not null)
            return LoadHook(path);
        return FakeLoadedGame(path);
    }

    protected static LoadedGame FakeLoadedGame(string path)
    {
        string root = Path.GetFullPath(path);
        if (!Directory.Exists(root))
            throw new ArgumentException($"Game folder does not exist: {root}");
        GameTarget target = File.Exists(Path.Combine(root, "GTA5_Enhanced.exe"))
            ? GameTarget.Enhanced
            : File.Exists(Path.Combine(root, "GTA5.exe"))
                ? GameTarget.Legacy
                : throw new ArgumentException("Select the GTA V folder containing GTA5.exe or GTA5_Enhanced.exe");
        return new LoadedGame(root.TrimEnd(Path.DirectorySeparatorChar), target, FakeCrypto);
    }

    private bool MoveToFakeTrash(string path)
    {
        TrashAttempts.Enqueue(path);
        if (TrashHandler is not null)
            return TrashHandler(path);
        string destination = Path.Combine(Trash, Guid.NewGuid().ToString("N"), Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(path))
            Directory.Move(path, destination);
        else
            File.Move(path, destination);
        return true;
    }

    protected sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync() => Task.FromResult(Text);

        public bool HasImage => false;

        public event EventHandler? Changed { add { } remove { } }

        public Task<FiveFury.Textures.RgbaImage?> GetImageAsync() => Task.FromResult<FiveFury.Textures.RgbaImage?>(null);

        public Task<bool> SetImageAsync(FiveFury.Textures.RgbaImage image) => Task.FromResult(false);

        /// <summary>The files on the clipboard, as a copy here or in Windows Explorer leaves them.</summary>
        public List<string> Files { get; } = [];

        public bool HasFiles => Files.Count > 0;

        public Task<IReadOnlyList<string>> GetFilesAsync() => Task.FromResult<IReadOnlyList<string>>([.. Files]);

        public Task<bool> SetFilesAsync(IReadOnlyList<string> paths)
        {
            Files.Clear();
            Files.AddRange(paths);
            Text = null;
            return Task.FromResult(true);
        }
    }

    protected sealed class FakeDialogs : IFileDialogs
    {
        public Queue<string?> Folders { get; } = new();

        public Queue<IReadOnlyList<string>> OpenFiles { get; } = new();

        public List<(string Title, IReadOnlyList<FileFilter>? Filters, string? StartPath)> Requests { get; } = [];

        public Task<string?> PickFolderAsync(string title, string? startPath = null)
        {
            Requests.Add((title, null, startPath));
            return Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);
        }

        public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters,
            bool allowMultiple = false, string? startPath = null)
        {
            Requests.Add((title, filters, startPath));
            return Task.FromResult(OpenFiles.Count > 0 ? OpenFiles.Dequeue() : (IReadOnlyList<string>)[]);
        }

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters,
            string? startPath = null) => Task.FromResult<string?>(null);
    }
}
